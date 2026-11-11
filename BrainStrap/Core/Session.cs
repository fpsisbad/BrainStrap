using System.Diagnostics;
using System.Windows;
using BrainStrap.Core.BrainAI;
using BrainStrap.Core.Perf;
using BrainStrap.Core.Servers;
using BrainStrap.Core.Platform;
using System.Text.Json.Nodes;
using BrainStrap.UI;

namespace BrainStrap.Core;

/// <summary>
/// Everything that happens while Roblox is open: session tracking, live FPS/ping/hardware numbers, the HUD,
/// server info from Roblox's logs, Discord presence, Game Mode+, companion apps, and the crash check at the end.
/// It only observes Roblox from the outside (logs, Windows counters, PresentMon); it never touches the game process's memory.
/// </summary>
internal static class Session
{
    public static bool Wanted(Settings s) =>
        s.Perf.TrackSessions || s.Hud.Enabled || s.Crosshair.Enabled || (s.Overlay.Enabled && Extensions.On(s, "brainoverlay")) || (s.Discord.Enabled && s.Discord.ClientId.Trim().Length > 0) || s.Life.GameModePlus || s.Life.Companions.Count > 0;

    /// <summary>For games started from a join link: waits for Roblox to appear, then runs the session.</summary>
    public static async Task WatchAsync(string? placeId)
    {
        var since = DateTime.Now.AddSeconds(-5);
        for (var i = 0; i < 120; i++)
        {
            var p = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault(x => { try { return x.StartTime >= since; } catch { return false; } });
            if (p != null)
            {
                var s = AppState.S;
                await RunAsync(p, placeId, s.ProfileNameFor(placeId), DateTime.UtcNow);
                return;
            }
            await Task.Delay(500);
        }
        Logger.Info("watch: Roblox did not start");
    }

