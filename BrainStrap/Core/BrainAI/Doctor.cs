using BrainStrap.Core.Perf;

namespace BrainStrap.Core.BrainAI;

/// <summary>A finding with an optional one-click fix (a change to the current graphics profile, or another action).</summary>
internal sealed record Finding(string Level, string Title, string Detail, string? FixLabel = null, Dictionary<string, string?>? SetValues = null, Action? Fix = null);

internal sealed record LagVerdict(string Kind, string Headline, string Detail, List<string> Evidence);

internal static class Doctor
{
    /// <summary>AI FPS Doctor: works out why FPS drops from the measurements it has, and says how sure it is.</summary>
    public static List<Finding> FpsDoctor(Settings s, PcInfo pc, FrameStats? fps, LiveSample? hw, List<HeavyApp> apps)
    {
        var f = new List<Finding>();
        var v = s.Current.Values;
        string? Val(string id) => v.GetValueOrDefault(id);

        if (pc.OnBattery)
            f.Add(new Finding("high", "Running on battery", "Windows slows the CPU and GPU on battery, often by half. Plug in for full speed, or use a battery profile in BrainLife."));

        if (fps != null)
        {
            if (fps.AvgFps < 40) f.Add(new Finding("high", $"Low average: {fps.AvgFps:0} FPS", "This PC is struggling with the current settings."));
            if (fps.Low1 < fps.AvgFps * 0.5)
                f.Add(new Finding("high", $"Big drops: 1% lows at {fps.Low1:0} FPS vs {fps.AvgFps:0} average",
                    "The worst frames are less than half the average. That's the stutter you feel, and it usually comes from Roblox changing graphics quality on its own, background apps, or memory running out.",
                    Val("frm") == null ? "Lock the quality level" : null, Val("frm") == null ? new Dictionary<string, string?> { ["frm"] = pc.Tier >= 2 ? "12" : "8" } : null));
            if (fps.StuttersPerMin > 6)
                f.Add(new Finding("medium", $"{fps.StuttersPerMin:0} hitches a minute", "Single long frames (over 25 ms). Often shader loading in a new area, background apps, or a nearly full disk."));
        }
        else
        {
            f.Add(new Finding("info", "No FPS measurement yet", "Run a benchmark on the Performance page while playing. With real frame times the diagnosis is much more precise."));
        }

        var fpsCap = int.TryParse(Val("fps"), out var c) ? c : 60;
        if (pc.Refresh > 60)
            f.Add(new Finding("info", $"Your screen runs at {pc.Refresh} Hz", $"Roblox caps FPS at 60 unless you raise it. The old FPS flag is ignored since Roblox's allowlist, so in a game open Roblox's Settings and set Maximum Frame Rate to {pc.Refresh}."));

        if (hw != null)
        {
            var oneCore = 100.0 / Math.Max(1, pc.Threads);
            if (hw.GpuPct >= 92 && hw.CpuPct < oneCore * 3)
                f.Add(new Finding("high", $"Graphics card is maxed out ({hw.GpuPct:0}%)", "The GPU is the limit, so graphics settings will help the most.",
                    "Apply GPU savers", new Dictionary<string, string?> { ["msaa"] = "1", ["grass"] = "off", ["lod"] = "perf", ["frm"] = pc.Tier <= 1 ? "5" : "10" }));
            else if (hw.CpuPct >= oneCore * 0.9 && hw.GpuPct < 70)
                f.Add(new Finding("medium", "Processor-limited", "Roblox runs most game logic on one CPU core, and that core is busy while the GPU waits. Busy servers and heavy scripts cause this; graphics settings won't help much. Closing background apps does."));
            if (hw.SysAvailBytes > 0 && hw.SysAvailBytes < 1_200L * 1024 * 1024)
                f.Add(new Finding("high", $"Memory almost full ({hw.SysAvailBytes / 1048576} MB free)", "When RAM runs out Windows swaps to disk, which causes long freezes. Close browser tabs and other apps."));
            if (hw.CpuTemp is > 90)
                f.Add(new Finding("high", $"System running hot ({hw.CpuTemp:0} °C)", "Hot chips slow themselves down (thermal throttling). Clean the vents, raise the laptop, and set Roblox's Maximum Frame Rate to 60 in its in-game Settings."));
            if (hw.GpuTemp is > 85)
                f.Add(new Finding("medium", $"GPU running hot ({hw.GpuTemp:0} °C)", $"The graphics card may be throttling. Set Roblox's Maximum Frame Rate to your screen's {Math.Max(60, pc.Refresh)} Hz in its in-game Settings."));
        }

        var heavy = apps.Where(a => a.CpuPct >= 8 || a.RamBytes >= 1_500L * 1024 * 1024).Take(3).ToList();
        if (heavy.Count > 0)
            f.Add(new Finding("medium", "Background apps competing: " + string.Join(", ", heavy.Select(a => a.Name)),
                string.Join(" ", heavy.Select(a => $"{a.Name}: {a.CpuPct:0}% CPU, {a.RamBytes / 1048576} MB.")) + " Close what you don't need while playing."));

        if (pc.Tier <= 1 && Val("msaa") is "4" or "8")
            f.Add(new Finding("high", "Heavy anti-aliasing on modest hardware", $"MSAA {Val("msaa")}x costs a lot on this GPU.", "Turn MSAA off", new Dictionary<string, string?> { ["msaa"] = "1" }));
        var ignored = FlagCatalog.Load().Flags.Where(d => !d.Allowlisted && v.ContainsKey(d.Id)).Select(d => d.Name).ToList();
        if (ignored.Count > 0)
            f.Add(new Finding("info", "Some settings are ignored by Roblox", string.Join(", ", ignored) + " aren't on Roblox's allowlist, so they have no effect. BrainFlags shows the alternatives."));
        if (Val("api") is "vulkan" or "opengl" && fps != null && fps.Stability < 70)
            f.Add(new Finding("medium", "A forced graphics API with uneven frames", "Forcing Vulkan or OpenGL can stutter on some drivers.", "Back to default API", new Dictionary<string, string?> { ["api"] = null }));
        if (s.SafeMode)
            f.Add(new Finding("info", "Safe mode is on", "Flags and mods are skipped, so changes here won't apply until you turn it off."));

        if (f.All(x => x.Level == "info"))
            f.Add(new Finding("info", "Nothing obvious is wrong", "Settings and the PC look healthy. If FPS still drops, it's probably the game itself (big maps, many players)."));
        return f.OrderBy(x => x.Level == "high" ? 0 : x.Level == "medium" ? 1 : 2).ToList();
    }

