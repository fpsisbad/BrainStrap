using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BrainStrap.Core;

/// <summary>
/// BrainStrap's own Roblox installer and updater. It needs nothing else on the PC: no Roblox launcher and no other
/// bootstrapper. It downloads the official, unmodified Roblox player packages straight from Roblox's own download
/// servers (the same files Roblox's installer fetches), checks every package against the checksum in Roblox's
/// manifest, and unpacks them into BrainStrap's folder.
///
/// If anything looks unfamiliar (a package this version doesn't know where to put, a checksum that doesn't match),
/// it stops and the caller falls back to whatever Roblox is already installed, or to Roblox's own launcher.
/// </summary>
internal static class RobloxDeploy
{
    /// <summary>%LOCALAPPDATA%\BrainStrap\Roblox\Versions</summary>
    public static string Root => Directory.CreateDirectory(Path.Combine(Paths.Data, "Roblox", "Versions")).FullName;
    static string Cache => Directory.CreateDirectory(Path.Combine(Paths.Data, "Roblox", "Downloads")).FullName;
    const string Marker = ".brainstrap-complete";

    static readonly string[] Mirrors =
    {
        "https://setup.rbxcdn.com", "https://setup-aws.rbxcdn.com", "https://setup-ak.rbxcdn.com", "https://roblox-setup.cachefly.net",
    };

