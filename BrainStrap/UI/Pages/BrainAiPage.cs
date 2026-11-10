using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BrainStrap.Core;
using BrainStrap.Core.BrainAI;
using BrainStrap.Core.Perf;
using BrainStrap.Core.Servers;

namespace BrainStrap.UI.Pages;

internal static class BrainAiPage
{
    public static string? PendingRequest;   // set by Home's BrainBoost button

    static readonly string[] Examples =
    {
        "Make Roblox run smoother on my laptop while keeping the graphics looking good",
        "Max FPS, I don't care about graphics",
        "Competitive and responsive, lowest input lag",
        "My laptop gets hot and loud",
        "Cinematic screenshots, best quality",
    };

    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("BrainAI", "Your own Roblox performance assistant. It reads your PC, understands plain English, and always shows you a preview before changing anything.", out var body);

        // ── BrainPilot: plain-English control of the whole launcher ──
        if (BrainStrap.Core.Platform.Extensions.On(s, "brainpilot"))
        {
            var ask = Ui.Field("turn on the hud and switch to my Competitive profile", 560);
            var reply = new StackPanel();
            void RunPilot()
            {
                var (text, actions, goPage, aiRequest) = BrainStrap.Core.Platform.Pilot.Interpret(s, ask.Text);
                reply.Children.Clear();
                reply.Children.Add(Ui.Small(text, 10));
                if (aiRequest != null) { PendingRequest = aiRequest; host.Refresh(); return; }
                if (actions.Count > 0)
                {
                    var g = BrainStrap.Core.Platform.Grammar.Load();
                    foreach (var a in actions) reply.Children.Add(Ui.Status("info", g.ActionLabel(a), ""));
                    reply.Children.Add(Ui.Buttons(Ui.Pill("Do it", () => { BrainStrap.Core.Platform.Flow.Run(s, actions); AppState.Save("BrainPilot: " + ask.Text); host.Refresh(); })));
                }
                else if (goPage != null) reply.Children.Add(Ui.Buttons(Ui.Pill("Take me there", () => host.Go(goPage))));
            }
            body.Children.Add(Ui.Card(Ui.Lbl("★ BrainPilot"), Ui.Title("Tell BrainStrap what to do", 15),
                Ui.Desc("Controls the launcher itself: profiles, HUD, apps, themes, backups, and where things are. It shows what it understood before anything happens."),
                new Border { Height = 10 }, ask, Ui.Buttons(Ui.Pill("Ask BrainPilot", RunPilot)), reply));
        }

        // ── PC Analyzer ──
        var pcBox = new StackPanel();
        pcBox.Children.Add(Ui.Small("Reading your hardware…"));
        body.Children.Add(Ui.Card(Ui.Lbl("AI PC Analyzer"), pcBox));
        PcInfo? pc = null;
        _ = Task.Run(() => PcAnalyzer.Detect()).ContinueWith(t => host.Dispatcher.Invoke(() =>
        {
            pcBox.Children.Clear();
            if (t.IsFaulted) { pcBox.Children.Add(Ui.Small("Couldn't read the hardware: " + t.Exception?.InnerException?.Message)); return; }
            pc = t.Result;
            var grid = Ui.Cols(4,
                Ui.Stat(pc.Score.ToString(), "BrainScore / 100", pc.Score >= 70 ? "Ok" : pc.Score >= 40 ? "Warn" : "Bad"),
                Ui.Stat(pc.TierName, "performance class"),
                Ui.Stat(pc.Refresh + " Hz", "monitor"),
                Ui.Stat(Ui.Mb(pc.RamBytes), "memory"));
            pcBox.Children.Add(grid);
            pcBox.Children.Add(Ui.KV("Processor", $"{pc.Cpu} · {pc.Cores} cores / {pc.Threads} threads"));
            pcBox.Children.Add(Ui.KV("Graphics", pc.Gpu + (pc.VramBytes > 0 ? $" · {Ui.Mb(pc.VramBytes)}" : "")));
            pcBox.Children.Add(Ui.KV("Type", (pc.Laptop ? "Laptop" : "Desktop") + (pc.Laptop ? (pc.OnBattery ? $" · on battery ({pc.BatteryPercent}%)" : " · plugged in") : "")));
            var sessions = History.Sessions().Where(r => r.Fps != null).Take(10).ToList();
            if (sessions.Count > 0) pcBox.Children.Add(Ui.KV("Roblox on this PC", $"{sessions.Average(r => r.Fps!.AvgFps):0} FPS average over your last {sessions.Count} measured sessions"));
            foreach (var n in pc.Notes) pcBox.Children.Add(Ui.Status("warn", n, ""));
        }));

