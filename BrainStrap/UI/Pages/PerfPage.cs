using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BrainStrap.Core;
using BrainStrap.Core.Perf;
using Microsoft.Win32;

namespace BrainStrap.UI.Pages;

/// <summary>Performance Lab: measuring, benchmarks, frame-time graphs, history, comparisons, monitors and the background app advisor.</summary>
internal static class PerfPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("Performance", "Real measurements, not guesses. Benchmark Roblox, see every stutter on the frame-time graph, and compare before and after a change.", out var body);

        // ── setup ──
        var exe = FrameCapture.FindExe(s);
        var perm = FrameCapture.HasPermission();
        var setup = new StackPanel();
        setup.Children.Add(Ui.Lbl("FPS measuring"));
        setup.Children.Add(Ui.Status(exe != null ? "ok" : "warn", exe != null ? "PresentMon is ready" : "Step 1: get PresentMon",
            exe != null ? exe : "BrainStrap measures frames with Intel's free PresentMon tool, which reads the frame timing Windows already records, without touching Roblox. Download the console version (PresentMon-…-x64.exe), then point BrainStrap at it.",
            exe != null ? null : Ui.Row(Ui.Ghost("Download page", () => Ui.Open(FrameCapture.ReleasesUrl)), Ui.Pill("Locate…", () =>
            {
                var dlg = new OpenFileDialog { Filter = "PresentMon (*.exe)|PresentMon*.exe|Programs (*.exe)|*.exe" };
                if (dlg.ShowDialog(host) != true) return;
                FrameCapture.Install(dlg.FileName, s);
                AppState.Save("PresentMon set up");
                host.Refresh();
            }))));
        setup.Children.Add(Ui.Status(perm ? "ok" : "warn", perm ? "Windows permission granted" : "Step 2: allow frame timing",
            perm ? "Your Windows user can read frame timing." : "Windows only shares frame timing with administrators and the 'Performance Log Users' group. One admin prompt adds you; sign out and back in afterwards.",
            perm ? null : Ui.Pill("Enable FPS measuring", () =>
            {
                var ok = FrameCapture.RequestPermission();
                MessageBox.Show(host, ok ? "Done. Sign out of Windows and back in, then FPS measuring works." : "It didn't go through (the prompt may have been cancelled).", "BrainStrap");
            })));
        setup.Children.Add(Ui.ToggleRow("Measure FPS in every session", "Records average FPS, 1% lows and stutters for each game you play (stays on this PC).", s.Perf.MeasureFpsInSessions,
            on => { s.Perf.MeasureFpsInSessions = on; AppState.Save(); }));
        body.Children.Add(Ui.Card(setup));

        // ── live monitor ──
        var live = new StackPanel();
        body.Children.Add(Ui.Card(Ui.Lbl("Live · Thermal and Memory Monitor"), live));
        RobloxMonitor? mon = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        void Tick()
        {
            var rp = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault();
            live.Children.Clear();
            if (rp == null)
            {
                mon?.Dispose(); mon = null;
                var cpuT = Thermals.CpuC();
                var gpuT = Thermals.GpuC();
                live.Children.Add(Ui.Small("Start a game to see Roblox's CPU, GPU and memory use here." +
                    (cpuT != null || gpuT != null ? $"  System now: {(cpuT != null ? $"{cpuT:0} °C" : "")}{(gpuT != null ? $"  GPU {gpuT:0} °C" : "")}" : ""), 0));
                return;
            }
            mon ??= new RobloxMonitor(rp);
            var x = mon.Sample();
            var ls = History.ReadLive();
            live.Children.Add(Ui.Cols(4,
                Ui.Stat(ls is { Measuring: true, Fps: > 0 } ? $"{ls.Fps:0}" : "—", "FPS now"),
                Ui.Stat($"{x.CpuPct:0}%", "Roblox CPU", x.CpuPct > 85 ? "Warn" : "Ink"),
                Ui.Stat($"{x.GpuPct:0}%", "Roblox GPU", x.GpuPct > 92 ? "Warn" : "Ink"),
                Ui.Stat(Ui.Mb(x.RamBytes), "Roblox RAM")));
            live.Children.Add(Ui.KV("Video memory", x.GpuMemBytes > 0 ? Ui.Mb(x.GpuMemBytes) : "not reported"));
            live.Children.Add(Ui.KV("Free system memory", x.SysAvailBytes > 0 ? Ui.Mb(x.SysAvailBytes) : "—", (x.SysAvailBytes is > 0 and < 1_200_000_000) ? "Bad" : "Ink"));
            live.Children.Add(Ui.KV("Temperatures", (x.CpuTemp != null ? $"System {x.CpuTemp:0} °C" : "System: not exposed by this PC") + (x.GpuTemp != null ? $" · GPU {x.GpuTemp:0} °C" : ""),
                x.CpuTemp > 90 || x.GpuTemp > 85 ? "Bad" : "Ink"));
        }
        timer.Tick += (_, _) => Tick();
        page.Loaded += (_, _) => { Tick(); timer.Start(); };
        page.Unloaded += (_, _) => { timer.Stop(); mon?.Dispose(); mon = null; };

        // ── benchmark ──
        var benchOut = new StackPanel();
        var label = Ui.Field(s.ActiveProfile, 220);
        body.Children.Add(Ui.Card(
            Ui.Lbl("FPS Benchmark · Frametime Graph · Stability Score"),
            Ui.Title("Measure 60 seconds of real gameplay", 15),
            Ui.Desc("Start a game and play normally (move around, the way you usually play). Run it again after changing settings to compare."),
            new Border { Height = 10 },
            Ui.Row(Ui.Text("Label  ", 13, "Muted"), label, new Border { Width = 10 }, Ui.AsyncPill("Run benchmark", async () =>
            {
                benchOut.Children.Clear();
                if (Process.GetProcessesByName("RobloxPlayerBeta").Length == 0) { benchOut.Children.Add(Ui.Status("warn", "Start a game first", "")); return; }
                if (FrameCapture.FindExe(s) == null || !FrameCapture.HasPermission()) { benchOut.Children.Add(Ui.Status("warn", "Finish the FPS measuring setup above first", "")); return; }
                using var cap = new FrameCapture();
                if (!cap.Start(s, 60)) { benchOut.Children.Add(Ui.Status("bad", "Couldn't start measuring", cap.Error ?? "")); return; }
                var countdown = Ui.Text("60 s left · keep playing", 14, "Accent", FontWeights.SemiBold);
                benchOut.Children.Add(countdown);
                for (var t = 60; t > 0; t--) { countdown.Text = $"{t} s left · keep playing"; await Task.Delay(1000); }
                await cap.WaitAsync(TimeSpan.FromSeconds(8));
                var frames = cap.Snapshot();
                var stats = FrameStats.From(frames);
                benchOut.Children.Clear();
                if (stats == null) { benchOut.Children.Add(Ui.Status("bad", "No frames recorded", cap.Error ?? "Was the game window open and active?")); return; }
                var live2 = History.ReadLive();
                var rec = new BenchRecord
                {
                    Label = label.Text.Trim().Length > 0 ? label.Text.Trim() : s.ActiveProfile,
                    Profile = s.ActiveProfile,
                    Values = new Dictionary<string, string>(s.Current.Values),
                    PlaceId = live2?.PlaceId,
                    GameName = live2?.GameName,
                    Stats = stats,
                    Graph = FrameStats.Graph(frames),
                };
                History.AddBenchmark(rec);
                ShowBench(benchOut, rec, null);
            })),
            benchOut));

        // ── comparison ──
        var benches = History.Benchmarks();
        if (benches.Count >= 1) ShowBench(benchOut, benches[0], null);
        if (benches.Count >= 2)
        {
            var a = new DarkCombo { Width = 300, Margin = new Thickness(0, 0, 10, 8) };
            var b = new DarkCombo { Width = 300, Margin = new Thickness(0, 0, 10, 8) };
            foreach (var x in benches) { a.Items.Add(BenchName(x)); b.Items.Add(BenchName(x)); }
            a.SelectedIndex = 1;
            b.SelectedIndex = 0;
            var cmp = new StackPanel();
            void Compare()
            {
                cmp.Children.Clear();
                if (a.SelectedIndex < 0 || b.SelectedIndex < 0) return;
                var before = benches[a.SelectedIndex];
                var after = benches[b.SelectedIndex];
                string D(double x, double y) { var d = y - x; var pct = x > 0 ? d / x * 100 : 0; return $"{x:0} → {y:0}  ({(d >= 0 ? "+" : "")}{pct:0}%)"; }
                cmp.Children.Add(Ui.KV("Average FPS", D(before.Stats.AvgFps, after.Stats.AvgFps), after.Stats.AvgFps >= before.Stats.AvgFps ? "Ok" : "Bad"));
                cmp.Children.Add(Ui.KV("1% low FPS", D(before.Stats.Low1, after.Stats.Low1), after.Stats.Low1 >= before.Stats.Low1 ? "Ok" : "Bad"));
                cmp.Children.Add(Ui.KV("Stability", $"{before.Stats.Stability} → {after.Stats.Stability}", after.Stats.Stability >= before.Stats.Stability ? "Ok" : "Bad"));
                cmp.Children.Add(Ui.KV("Stutters / min", $"{before.Stats.StuttersPerMin:0.#} → {after.Stats.StuttersPerMin:0.#}", after.Stats.StuttersPerMin <= before.Stats.StuttersPerMin ? "Ok" : "Bad"));
                var changed = after.Values.Keys.Union(before.Values.Keys).Where(k => after.Values.GetValueOrDefault(k) != before.Values.GetValueOrDefault(k)).ToList();
                cmp.Children.Add(Ui.KV("Settings changed", changed.Count == 0 ? "none" : string.Join(", ", changed)));
                cmp.Children.Add(Ui.Graph(after.Graph, 130, "Accent", before.Graph));
                cmp.Children.Add(Ui.Small("Bright line: " + BenchName(after) + " · dim line: " + BenchName(before), 0));
            }
            a.SelectionChanged += (_, _) => Compare();
            b.SelectionChanged += (_, _) => Compare();
            body.Children.Add(Ui.Card(Ui.Lbl("Benchmark Comparison"), Ui.Row(Ui.Text("Before ", 13, "Muted"), a), Ui.Row(Ui.Text("After    ", 13, "Muted"), b), cmp));
            Compare();
        }

        // ── history ──
        var sessions = History.Sessions().Take(25).ToList();
        var hist = new StackPanel();
        var measured = sessions.Where(r => r.Fps != null).Reverse().ToList();
        if (measured.Count >= 2)
        {
            hist.Children.Add(Ui.Small("Average FPS across your measured sessions (oldest → newest)", 0));
            hist.Children.Add(Ui.Spark(measured.Select(r => r.Fps!.AvgFps).ToList(), 46));
        }
        if (sessions.Count == 0) hist.Children.Add(Ui.Small("No sessions yet. Play through BrainStrap and they appear here.", 0));
        foreach (var r in sessions.Take(12))
        {
            var line = $"{r.Start:d MMM HH:mm} · {r.GameName ?? (r.PlaceId != null ? "Place " + r.PlaceId : "Roblox")} · {r.Minutes:0} min";
            var detail = (r.Fps != null ? $"{r.Fps.AvgFps:0} FPS, 1% low {r.Fps.Low1:0}, stability {r.Fps.Stability}" : "FPS not measured") +
                         (r.Ping is { Samples: > 0 } p && p.LossPct < 100 ? $" · ping {p.Avg:0} ms" : "") + $" · profile {r.Profile}" +
                         (r.Problem != null ? $" · {r.Problem}" : "");
            hist.Children.Add(Ui.Status(r.Problem != null ? "warn" : r.Fps == null ? "info" : r.Fps.Stability >= 70 ? "ok" : "warn", line, detail));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Performance History"), hist));

        // ── BrainReplay: what happened, when, in your last sessions ──
        if (BrainStrap.Core.Platform.Extensions.On(s, "brainreplay"))
        {
            var rep = new StackPanel();
            foreach (var r in sessions.Where(x => x.Events.Count > 0).Take(3))
            {
                rep.Children.Add(Ui.Title($"{r.Start:d MMM HH:mm} · {r.GameName ?? "Roblox"}", 14));
                if (r.Graph is { Length: > 1 }) rep.Children.Add(Ui.Graph(r.Graph, 70));
                foreach (var ev in r.Events.Take(14))
                    rep.Children.Add(Ui.Status(ev.Kind is "crash" or "disconnect" ? "bad" : ev.Kind is "stutter" or "ping" ? "warn" : "info",
                        $"{TimeSpan.FromSeconds(ev.T):m\\:ss} · {ev.Kind}", ev.Detail));
                rep.Children.Add(new Border { Height = 12 });
            }
            if (rep.Children.Count == 0) rep.Children.Add(Ui.Small("Stutters, ping spikes, server joins and disconnects from your sessions will be listed here on a timeline.", 0));
            body.Children.Add(Ui.Card(Ui.Lbl("BrainReplay"), rep));
        }

        // ── background apps ──
        var appsBox = new StackPanel();
        body.Children.Add(Ui.Card(
            Ui.Lbl("Background App Advisor"),
            Ui.Desc("Programs that compete with Roblox for CPU, GPU and memory. BrainStrap only asks a program to close (like clicking its X); it never force-quits anything.", 0),
            Ui.Buttons(Ui.AsyncPill("Scan now", async () =>
            {
                appsBox.Children.Clear();
                appsBox.Children.Add(Ui.Small("Scanning…"));
                var apps = await BackgroundAdvisor.ScanAsync();
                appsBox.Children.Clear();
                if (apps.Count == 0) { appsBox.Children.Add(Ui.Status("ok", "Nothing heavy running", "")); return; }
                foreach (var a in apps)
                {
                    var name = a.Name;
                    appsBox.Children.Add(Ui.Status(a.CpuPct >= 10 || a.RamBytes >= 1_500L * 1024 * 1024 ? "warn" : "info",
                        $"{a.Name} · {a.CpuPct:0}% CPU · {Ui.Mb(a.RamBytes)}", a.Hint,
                        Ui.Ghost("Close", () =>
                        {
                            if (MessageBox.Show(host, $"Ask {name} to close? Save your work in it first.", "BrainStrap", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                            var n = BackgroundAdvisor.CloseGracefully(name);
                            MessageBox.Show(host, n > 0 ? $"Asked {name} to close." : $"{name} has no window to close; quit it from its tray icon.", "BrainStrap");
                        })));
                }
            })),
            appsBox));

        // ── advanced ──
        var extra = Ui.Field(s.Perf.PresentMonExtraArgs, 420, true);
        extra.LostFocus += (_, _) => { s.Perf.PresentMonExtraArgs = extra.Text.Trim(); AppState.Save(); };
        body.Children.Add(Ui.Card(Ui.Lbl("Advanced"), Ui.Desc("Extra PresentMon options, only needed if a future PresentMon version renames its options.", 0), new Border { Height = 8 }, extra));
        return page;
    }

    static string BenchName(BenchRecord b) => $"{b.Label} · {b.When:d MMM HH:mm} · {b.Stats.AvgFps:0} FPS";

    static void ShowBench(StackPanel box, BenchRecord rec, BenchRecord? compare)
    {
        var st = rec.Stats;
        box.Children.Add(Ui.Lbl($"{rec.Label} · {rec.When:d MMM HH:mm}{(rec.GameName != null ? " · " + rec.GameName : "")}", 14));
        box.Children.Add(Ui.Cols(4,
            Ui.Stat($"{st.AvgFps:0}", "average FPS"),
            Ui.Stat($"{st.Low1:0}", "1% low FPS", st.Low1 < st.AvgFps * 0.5 ? "Warn" : "Ink"),
            Ui.Stat($"{st.Stability}", "stability · " + st.StabilityLabel, st.Stability >= 70 ? "Ok" : "Warn"),
            Ui.Stat($"{st.StuttersPerMin:0.#}", "stutters / min", st.StuttersPerMin > 6 ? "Warn" : "Ink")));
        box.Children.Add(Ui.Graph(rec.Graph, 140, "Accent", compare?.Graph));
        box.Children.Add(Ui.Small($"{st.Frames} frames over {st.Seconds:0} s · 0.1% low {st.Low01:0} FPS · worst frame {st.WorstMs:0} ms. Spikes on the graph are the stutters averages hide.", 0));
    }
}
