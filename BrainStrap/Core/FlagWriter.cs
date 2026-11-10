using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BrainStrap.Core;

/// <summary>
/// Writes ClientSettings\ClientAppSettings.json inside the Roblox version folder, the same file Roblox's own
/// "FastFlag" support reads. BrainStrap remembers a hash of what it wrote so it only ever deletes its own file.
/// </summary>
internal static class FlagWriter
{
    static string StateFile => Path.Combine(Paths.State, "flags.json");
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string FlagsPath(string versionDir) => Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");

    static Dictionary<string, string> LoadState()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StateFile)) ?? new();
        }
        catch
        {
        }
        return new();
    }

    static void SaveState(Dictionary<string, string> s)
    {
        try { File.WriteAllText(StateFile, JsonSerializer.Serialize(s)); } catch { }
    }

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string Render(IDictionary<string, object> flags) => JsonSerializer.Serialize(flags, Pretty);

    /// <summary>Writes the flags, or removes BrainStrap's earlier file when there are none.</summary>
    public static void Apply(string versionDir, IDictionary<string, object> flags)
    {
        if (flags.Count == 0)
        {
            RemoveIfOurs(versionDir);
            return;
        }

        var verName = new DirectoryInfo(versionDir).Name;
        var path = FlagsPath(versionDir);
        var json = Render(flags);
        var state = LoadState();

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            var ours = state.TryGetValue(verName, out var h) && h == Hash(existing);
            if (existing == json) { state[verName] = Hash(json); SaveState(state); return; }
            if (!ours)
            {
                // someone else's file: keep a copy once before replacing it
                var bak = Path.Combine(Paths.Backups, verName);
                Directory.CreateDirectory(bak);
                var dst = Path.Combine(bak, "ClientAppSettings.json.orig");
                if (!File.Exists(dst)) File.Copy(path, dst);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        state[verName] = Hash(json);
        SaveState(state);
        Logger.Info($"wrote {flags.Count} flag(s) to {verName}");
    }

    /// <summary>Deletes ClientAppSettings.json only if it is byte-for-byte what BrainStrap wrote.</summary>
    public static void RemoveIfOurs(string versionDir)
    {
        try
        {
            var verName = new DirectoryInfo(versionDir).Name;
            var path = FlagsPath(versionDir);
            var state = LoadState();
            if (!File.Exists(path) || !state.TryGetValue(verName, out var h)) return;
            if (h != Hash(File.ReadAllText(path)))
            {
                Logger.Info("ClientAppSettings.json was changed by something else; leaving it alone");
                return;
            }
            File.Delete(path);
            var dir = Path.GetDirectoryName(path)!;
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            state.Remove(verName);
            SaveState(state);
        }
        catch (Exception ex)
        {
            Logger.Error("could not remove flags file", ex);
        }
    }

    /// <summary>For uninstall: remove our flags file from every version folder we ever wrote to.</summary>
    public static void RemoveEverywhere()
    {
        foreach (var v in RobloxInstall.List()) RemoveIfOurs(v.Folder);
    }
}