    /// <summary>Where each package of the Windows player unpacks to, relative to the version folder.</summary>
    static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = "",
        ["shaders.zip"] = "shaders",
        ["ssl.zip"] = "ssl",
        ["WebView2.zip"] = "",
        ["WebView2RuntimeInstaller.zip"] = "WebView2RuntimeInstaller",
        ["content-avatar.zip"] = @"content\avatar",
        ["content-configs.zip"] = @"content\configs",
        ["content-fonts.zip"] = @"content\fonts",
        ["content-sky.zip"] = @"content\sky",
        ["content-sounds.zip"] = @"content\sounds",
        ["content-textures2.zip"] = @"content\textures",
        ["content-models.zip"] = @"content\models",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain",
        ["content-textures3.zip"] = @"PlatformContent\pc\textures",
        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages",
        ["extracontent-translations.zip"] = @"ExtraContent\translations",
        ["extracontent-models.zip"] = @"ExtraContent\models",
        ["extracontent-textures.zip"] = @"ExtraContent\textures",
        ["extracontent-places.zip"] = @"ExtraContent\places",
    };

    internal sealed record Package(string Name, string Md5, long Packed, long Size);
    internal sealed record Step(string Stage, double Fraction, string Detail);

    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromMinutes(15),
        DefaultRequestHeaders = { { "User-Agent", "BrainStrap/2" } },
    };

    public static bool IsVersionName(string? v) => v != null && Regex.IsMatch(v, "^version-[0-9a-f]{16}$");

    /// <summary>A finished BrainStrap-managed install of this version, or null.</summary>
    public static InstalledVersion? Installed(string version)
    {
        if (!IsVersionName(version)) return null;
        var dir = Path.Combine(Root, version);
        var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
        return File.Exists(exe) && File.Exists(Path.Combine(dir, Marker)) ? new InstalledVersion(dir, version, File.GetLastWriteTimeUtc(exe)) : null;
    }

    /// <summary>Total size on disk of BrainStrap-managed Roblox versions.</summary>
    public static long DiskUse()
    {
        try { return new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }

    public static List<Package> ParseManifest(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count < 5 || !lines[0].StartsWith('v')) throw new InvalidDataException("Roblox's package list wasn't in the expected format.");
        var list = new List<Package>();
        for (var i = 1; i + 3 < lines.Count; i += 4)
        {
            if (!Regex.IsMatch(lines[i], @"^[\w\-\.]{1,80}$") || !Regex.IsMatch(lines[i + 1], "^[0-9a-fA-F]{32}$") ||
                !long.TryParse(lines[i + 2], out var packed) || !long.TryParse(lines[i + 3], out var size))
                throw new InvalidDataException("Roblox's package list wasn't in the expected format.");
            list.Add(new Package(lines[i], lines[i + 1].ToLowerInvariant(), packed, size));
        }
        return list;
    }

    /// <summary>Downloads, checks and unpacks one Roblox version. Safe to call again after a failure or a cancel.</summary>
    public static async Task<InstalledVersion> InstallAsync(string version, IProgress<Step>? progress = null, CancellationToken ct = default)
    {
        if (!IsVersionName(version)) throw new InvalidOperationException("Roblox reported a version name BrainStrap doesn't recognise.");
        if (Installed(version) is { } have) return have;

        progress?.Report(new Step("Getting Roblox's package list…", 0, version));
        string? mirror = null, manifest = null;
        foreach (var m in Mirrors)
        {
            try
            {
                manifest = await Http.GetStringAsync($"{m}/{version}-rbxPkgManifest.txt", ct);
                mirror = m;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Logger.Warn($"manifest from {m}: {ex.GetType().Name}"); }
        }
        if (mirror == null || manifest == null) throw new IOException("Couldn't reach Roblox's download servers.");

        var packages = ParseManifest(manifest).Where(p => p.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        var unknown = packages.Where(p => !Map.ContainsKey(p.Name)).Select(p => p.Name).ToList();
        if (unknown.Count > 0) throw new NotSupportedException("Roblox added a package this BrainStrap version doesn't know yet: " + string.Join(", ", unknown));
        if (!packages.Any(p => p.Name.Equals("RobloxApp.zip", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Roblox's package list has no player in it.");

        var free = new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace;
        var need = packages.Sum(p => p.Packed + p.Size) + (64L << 20);
        if (free < need) throw new IOException($"Not enough disk space: Roblox needs about {need / 1048576} MB free.");

        // 1) download, three at a time, each checked against Roblox's checksum
        long total = packages.Sum(p => p.Packed), done = 0;
        using var gate = new SemaphoreSlim(3);
        await Task.WhenAll(packages.Select(async p =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await DownloadAsync(mirror, version, p, bytes =>
                {
                    var d = Interlocked.Add(ref done, bytes);
                    progress?.Report(new Step("Downloading Roblox…", 0.02 + 0.78 * d / Math.Max(1, total), $"{d / 1048576} of {total / 1048576} MB"));
                }, ct);
            }
            finally { gate.Release(); }
        }));

        // 2) unpack into a side folder, then swap it in, so a half-finished install is never used
        var temp = Path.Combine(Root, version + ".partial");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        var n = 0;
        foreach (var p in packages)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new Step("Unpacking Roblox…", 0.8 + 0.18 * n++ / packages.Count, p.Name));
            var target = Path.Combine(temp, Map[p.Name]);
            await Task.Run(() => Extract(Path.Combine(Cache, version + "-" + p.Name), target), ct);
        }
        await File.WriteAllTextAsync(Path.Combine(temp, "AppSettings.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n\t<ContentFolder>content</ContentFolder>\r\n\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n</Settings>\r\n", ct);
        if (!File.Exists(Path.Combine(temp, "RobloxPlayerBeta.exe"))) throw new InvalidDataException("The Roblox player was missing after unpacking.");

        var final = Path.Combine(Root, version);
        if (Directory.Exists(final)) Directory.Delete(final, true);
        Directory.Move(temp, final);
        await File.WriteAllTextAsync(Path.Combine(final, Marker), DateTime.UtcNow.ToString("o"), ct);
        try { Directory.Delete(Cache, true); } catch { }

        progress?.Report(new Step("Finishing…", 0.99, ""));
        EnsureWebView2(final);
        Logger.Info("installed Roblox " + version);
        return Installed(version) ?? throw new InvalidDataException("The Roblox install didn't complete.");
    }

    static async Task DownloadAsync(string mirror, string version, Package p, Action<long> advanced, CancellationToken ct)
    {
        var file = Path.Combine(Cache, version + "-" + p.Name);
        if (File.Exists(file) && await Md5Async(file, ct) == p.Md5) { advanced(p.Packed); return; }

        Exception? last = null;
        foreach (var m in new[] { mirror }.Concat(Mirrors.Where(x => x != mirror)).Take(3))
        {
            long got = 0;
            try
            {
                using var resp = await Http.GetAsync($"{m}/{version}-{p.Name}", HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                await using (var dst = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                {
                    var buf = new byte[1 << 16];
                    int read;
                    while ((read = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, read), ct);
                        got += read;
                        advanced(read);
                    }
                }
                if (await Md5Async(file, ct) == p.Md5) return;
                last = new InvalidDataException(p.Name + " didn't match Roblox's checksum.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { last = ex; }
            advanced(-got);     // take this attempt back out of the progress bar
            try { File.Delete(file); } catch { }
        }
        throw new IOException("Couldn't download " + p.Name + ".", last);
    }

    static async Task<string> Md5Async(string file, CancellationToken ct)
    {
        using var md5 = MD5.Create();
        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
        return Convert.ToHexString(await md5.ComputeHashAsync(fs, ct)).ToLowerInvariant();
    }

    /// <summary>Unzips with a guard so no entry can land outside the target folder.</summary>
    static void Extract(string zip, string target)
    {
        Directory.CreateDirectory(target);
        var root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zip);
        foreach (var e in archive.Entries)
        {
            var rel = e.FullName.Replace('/', '\\').TrimStart('\\');
            if (rel.Length == 0) continue;
            var dest = Path.GetFullPath(Path.Combine(root, rel));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A Roblox package had an unexpected path in it.");
            if (rel.EndsWith('\\')) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
        }
    }

    /// <summary>Roblox's menus use Microsoft's WebView2 runtime. Windows 11 has it; on older PCs Roblox ships Microsoft's installer, which is run here if needed.</summary>
    static void EnsureWebView2(string folder)
    {
        try
        {
            const string id = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            bool Has(RegistryKey hive, string path) { using var k = hive.OpenSubKey(path); return k?.GetValue("pv") is string v && v.Length > 0 && v != "0.0.0.0"; }
            if (Has(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + id) || Has(Registry.LocalMachine, @"SOFTWARE\" + id) || Has(Registry.CurrentUser, @"Software\" + id)) return;
            var setup = Path.Combine(folder, "WebView2RuntimeInstaller", "MicrosoftEdgeWebview2Setup.exe");
            if (!File.Exists(setup)) return;
            Logger.Info("installing the WebView2 runtime Roblox ships with");
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup, "/silent /install") { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(120000);
        }
        catch (Exception ex) { Logger.Warn("webview2: " + ex.Message); }
    }

    /// <summary>Removes BrainStrap-managed versions other than the one given. Refuses while Roblox is running.</summary>
    public static int Prune(string keep)
    {
        if (RobloxInstall.IsRobloxRunning()) return 0;
        var n = 0;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals(keep, StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.StartsWith("version-", StringComparison.Ordinal)) continue;
            try { Directory.Delete(dir, true); n++; } catch (Exception ex) { Logger.Warn("prune " + name + ": " + ex.Message); }
        }
        return n;
    }
}
