using System.Text.Json;
using System.Text.RegularExpressions;
using BrainStrap.Core.Servers;
using Microsoft.Win32;

namespace BrainStrap.Core;

internal sealed record Check(string Title, string Detail, string Level, string? FixLabel = null, Func<string>? Fix = null);

/// <summary>Roblox Repair Center: checks the install and BrainStrap's own changes, with a one-click fix for each problem.</summary>
internal static class RepairCenter
{
    public static async Task<List<Check>> RunAsync(Settings s)
    {
        var list = new List<Check>();
        var installed = RobloxInstall.Newest();
        if (installed == null)
        {
            list.Add(new Check("Roblox is not installed", "Press Play Roblox on the Home page and BrainStrap downloads it.", "bad", "Open roblox.com/download", () => { Open("https://www.roblox.com/download"); return "Opened the download page."; }));
            return list;
        }
        list.Add(new Check("Roblox player found", installed.Name, "ok"));

        var latest = await RobloxInstall.LatestVersionAsync();
        if (latest == null) list.Add(new Check("Couldn't check for updates", "Roblox's version service didn't answer. Check your internet.", "warn"));
        else if (!latest.Equals(installed.Name, StringComparison.OrdinalIgnoreCase))
            list.Add(new Check("Roblox update waiting", "It installs automatically the next time you press Play.", "warn"));
        else list.Add(new Check("Roblox is up to date", latest, "ok"));

        if (ProtocolHandler.IsRegisteredToUs()) list.Add(new Check("Play button opens BrainStrap", "", "ok"));
        else list.Add(new Check("Play button opens Roblox's own launcher", "Your BrainStrap settings won't apply when launching from the website.", "warn", "Register BrainStrap",
            () => { Installer.Install(s); return "Registered."; }));

        list.Add(ProtocolHandler.IsBrainStrapCommand(ProtocolHandler.CurrentLinkCommand())
            ? new Check("roblox:// join links open BrainStrap", "Used for joining servers and for Quick Play.", "ok")
            : new Check("roblox:// join links don't open BrainStrap", "Joining still works from BrainStrap's own buttons. Registering makes links from other places use your setup too.", "warn", "Register BrainStrap",
                () => { Installer.Install(s); return "Registered."; }));

        var flagsFile = FlagWriter.FlagsPath(installed.Folder);
        if (File.Exists(flagsFile))
        {
            try
            {
                using var _ = JsonDocument.Parse(File.ReadAllText(flagsFile));
                list.Add(new Check("Graphics flag file is valid", "", "ok"));
            }
            catch
            {
                list.Add(new Check("Graphics flag file is broken", "Roblox may ignore it or misbehave.", "bad", "Remove the file", () => { File.Delete(flagsFile); return "Removed. BrainStrap writes a fresh one next launch."; }));
            }
        }

        var report = FlagValidator.ValidateProfile(s.Current);
        if (report.Count(r => r.Level == "bad") > 0)
            list.Add(new Check($"{report.Count(r => r.Level == "bad")} custom flag(s) rejected", string.Join(" · ", report.Where(r => r.Level == "bad").Select(r => r.Flag).Take(4)), "warn"));

        var mods = ModManager.Scan();
        if (mods.Any(m => !m.Allowed))
            list.Add(new Check($"{mods.Count(m => !m.Allowed)} mod file(s) skipped", "They are in the wrong folder or are program files.", "warn"));
        list.Add(new Check("Mods", mods.Count == 0 ? "None installed" : $"{mods.Count(m => m.Allowed)} file(s) ready", "ok", mods.Count > 0 ? "Put original files back" : null,
            mods.Count > 0 ? () => { ModManager.RevertAll(installed.Folder); return "Originals restored for this Roblox version."; } : null));

        var all = RobloxInstall.List();
        if (all.Count > 1)
            list.Add(new Check($"{all.Count - 1} old Roblox version(s)", "Leftovers from updates, taking disk space.", "warn", "Remove old versions",
                () => { var (n, p) = RobloxInstall.PruneOld(); return p ?? $"Removed {n}."; }));

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(installed.Folder)!);
            var gb = drive.AvailableFreeSpace / 1073741824.0;
            list.Add(new Check("Free disk space", $"{gb:0.#} GB", gb < 5 ? "bad" : gb < 15 ? "warn" : "ok"));
        }
        catch
        {
        }

        if (s.SafeMode) list.Add(new Check("Safe mode is on", "Flags and mods are skipped on every launch.", "warn", "Turn safe mode off", () => { s.SafeMode = false; AppState.Save("Safe mode off"); return "Safe mode is off."; }));

        var crash = CrashAnalyzer.AnalyzeLatest();
        if (crash != null && crash.Severity != "ok")
            list.Add(new Check("Last session: " + crash.Title, crash.Explanation, crash.Severity == "bad" ? "bad" : "warn"));

        var cache = CacheManager.Scan();
        var mb = cache.Sum(c => c.Bytes) / 1048576.0;
        if (mb > 500) list.Add(new Check("Large Roblox cache", $"{mb:0} MB", "warn", "Clean caches", () => CacheManager.CleanAll()));

        return list;
    }

    static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}