    public static async Task RunAsync(Process roblox, string? placeId, string profile, DateTime launchedUtc, LaunchOverrides? ov = null)
    {
        var s = AppState.S;
        ov ??= new LaunchOverrides();
        var t0 = DateTime.UtcNow;
        double Now() => Math.Round((DateTime.UtcNow - t0).TotalSeconds, 1);
        var rec = new SessionRecord { Start = DateTime.Now, PlaceId = placeId, Profile = profile };
        var live = new LiveState { Started = DateTime.UtcNow, PlaceId = placeId, Profile = profile };
        var breaksShown = 0;

        using var gameMode = new GameModePlus();
        using var companions = new Companions();
        if (s.Life.GameModePlus || ov.GameMode) gameMode.Engage(roblox);
        companions.Start(s);

        using var logs = new LogWatcher();
        Pinger? pinger = null;
        DiscordRpc? discord = null;
        var discordStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        async Task UpdateDiscordAsync()
        {
            if (!s.Discord.Enabled || s.Discord.ClientId.Trim().Length == 0) return;
            try
            {
                if (discord == null)
                {
                    discord = new DiscordRpc(s.Discord.ClientId.Trim());
                    if (!await discord.ConnectAsync()) { discord.Dispose(); discord = null; return; }
                }
                var title = s.Discord.ShowGameName && rec.GameName != null ? rec.GameName : "Playing Roblox";
                await discord.SetActivityAsync(title, "via BrainStrap", discordStart, s.Discord.LargeImageKey);
            }
            catch (Exception ex)
            {
                Logger.Warn("discord: " + ex.Message);
            }
        }

        logs.Joined += (place, job) =>
        {
            rec.PlaceId = live.PlaceId = place;
            rec.JobId = live.JobId = job;
            rec.Events.Add(new ReplayEvent { T = Now(), Kind = "join", Detail = "Joined a server" });
            _ = Task.Run(async () =>
            {
                var g = await RobloxApi.GameAsync(place);
                if (g != null) rec.GameName = live.GameName = g.Name;
                await await Application.Current.Dispatcher.InvokeAsync(UpdateDiscordAsync);
            });
        };
        logs.ServerAddress += ip =>
        {
            if (rec.ServerIp == ip) return;
            rec.ServerIp = live.ServerIp = ip;
            if (s.Servers.TrackPing) { pinger?.Dispose(); pinger = new Pinger(ip); }
            _ = Task.Run(async () =>
            {
                var r = await ServerGeo.LookupAsync(ip, s);
                if (r != null) rec.Region = live.Region = r.ToString();
            });
        };
        logs.Disconnected += code =>
        {
            rec.Disconnect = $"{code}: {RobloxCodes.Explain(code).title}";
            rec.Events.Add(new ReplayEvent { T = Now(), Kind = "disconnect", Detail = rec.Disconnect });
        };
        _ = Connect.SendWebhooksAsync(s, "start", new JsonObject { ["game"] = rec.GameName, ["placeId"] = placeId, ["profile"] = profile });
        logs.Start(launchedUtc);
        if (placeId != null) _ = Task.Run(async () => { var g = await RobloxApi.GameAsync(placeId); if (g != null) rec.GameName ??= live.GameName = g.Name; });
        await UpdateDiscordAsync();

        FrameCapture? frames = null;
        if ((s.Perf.MeasureFpsInSessions || ov.Track) && FrameCapture.FindExe(s) != null && FrameCapture.HasPermission())
        {
            frames = new FrameCapture();
            if (!frames.Start(s)) { Logger.Info("fps capture: " + frames.Error); frames.Dispose(); frames = null; }
        }
        live.Measuring = frames != null;

        using var monitor = new RobloxMonitor(roblox);
        Hud? hud = (ov.Hud ?? (s.Hud.Enabled && Extensions.On(s, "brainhud"))) ? new Hud(s) : null;
        hud?.Show();
        UI.OverlayHost? overlay = null;
        if (s.Overlay.Enabled && Extensions.On(s, "brainoverlay"))
            try { overlay = new UI.OverlayHost(s, roblox); } catch (Exception ex) { Logger.Warn("overlay: " + ex.Message); }
        CrosshairOverlay? sight = s.Crosshair.Enabled ? new CrosshairOverlay(s) : null;
        sight?.Show();

        double peakRam = 0, peakCpuT = 0, peakGpuT = 0;
        var tick = 0;
        while (!SafeExited(roblox))
        {
            await Task.Delay(1000);
            tick++;
            var (fps, low1) = frames?.Recent() ?? (0d, 0d);
            live.Fps = fps;
            live.Low1 = low1;
            if (tick % 2 == 0)
            {
                var hw = monitor.Sample();
                live.Hw = hw;
                peakRam = Math.Max(peakRam, hw.RamBytes / 1048576.0);
                if (hw.CpuTemp is { } ct) peakCpuT = Math.Max(peakCpuT, ct);
                if (hw.GpuTemp is { } gt) peakGpuT = Math.Max(peakGpuT, gt);
                live.Ping = pinger?.Stats(30);
                live.Updated = DateTime.UtcNow;
                History.WriteLive(live);
                Connect.WriteObs(s, live);
                if (live.Ping is { Samples: > 5 } pg && pg.LossPct < 100 && pinger?.Recent(1).FirstOrDefault() is double last && last > Math.Max(120, pg.Avg * 2.2))
                    rec.Events.Add(new ReplayEvent { T = Now(), Kind = "ping", Detail = $"Ping spike: {last:0} ms (usual {pg.Avg:0})" });
            }
            hud?.Update(live, DateTime.UtcNow - live.Started);
            sight?.Sync(roblox);
            if (s.Life.BreakMinutes > 0 && (DateTime.UtcNow - live.Started).TotalMinutes >= s.Life.BreakMinutes * (breaksShown + 1))
            {
                breaksShown++;
                Connect.Notify("Time for a break?", $"You've been playing for {(DateTime.UtcNow - live.Started).TotalMinutes:0} minutes.");
            }
        }

        hud?.Close();
        overlay?.Dispose();
        sight?.Close();
        History.ClearLive();
        if (discord != null) { await discord.ClearAsync(); discord.Dispose(); }

        rec.Minutes = Math.Round((DateTime.Now - rec.Start).TotalMinutes, 1);
        try { rec.ExitCode = roblox.ExitCode; } catch { }
        if (frames != null)
        {
            var all = frames.Snapshot();
            rec.Fps = FrameStats.From(all);
            rec.Graph = FrameStats.Graph(all, 240);
            // BrainReplay: the worst hitches, placed on the session timeline
            if (rec.Fps != null)
            {
                double acc = 0;
                var marks = new List<ReplayEvent>();
                var limit = Math.Max(25, rec.Fps.MedianMs * 2.5);
                foreach (var f in all) { acc += f; if (f > limit) marks.Add(new ReplayEvent { T = Math.Round(acc / 1000, 1), Kind = "stutter", Detail = $"{f:0} ms frame" }); }
                rec.Events.AddRange(marks.OrderByDescending(m => double.Parse(m.Detail.Split(' ')[0])).Take(40));
            }
            frames.Dispose();
        }
        rec.Ping = pinger?.Stats();
        pinger?.Dispose();
        rec.PeakRamMb = peakRam > 0 ? Math.Round(peakRam) : null;
        rec.PeakCpuTemp = peakCpuT > 0 ? peakCpuT : null;
        rec.PeakGpuTemp = peakGpuT > 0 ? peakGpuT : null;

        // crash and disconnect check
        CrashReport? report = null;
        if (logs.FilePath != null && (rec.ExitCode is { } code && code != 0 || rec.Disconnect != null))
        {
            report = CrashAnalyzer.Analyze(logs.FilePath, rec.ExitCode);
            if (report.Severity != "ok") rec.Problem = report.Title;
        }
        if (report != null && report.Severity != "ok") rec.Events.Add(new ReplayEvent { T = Now(), Kind = "crash", Detail = report.Title });
        rec.Events = rec.Events.OrderBy(e => e.T).ToList();
        if ((s.Perf.TrackSessions || ov.Track) && rec.Minutes >= 0.2 && Extensions.On(s, "brainpulse")) History.AddSession(rec);
        // Flag Restore: remember settings that ran well, so they can be restored later
        if (rec.Fps is { Stability: >= 70 } && rec.Problem == null) s.Platform.KnownGood[profile] = new Dictionary<string, string>(s.Profiles.GetValueOrDefault(profile)?.Values ?? new());
        var crashed = report != null && report.Severity == "bad";
        _ = Connect.SendWebhooksAsync(s, crashed ? "crash" : "end", new JsonObject
        {
            ["game"] = rec.GameName, ["placeId"] = rec.PlaceId, ["minutes"] = rec.Minutes, ["fps"] = rec.Fps?.AvgFps, ["stability"] = rec.Fps?.Stability, ["problem"] = rec.Problem,
        });
        Flow.Fire(s, crashed ? "crash" : "exit", rec.PlaceId, rec.GameName);
        SettingsStore.Save(s);
        Logger.Info($"session ended after {rec.Minutes} min{(rec.Fps != null ? $", {rec.Fps.AvgFps} fps avg" : "")}");
        JumpLists.Update(s);

        if (report != null && report.Severity != "ok")
        {
            var toast = new Toast("BrainAI: " + report.Title, report.Explanation, "Open Troubleshooter", () => OpenApp("tools"));
            toast.Show();
            await toast.ClosedTask;
        }
    }

    static bool SafeExited(Process p)
    {
        try { return p.HasExited; } catch { return true; }
    }

    public static void OpenApp(string page)
    {
        try { Process.Start(new ProcessStartInfo(Paths.SelfExe) { UseShellExecute = false, ArgumentList = { "--page", page } }); }
        catch (Exception ex) { Logger.Warn("open app: " + ex.Message); }
    }
}