        // ── Settings Builder ──
        var input = Ui.Field(PendingRequest ?? "");
        input.AcceptsReturn = false;
        input.TextWrapping = TextWrapping.Wrap;
        input.MinHeight = 46;
        var useCloud = CloudAi.Ready(s);
        var preview = new StackPanel();
        async Task Ask(string request, string title, string? assignPlace = null, string? newProfileName = null)
        {
            if (request.Trim().Length == 0) return;
            preview.Children.Clear();
            preview.Children.Add(Ui.Small("Thinking…"));
            pc ??= await Task.Run(() => PcAnalyzer.Detect());
            var current = new Dictionary<string, string>(s.Current.Values);
            var measured = History.Benchmarks().Count > 0;
            BrainResult? result;
            BrainPrediction prediction;
            if (useCloud && CloudAi.Ready(s))
            {
                var (r, err) = await CloudAi.AskAsync(s, request, pc, current);
                if (r == null) { preview.Children.Clear(); preview.Children.Add(Ui.Status("warn", "Cloud AI: " + err, "Using the local engine instead.")); }
                result = r;
                prediction = r != null ? Engine.Predict(current, r.Values, pc.Refresh, measured) : default!;
                if (r == null)
                {
                    var (_, lr, lp) = Engine.Ask(request, pc, current, measured);
                    result = lr;
                    prediction = lp;
                }
            }
            else
            {
                var (_, r, p) = Engine.Ask(request, pc, current, measured);
                result = r;
                prediction = p;
            }
            ShowPreview(host, preview, title, result!, prediction, assignPlace, newProfileName);
        }