internal sealed record CacheItem(string Name, string Path, long Bytes, string What);

/// <summary>
/// Cache Manager. Shows sizes first and only ever cleans the folders below. It never touches Roblox's LocalStorage
/// (where your login lives), the installed player, or your settings.
/// </summary>
internal static class CacheManager
{
    static IEnumerable<(string name, string path, string what, int keepNewest)> Targets()
    {
        yield return ("Roblox download cache", Path.Combine(Path.GetTempPath(), "Roblox"), "Temporary game assets. Roblox downloads them again when needed.", 0);
        yield return ("Roblox logs", LogWatcher.LogDir, "Roblox's log files. The newest 5 are kept for the crash analyzer.", 5);
        yield return ("Roblox installer downloads", Path.Combine(Paths.Local, "Roblox", "Downloads"), "Leftover installer files.", 0);
        yield return ("BrainStrap logs", Paths.Logs, "BrainStrap's own logs. The newest 3 are kept.", 3);
    }

    public static List<CacheItem> Scan()
    {
        var list = new List<CacheItem>();
        foreach (var (name, path, what, _) in Targets())
            list.Add(new CacheItem(name, path, Size(path), what));
        return list;
    }

    static long Size(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    public static string Clean(string path)
    {
        if (RobloxInstall.IsRobloxRunning()) return "Close Roblox first.";
        var target = Targets().FirstOrDefault(t => t.path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (target.path == null) return "Not a cache BrainStrap manages.";
        if (!Directory.Exists(target.path)) return "Already empty.";
        long freed = 0;
        var files = new DirectoryInfo(target.path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .OrderByDescending(f => f.LastWriteTimeUtc).Skip(target.keepNewest).ToList();
        foreach (var f in files)
        {
            try { var len = f.Length; f.Delete(); freed += len; } catch { }
        }
        foreach (var d in new DirectoryInfo(target.path).EnumerateDirectories("*", SearchOption.AllDirectories).OrderByDescending(d => d.FullName.Length))
        {
            try { if (!d.EnumerateFileSystemInfos().Any()) d.Delete(); } catch { }
        }
        return $"Freed {freed / 1048576.0:0.#} MB.";
    }

    public static string CleanAll()
    {
        if (RobloxInstall.IsRobloxRunning()) return "Close Roblox first.";
        return string.Join(" ", Targets().Select(t => Clean(t.path)).Distinct());
    }
}

internal sealed record VersionSeen(string Folder, string Version, DateTime FirstSeen);

/// <summary>Update Inspector: remembers each Roblox version this PC has had and when it first appeared.</summary>
internal static class UpdateInspector
{
    static string FileName => Path.Combine(Paths.State, "versions.json");

    public static List<VersionSeen> List()
    {
        try { return File.Exists(FileName) ? JsonSerializer.Deserialize<List<VersionSeen>>(File.ReadAllText(FileName)) ?? new() : new(); }
        catch { return new(); }
    }

    public static async Task<List<VersionSeen>> RefreshAsync()
    {
        var list = List();
        var (ok, version) = await RobloxApi.StatusAsync();
        foreach (var v in RobloxInstall.List())
        {
            if (list.Any(x => x.Folder == v.Name)) continue;
            var guess = ok && v == RobloxInstall.Newest() ? version : "";
            list.Add(new VersionSeen(v.Name, guess, v.Stamp.ToLocalTime()));
        }
        list = list.OrderByDescending(x => x.FirstSeen).Take(50).ToList();
        try { File.WriteAllText(FileName, JsonSerializer.Serialize(list)); } catch { }
        return list;
    }
}

internal sealed record CrashReport(string Title, string Explanation, List<string> Fixes, string Severity, string? LogFile, List<string> Evidence);

/// <summary>
/// Crash Report Analyzer (and the AI Troubleshooter's evidence): turns Roblox's log into a plain explanation.
/// It only quotes short matched lines, with anything that looks like a ticket or cookie removed.
/// </summary>
internal static class CrashAnalyzer
{
    static readonly (Regex re, string title, string why, string[] fixes, string sev)[] Patterns =
    {
        (new Regex(@"DXGI_ERROR_DEVICE_(REMOVED|HUNG|RESET)|device removed|device lost|D3D11.*fail", RegexOptions.IgnoreCase),
            "Graphics driver crashed", "The graphics card stopped responding to Roblox. Usually an outdated or unstable GPU driver, an overclock, or overheating.",
            new[] { "Update your graphics driver from NVIDIA, AMD or Intel's website.", "Remove any GPU overclock.", "In Graphics, set Graphics API to Direct3D 11." }, "bad"),
        (new Regex(@"VK_ERROR|vulkan.*(fail|error)", RegexOptions.IgnoreCase),
            "Vulkan error", "The Vulkan renderer failed on this PC.", new[] { "In Graphics, set Graphics API to Default or Direct3D 11." }, "bad"),
        (new Regex(@"out of memory|bad_alloc|OOM|failed to allocate", RegexOptions.IgnoreCase),
            "Ran out of memory", "Roblox couldn't get more RAM. Big games plus browser tabs can fill 8 GB.",
            new[] { "Close browsers and other heavy apps (Performance → Background apps).", "Lower texture quality in Graphics." }, "bad"),
        (new Regex(@"ClientAppSettings.*(invalid|error|fail)|failed to (load|parse) (fast)?flags", RegexOptions.IgnoreCase),
            "Graphics flags were rejected", "Roblox couldn't read the flag file.", new[] { "Turn on Safe mode and launch once.", "Check custom flags in Graphics." }, "bad"),
        (new Regex(@"Unhandled exception|Crash(ed)?\b|minidump|Writing crash dump|EXCEPTION_ACCESS_VIOLATION", RegexOptions.IgnoreCase),
            "Roblox crashed", "Roblox closed because of an internal error.",
            new[] { "Launch once in Safe mode to rule out flags and mods.", "Use Repair Center → Put original files back.", "Update your graphics driver." }, "bad"),
        (new Regex(@"HttpError: (Timedout|ConnectFail|SslConnectFail)|Connection attempt failed", RegexOptions.IgnoreCase),
            "Network trouble", "Roblox couldn't reach its servers reliably.", new[] { "Run Servers → Connection Diagnostics.", "Turn off any VPN." }, "warn"),
    };

    public static CrashReport? AnalyzeLatest() => LogWatcher.NewestPlayerLog() is { } f ? Analyze(f) : null;

    public static CrashReport Analyze(string file, int? exitCode = null)
    {
        var evidence = new List<string>();
        int? disconnect = null;
        (Regex re, string title, string why, string[] fixes, string sev)? hit = null;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            // only the end of the file matters for how a session ended
            if (fs.Length > 4_000_000) fs.Seek(-4_000_000, SeekOrigin.End);
            string? line;
            var dc = new Regex(@"(?:Disconnect reason received|Sending disconnect with reason|disconnect reason)\s*:?\s*(\d{3})", RegexOptions.IgnoreCase);
            while ((line = r.ReadLine()) != null)
            {
                var m = dc.Match(line);
                if (m.Success) disconnect = int.Parse(m.Groups[1].Value);
                foreach (var p in Patterns)
                {
                    if (!p.re.IsMatch(line)) continue;
                    if (hit == null || Rank(p.sev) > Rank(hit.Value.sev)) hit = p;
                    if (evidence.Count < 6) evidence.Add(Scrub(line));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            return new CrashReport("Couldn't read the log", ex.Message, new List<string>(), "warn", file, evidence);
        }

        if (hit != null)
            return new CrashReport(hit.Value.title, hit.Value.why, hit.Value.fixes.ToList(), hit.Value.sev, file, evidence);
        if (disconnect != null)
        {
            var (t, h) = RobloxCodes.Explain(disconnect.Value);
            var sev = disconnect is 285 or 278 or 271 or 274 or 275 ? "ok" : "warn";
            return new CrashReport($"{t} (error {disconnect})", h, new List<string>(), sev, file, evidence);
        }
        if (exitCode is { } code && code != 0)
            return new CrashReport("Roblox closed unexpectedly", $"Exit code {code}, with no clear cause in the log.",
                new List<string> { "Launch once in Safe mode.", "Update your graphics driver." }, "warn", file, evidence);
        return new CrashReport("No problems found", "The last session ended normally.", new List<string>(), "ok", file, evidence);
    }

    static int Rank(string sev) => sev == "bad" ? 2 : sev == "warn" ? 1 : 0;

    static string Scrub(string line)
    {
        line = Regex.Replace(line, @"_\|WARNING:-DO-NOT-SHARE-THIS[^\s;""]*", "[cookie removed]");
        line = Regex.Replace(line, @"(ticket|token|auth\w*|gameinfo|cookie)\S*\s*[=:]\s*\S+", "$1=[removed]", RegexOptions.IgnoreCase);
        line = Regex.Replace(line, @"\b[0-9a-f]{64,}\b", "[removed]", RegexOptions.IgnoreCase);
        return line.Length > 180 ? line[..180] + "…" : line;
    }
}

internal sealed record FlagIssue(string Flag, string Level, string Message);

/// <summary>FastFlag Validator: name format, value type, and a hard block on anything that could change gameplay.</summary>
internal static class FlagValidator
{
    // physics, networking, character and replication flags can change how the game plays: never allowed
    static readonly Regex Gameplay = new(
        @"physics|s2physics|replicat|sender|network|netrecv|humanoid|hitbox|ragdoll|collision|gravity|walkspeed|jumppower|teleport|interpolat|assemblyextents|velocity|simulat|ownership|clientcast",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static FlagIssue Check(string name, string value)
    {
        if (!FlagCatalog.IsValidFlagName(name)) return new FlagIssue(name, "bad", "Not a Roblox flag name.");
        if (Gameplay.IsMatch(name)) return new FlagIssue(name, "bad", "Could change how the game plays. BrainStrap only allows visual and performance flags.");
        var v = value.Trim().Trim('"');
        var isBool = v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase);
        var isInt = long.TryParse(v, out _);
        if (Regex.IsMatch(name, "^(D|S)?FFlag") && !isBool) return new FlagIssue(name, "bad", "FFlag values must be true or false.");
        if (Regex.IsMatch(name, "^(D|S)?F(Int|Log)") && !isInt) return new FlagIssue(name, "bad", "This kind of flag needs a whole number.");
        if (v.Length > 200) return new FlagIssue(name, "bad", "Value is too long.");
        if (!FlagCatalog.IsAllowlisted(FlagCatalog.Load(), name))
            return new FlagIssue(name, "warn", "Not on Roblox's allowlist, so the Roblox Player ignores it. BrainStrap won't write it unless you allow ignored flags.");
        return new FlagIssue(name, "ok", "On Roblox's allowlist.");
    }

    public static List<FlagIssue> ValidateProfile(FlagProfile p) => p.Custom.Select(kv => Check(kv.Key, kv.Value)).ToList();

    /// <summary>Checks a whole ClientAppSettings.json someone pasted.</summary>
    public static List<FlagIssue> ValidateJson(string json)
    {
        var list = new List<FlagIssue>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new List<FlagIssue> { new("(file)", "bad", "Must be a JSON object { \"Name\": value }.") };
            foreach (var p in doc.RootElement.EnumerateObject())
                list.Add(Check(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText()));
        }
        catch (JsonException ex)
        {
            list.Add(new FlagIssue("(file)", "bad", "Not valid JSON: " + ex.Message));
        }
        return list;
    }
}
