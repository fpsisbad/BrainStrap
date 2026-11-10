using System.Security.Cryptography;
using System.Text.Json;

namespace BrainStrap.Core;

internal sealed class ModEntry
{
    public string Rel { get; set; } = "";
    public bool HadOriginal { get; set; }
    public string Hash { get; set; } = "";
}

internal sealed class ModState
{
    /// <summary>version folder name -> files BrainStrap put there</summary>
    public Dictionary<string, List<ModEntry>> Versions { get; set; } = new();
}

internal sealed record ModFile(string Rel, string FullPath, bool Allowed, string? Reason);

/// <summary>
/// Copies the user's own files from %LOCALAPPDATA%\BrainStrap\Mods into the Roblox version folder (sounds, cursors,
/// sky textures, fonts...) and puts the originals back when a mod is removed or mods are switched off.
///
/// Safety rules, enforced in code:
///   - only the content folders listed below can be touched
///   - program and script files (.exe .dll .bat .ps1 ...) are never copied
///   - links / reparse points are ignored, and every destination is checked to stay inside the version folder
/// </summary>
internal static class ModManager
{
    static readonly string[] AllowedRoots = { "content", "PlatformContent", "ExtraContent" };

    static readonly HashSet<string> BlockedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf",
        ".scr", ".msi", ".com", ".lnk", ".cpl", ".jar", ".hta", ".reg", ".pif", ".gadget",
    };

    const long MaxFileBytes = 256L * 1024 * 1024;

    static string StateFile => Path.Combine(Paths.State, "mods.json");

    static ModState LoadState()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<ModState>(File.ReadAllText(StateFile)) ?? new ModState();
        }
        catch
        {
        }
        return new ModState();
    }

    static void SaveState(ModState s)
    {
        try { File.WriteAllText(StateFile, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    static string Sha(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>Lists every file in the Mods folder with a verdict on whether BrainStrap will apply it.</summary>
    public static List<ModFile> Scan()
    {
        var list = new List<ModFile>();
        var root = Paths.Mods;
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            IgnoreInaccessible = true,
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", opts))
        {
            var rel = Path.GetRelativePath(root, file);
            var first = rel.Split(Path.DirectorySeparatorChar)[0];

            if (!AllowedRoots.Contains(first, StringComparer.OrdinalIgnoreCase))
                list.Add(new ModFile(rel, file, false, "must be inside content, PlatformContent or ExtraContent"));
            else if (BlockedExt.Contains(Path.GetExtension(file)))
                list.Add(new ModFile(rel, file, false, "program and script files are never copied"));
            else if (new FileInfo(file).Length > MaxFileBytes)
                list.Add(new ModFile(rel, file, false, "file is larger than 256 MB"));
            else
                list.Add(new ModFile(rel, file, true, null));
        }
        return list.OrderBy(m => m.Rel, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string? SafeTarget(string versionDir, string rel)
    {
        var baseDir = Path.GetFullPath(versionDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(versionDir, rel));
        return target.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? target : null;
    }

    /// <summary>Makes the version folder match the Mods folder. Pass enabled=false to put every original back.</summary>
    public static (int applied, int reverted, int skipped) Reconcile(string versionDir, bool enabled)
    {
        int applied = 0, reverted = 0, skipped = 0;
        var ver = new DirectoryInfo(versionDir).Name;
        var state = LoadState();
        if (!state.Versions.TryGetValue(ver, out var entries)) state.Versions[ver] = entries = new List<ModEntry>();

        var wanted = enabled ? Scan().Where(m => m.Allowed).ToList() : new List<ModFile>();
        skipped = enabled ? Scan().Count(m => !m.Allowed) : 0;

        // take out anything that is no longer wanted
        foreach (var e in entries.ToList())
        {
            if (wanted.Any(w => w.Rel.Equals(e.Rel, StringComparison.OrdinalIgnoreCase))) continue;
            Revert(versionDir, ver, e);
            entries.Remove(e);
            reverted++;
        }

        // put in what is wanted
        foreach (var w in wanted)
        {
            var target = SafeTarget(versionDir, w.Rel);
            if (target == null) { skipped++; continue; }

            var hash = Sha(w.FullPath);
            var e = entries.FirstOrDefault(x => x.Rel.Equals(w.Rel, StringComparison.OrdinalIgnoreCase));
            if (e != null && e.Hash == hash && File.Exists(target) && Sha(target) == hash) continue;   // already in place

            if (e == null)
            {
                e = new ModEntry { Rel = w.Rel, HadOriginal = File.Exists(target) };
                if (e.HadOriginal)
                {
                    var bak = Path.Combine(Paths.Backups, ver, w.Rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(bak)!);
                    File.Copy(target, bak, true);
                }
                entries.Add(e);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(w.FullPath, target, true);
            e.Hash = hash;
            applied++;
        }

        if (entries.Count == 0) state.Versions.Remove(ver);
        SaveState(state);
        if (applied + reverted > 0) Logger.Info($"mods: {applied} applied, {reverted} reverted");
        return (applied, reverted, skipped);
    }

    static void Revert(string versionDir, string ver, ModEntry e)
    {
        try
        {
            var target = SafeTarget(versionDir, e.Rel);
            if (target == null) return;
            var bak = Path.Combine(Paths.Backups, ver, e.Rel);
            if (e.HadOriginal && File.Exists(bak)) File.Copy(bak, target, true);
            else if (File.Exists(target)) File.Delete(target);
        }
        catch (Exception ex)
        {
            Logger.Warn("could not revert " + e.Rel + ": " + ex.Message);
        }
    }

    /// <summary>Puts the originals back in one version folder.</summary>
    public static void RevertAll(string versionDir) => Reconcile(versionDir, false);

    /// <summary>For uninstall: undo mods in every Roblox version folder BrainStrap ever touched.</summary>
    public static void RevertEverywhere()
    {
        foreach (var v in RobloxInstall.List()) RevertAll(v.Folder);
    }
}