        var examples = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var e in Examples)
        {
            var ex = e;
            var b = Ui.Ghost(ex.Length > 46 ? ex[..46] + "…" : ex, () => { input.Text = ex; _ = Ask(ex, "BrainAI's plan"); });
            b.ToolTip = ex;
            examples.Children.Add(b);
        }
        var cloudToggle = Ui.Row(Ui.Toggle(useCloud, on => useCloud = on), Ui.Text("  Use cloud AI (your own API key)", 12.5, "Muted"));
        cloudToggle.Visibility = CloudAi.Ready(s) ? Visibility.Visible : Visibility.Collapsed;
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) _ = Ask(input.Text, "BrainAI's plan"); };

        body.Children.Add(Ui.Card(
            Ui.Lbl("AI Settings Builder"),
            Ui.Title("Tell BrainAI what you want, in your own words", 16),
            Ui.Desc("It checks only settings BrainStrap supports, explains every change, estimates the effect, and waits for you to apply it.", 4),
            new Border { Height = 12 },
            input,
            Ui.Buttons(Ui.AsyncPill("Ask BrainAI", () => Ask(input.Text, "BrainAI's plan")), cloudToggle),
            examples,
            preview));
        if (PendingRequest != null) { var r = PendingRequest; PendingRequest = null; _ = Ask(r, "BrainBoost"); }

        // ── BrainBoost / One Click Optimization ──
        body.Children.Add(Ui.Card(
            Ui.Lbl("BrainBoost · One Click Optimization"),
            Ui.Title("The safe, tested setup for this exact PC", 15),
            Ui.Desc("Picks performance settings from your hardware: smoother on weak PCs, smooth and still good-looking on strong ones. You'll see the preview first."),
            Ui.Buttons(Ui.AsyncPill("Run BrainBoost", async () =>
            {
                pc ??= await Task.Run(() => PcAnalyzer.Detect());
                var req = pc.Tier <= 0 ? "max fps on an old pc, smoother" : pc.Tier == 1 ? "smoother while keeping the graphics looking good" + (pc.Laptop ? " on my laptop" : "") : "smooth and looking good";
                await Ask(req, "BrainBoost");
            }))));

        // ── Profile Generator ──
        var placeBox = Ui.Field("", 200, true);
        var genMsg = Ui.Small("");
        body.Children.Add(Ui.Card(
            Ui.Lbl("AI Profile Generator"),
            Ui.Title("Build a profile for one game", 15),
            Ui.Desc("Enter the game's place ID (the number in its roblox.com address). BrainAI reads the game's genre and builds a matching profile that switches on automatically for that game."),
            new Border { Height = 10 },
            Ui.Row(placeBox, new Border { Width = 10 }, Ui.AsyncPill("Generate", async () =>
            {
                var id = new string(placeBox.Text.Where(char.IsAsciiDigit).ToArray());
                if (!RobloxApi.IsId(id)) { Ui.Msg(genMsg, "That isn't a place ID.", "Warn"); return; }
                var g = await RobloxApi.GameAsync(id);
                if (g == null) { Ui.Msg(genMsg, "Couldn't find that game.", "Warn"); return; }
                var text = Engine.GenreText(g.Genre);
                Ui.Msg(genMsg, $"{g.Name} · {(g.Genre.Length > 0 ? g.Genre : "no genre listed")} → \"{text}\"", "Muted");
                await Ask(text, "Profile for " + g.Name, id, ProfileName(g.Name));
            })),
            genMsg));

        // ── FPS Doctor ──
        var docBox = new StackPanel();
        body.Children.Add(Ui.Card(
            Ui.Lbl("AI FPS Doctor"),
            Ui.Title("Why does my FPS drop?", 15),
            Ui.Desc("Combines your last benchmark or session, live hardware numbers (if Roblox is open), background apps and your settings. Each finding has a one-click fix."),
            Ui.Buttons(Ui.AsyncPill("Run FPS Doctor", async () =>
            {
                docBox.Children.Clear();
                docBox.Children.Add(Ui.Small("Examining…"));
                pc ??= await Task.Run(() => PcAnalyzer.Detect());
                var fps = History.Benchmarks().FirstOrDefault()?.Stats ?? History.Sessions().FirstOrDefault(r => r.Fps != null)?.Fps;
                LiveSample? hw = History.ReadLive()?.Hw;
                if (hw == null && Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault() is { } rp)
                {
                    using var m = new RobloxMonitor(rp);
                    m.Sample();
                    await Task.Delay(1500);
                    hw = m.Sample();
                }
                var apps = await BackgroundAdvisor.ScanAsync();
                var findings = Doctor.FpsDoctor(s, pc, fps, hw, apps);
                docBox.Children.Clear();
                docBox.Children.Add(Ui.Small(fps != null ? $"Based on a measurement from {(History.Benchmarks().FirstOrDefault()?.When ?? History.Sessions().First(r => r.Fps != null).Start):d MMM HH:mm}." : "No FPS measurement yet, so this is based on hardware and settings only.", 0));
                RenderFindings(host, docBox, findings);
            })),
            docBox));

        // ── Lag Detective ──
        var lagBox = new StackPanel();
        body.Children.Add(Ui.Card(
            Ui.Lbl("AI Lag Detective"),
            Ui.Title("Is it FPS lag or network lag?", 15),
            Ui.Desc("Play normally while it runs. It measures frame times and pings your router, the internet and the game server at the same time, then tells you which one is the problem."),
            Ui.Buttons(Ui.AsyncPill("Run 30-second test", async () =>
            {
                lagBox.Children.Clear();
                if (Process.GetProcessesByName("RobloxPlayerBeta").Length == 0) { lagBox.Children.Add(Ui.Status("warn", "Start a game first", "The test needs Roblox running.")); return; }
                var status = Ui.Small("Testing… keep playing for 30 seconds.");
                lagBox.Children.Add(status);
                using var cap = new FrameCapture();
                var capturing = FrameCapture.HasPermission() && cap.Start(s, 30);
                var serverIp = History.ReadLive()?.ServerIp;
                var gw = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
                var tRouter = gw != null ? Pinger.BurstAsync(gw, 60, 450) : Task.FromResult<PingStats>(null!);
                var tNet = Pinger.BurstAsync("1.1.1.1", 60, 450);
                var tSrv = serverIp != null ? Pinger.BurstAsync(serverIp, 60, 450) : Task.FromResult<PingStats>(null!);
                await Task.WhenAll(tRouter, tNet, tSrv);
                if (capturing) await cap.WaitAsync(TimeSpan.FromSeconds(8));
                var frames = capturing ? FrameStats.From(cap.Snapshot()) : null;
                var v = Doctor.LagDetective(frames, serverIp != null ? tSrv.Result : null, gw != null ? tRouter.Result : null, tNet.Result);
                lagBox.Children.Clear();
                lagBox.Children.Add(Ui.Status(v.Kind is "none" ? "ok" : v.Kind is "unknown" ? "info" : "bad", v.Headline, v.Detail));
                foreach (var e in v.Evidence) lagBox.Children.Add(Ui.Text("• " + e, 12.5, "Muted"));
                if (!capturing) lagBox.Children.Add(Ui.Small("Frame times weren't measured: set up FPS measuring on the Performance page for a full answer.", 8));
            })),
            lagBox));

        // ── Compatibility + Troubleshooter ──
        var compBox = new StackPanel();
        RenderFindings(host, compBox, Doctor.Compatibility(s));
        body.Children.Add(Ui.Card(Ui.Lbl("AI Compatibility Checker"), Ui.Desc("Checks the active profile for invalid, conflicting or possibly outdated settings.", 0), new Border { Height = 10 }, compBox));

        var troubleBox = new StackPanel();
        var report = Doctor.Troubleshoot();
        if (report == null) troubleBox.Children.Add(Ui.Small("No Roblox logs yet. Play a game first."));
        else RenderReport(troubleBox, report);
        body.Children.Add(Ui.Card(Ui.Lbl("AI Troubleshooter"), Ui.Desc("How your last Roblox session ended, explained.", 0), new Border { Height = 10 }, troubleBox,
            Ui.Buttons(Ui.Ghost("More in Tools → Crash Report Analyzer", () => host.Go("tools")))));

        // ── Cloud mode ──
        var key = new PasswordBox { Height = 36, Padding = new Thickness(10, 8, 10, 8), Width = 360 };
        var model = Ui.Field(s.Ai.Model, 220, true);
        var keyMsg = Ui.Small(s.Ai.KeyProtected != null ? "A key is saved (encrypted for your Windows user)." : "No key saved.");
        body.Children.Add(Ui.Card(
            Ui.Lbl("Cloud AI (optional)"),
            Ui.ToggleRow("Let BrainAI use a cloud model for the Settings Builder", "Off by default; everything above runs on this PC. When on, only your request, a hardware summary and the setting list are sent to Anthropic with your own API key. No account, logs or files. Answers are still checked against the catalog.",
                s.Ai.CloudEnabled, on => { s.Ai.CloudEnabled = on; AppState.Save(); }),
            Ui.Lbl("API key (from console.anthropic.com)"), key,
            Ui.Lbl("Model", 12), model,
            Ui.Buttons(
                Ui.Pill("Save", () =>
                {
                    if (key.Password.Trim().Length > 0) s.Ai.KeyProtected = Secret.Protect(key.Password.Trim());
                    s.Ai.Model = model.Text.Trim().Length > 0 ? model.Text.Trim() : "claude-haiku-5-5";
                    key.Clear();
                    AppState.Save("Cloud AI settings");
                    Ui.Msg(keyMsg, "Saved. The key is encrypted for your Windows user.", "Ok");
                }),
                Ui.Ghost("Remove key", () => { s.Ai.KeyProtected = null; AppState.Save("Cloud AI key removed"); Ui.Msg(keyMsg, "Key removed.", "Ok"); }),
                Ui.Ghost("Get a key", () => Ui.Open("https://console.anthropic.com/"))),
            keyMsg));
        return page;
    }

    static string ProfileName(string game)
    {
        var n = System.Text.RegularExpressions.Regex.Replace(game, @"[^\w \-\.]", "").Trim();
        return (n.Length > 28 ? n[..28] : n).Trim() is { Length: > 0 } x ? x : "Game profile";
    }

    public static void ShowPreview(MainWindow host, StackPanel box, string title, BrainResult r, BrainPrediction p, string? assignPlace = null, string? newProfileName = null)
    {
        var s = AppState.S;
        box.Children.Clear();
        var card = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        card.Children.Add(Ui.Lbl("Preview · nothing is changed yet"));
        card.Children.Add(Ui.Title(title, 17));
        if (r.Changes.Count == 0)
        {
            card.Children.Add(Ui.Small("Your current profile already matches this request. Nothing to change."));
            box.Children.Add(card);
            return;
        }
        card.Children.Add(Ui.Small($"{r.Changes.Count} change(s) to the '{s.ActiveProfile}' profile:", 4));
        foreach (var c in r.Changes)
        {
            var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(Ui.Row(Ui.Title(c.Name, 14), Ui.Text($"   {c.From}  →  {c.To}", 13, "Accent", FontWeights.SemiBold)));
            if (c.Why.Length > 0) row.Children.Add(Ui.Small(c.Why));
            card.Children.Add(row);
        }
        var pred = Ui.Text(p.Describe(), 13, "Ink", FontWeights.SemiBold);
        pred.Margin = new Thickness(0, 16, 0, 0);
        card.Children.Add(pred);
        var msg = Ui.Small("", 6);
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (assignPlace == null)
            buttons.Children.Add(Ui.Pill($"Apply to '{s.ActiveProfile}'", () =>
            {
                s.Current.Values = new Dictionary<string, string>(r.Values);
                AppState.Save($"BrainAI: {title}");
                Ui.Msg(msg, "Applied. It takes effect the next time you press Play. Roll back any time in Tools → Timeline.", "Ok");
            }));
        buttons.Children.Add(Ui.Ghost(assignPlace != null ? $"Save as '{newProfileName}' for this game" : "Save as a new profile", () =>
        {
            var name = newProfileName ?? Prompt.Ask(host, "New profile", "Name for the new profile", "BrainAI");
            if (name == null) return;
            var unique = name;
            for (var i = 2; s.Profiles.ContainsKey(unique); i++) unique = $"{name} {i}";
            s.Profiles[unique] = new FlagProfile { Values = new Dictionary<string, string>(r.Values), Custom = new Dictionary<string, string>(s.Current.Custom) };
            if (assignPlace != null) s.GameProfiles[assignPlace] = unique;
            else s.ActiveProfile = unique;
            AppState.Save($"BrainAI created profile {unique}");
            Ui.Msg(msg, assignPlace != null ? $"Saved. '{unique}' now switches on automatically for this game." : $"Saved as '{unique}' and made active.", "Ok");
        }));
        buttons.Children.Add(Ui.Ghost("Discard", () => box.Children.Clear()));
        card.Children.Add(buttons);
        card.Children.Add(msg);
        var frame = new Border { Child = card, BorderThickness = new Thickness(0, 1, 0, 0) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        box.Children.Add(frame);
    }

    public static void RenderFindings(MainWindow host, StackPanel box, List<Finding> findings)
    {
        box.Children.Clear();
        foreach (var f in findings)
        {
            Button? fix = null;
            if (f.FixLabel != null)
            {
                var finding = f;
                fix = Ui.Ghost(f.FixLabel, () =>
                {
                    Doctor.ApplyFix(AppState.S, finding);
                    MessageBox.Show(host, "Done. It applies the next time you press Play, and you can undo it in Tools → Timeline.", "BrainStrap");
                });
            }
            box.Children.Add(Ui.Status(f.Level, f.Title, f.Detail, fix));
        }
    }

    public static void RenderReport(StackPanel box, CrashReport r)
    {
        box.Children.Add(Ui.Status(r.Severity, r.Title, r.Explanation));
        foreach (var f in r.Fixes) box.Children.Add(Ui.Text("→ " + f, 13, "Ink"));
        if (r.Evidence.Count > 0)
        {
            box.Children.Add(Ui.Lbl("From the log", 12));
            foreach (var e in r.Evidence) box.Children.Add(Ui.Text(e, 11.5, "Dim", mono: true));
        }
    }
}