    /// <summary>AI Lag Detective: "lag" can mean low FPS or a bad connection. Frame times vs ping tell them apart.</summary>
    public static LagVerdict LagDetective(FrameStats? frames, PingStats? server, PingStats? router, PingStats? internet)
    {
        var ev = new List<string>();
        var fpsBad = frames != null && (frames.Stability < 70 || frames.Low1 < frames.AvgFps * 0.5 || frames.AvgFps < 40);
        if (frames != null) ev.Add($"Frames: {frames.AvgFps:0} FPS average, 1% low {frames.Low1:0}, stability {frames.Stability}/100 ({frames.StabilityLabel}).");
        else ev.Add("Frames: not measured (set up FPS measuring on the Performance page).");

        var localBad = router != null && (router.LossPct > 1 || router.Jitter > 10);
        if (router != null) ev.Add($"Router: {router.Avg:0} ms, jitter {router.Jitter:0} ms, loss {router.LossPct:0.#}%.");
        var netBad = internet != null && (internet.LossPct > 2 || internet.Jitter > 20 || internet.Avg > 120);
        if (internet != null) ev.Add($"Internet: {internet.Avg:0} ms, jitter {internet.Jitter:0} ms, loss {internet.LossPct:0.#}%.");
        var serverBad = server != null && server.LossPct < 100 && (server.Avg > 150 || server.Jitter > 25 || server.LossPct > 3);
        if (server != null) ev.Add(server.LossPct >= 100 ? "Game server: doesn't answer ping." : $"Game server: {server.Avg:0} ms, jitter {server.Jitter:0} ms, loss {server.LossPct:0.#}%.");

        if (fpsBad && (localBad || netBad || serverBad))
            return new LagVerdict("both", "Both: frame drops and a shaky connection", "Fix the FPS side with the FPS Doctor, and the connection side with the network notes below.", ev);
        if (fpsBad)
            return new LagVerdict("fps", "FPS lag: your PC, not your internet", "The connection is fine but frames are uneven. Run the FPS Doctor for fixes.", ev);
        if (localBad)
            return new LagVerdict("wifi", "Network lag: your Wi-Fi or router", "Problems start before your traffic even leaves the house. Move closer to the router, use 5 GHz, or plug in a cable.", ev);
        if (netBad)
            return new LagVerdict("internet", "Network lag: your internet connection", "Your home network is fine but the connection beyond it isn't. Pause downloads and streams, or contact your provider if it continues.", ev);
        if (serverBad)
            return new LagVerdict("server", "Network lag: this game server", "Your connection is fine, but this server is far away or overloaded. Try a suggested server on the Servers page.", ev);
        if (frames == null && server == null)
            return new LagVerdict("unknown", "Not enough data", "Start a game first, then run the Lag Detective while you play.", ev);
        return new LagVerdict("none", "No lag found during the test", "Frames and connection were both steady. If it lags at certain moments, run the test again when it happens.", ev);
    }

    /// <summary>AI Compatibility Checker: flags settings that are invalid, conflicting or possibly outdated.</summary>
    public static List<Finding> Compatibility(Settings s)
    {
        var f = new List<Finding>();
        var cat = FlagCatalog.Load();
        var p = s.Current;
        foreach (var kv in p.Values)
        {
            var def = cat.Flags.FirstOrDefault(d => d.Id == kv.Key);
            if (def == null) f.Add(new Finding("medium", $"Unknown setting '{kv.Key}'", "This BrainStrap version doesn't know it. It is ignored.", "Remove it", new Dictionary<string, string?> { [kv.Key] = null }));
            else if (Engine.Clean(def, kv.Value) != kv.Value)
                f.Add(new Finding("high", $"{def.Name}: '{kv.Value}' isn't allowed", "The value is outside what this setting accepts.", "Reset to default", new Dictionary<string, string?> { [kv.Key] = null }));
        }
        foreach (var issue in FlagValidator.ValidateProfile(p).Where(i => i.Level != "ok"))
            f.Add(new Finding(issue.Level == "bad" ? "high" : "info", $"Custom flag {issue.Flag}", issue.Message));

        var resolved = FlagCatalog.Resolve(cat, p, s.FlagFormat);
        var apis = resolved.Keys.Count(k => k.StartsWith("FFlagDebugGraphicsPrefer"));
        if (apis > 1) f.Add(new Finding("high", "Two graphics APIs forced at once", "Only one can win, and the result is unpredictable. Remove the extra one from custom flags."));
        var lights = resolved.Keys.Count(k => k is "DFFlagDebugRenderForceTechnologyVoxel" or "FFlagDebugForceFutureIsBrightPhase2" or "FFlagDebugForceFutureIsBrightPhase3");
        if (lights > 1) f.Add(new Finding("high", "Two lighting modes forced at once", "Remove the extra one from custom flags."));

        if (DateTime.TryParse(cat.Updated + "-01", out var updated) && (DateTime.Now - updated).TotalDays > 180)
            f.Add(new Finding("medium", "The flag catalog is over 6 months old", "Roblox changes which flags it honours. Turn on 'Show FPS counter' and check it still appears; if not, flags may be ignored. Update BrainStrap for a fresh catalog."));
        if (resolved.Count == 0) f.Add(new Finding("info", "No flags in this profile", "Roblox runs with its own defaults."));
        if (f.Count == 0) f.Add(new Finding("info", "All settings check out", $"{resolved.Count} flag(s) valid for this BrainStrap catalog ({cat.Updated})."));
        return f;
    }

    /// <summary>AI Troubleshooter: the last session's ending, explained, with fixes.</summary>
    public static CrashReport? Troubleshoot()
    {
        var last = History.Sessions().FirstOrDefault();
        var log = LogWatcherFile();
        if (log == null) return null;
        return CrashAnalyzer.Analyze(log, last?.ExitCode);
    }

    static string? LogWatcherFile() => Servers.LogWatcher.NewestPlayerLog();

    /// <summary>Applies a finding's fix to the current profile (null value = back to Roblox's default).</summary>
    public static void ApplyFix(Settings s, Finding f)
    {
        if (f.SetValues != null)
        {
            foreach (var kv in f.SetValues)
            {
                if (kv.Value == null) s.Current.Values.Remove(kv.Key);
                else s.Current.Values[kv.Key] = kv.Value;
            }
            AppState.Save("BrainAI fix: " + f.Title);
        }
        f.Fix?.Invoke();
    }
}
