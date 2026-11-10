using System.Windows;
using System.Windows.Controls;
using BrainStrap.Core;
using BrainStrap.Core.Servers;
using Microsoft.Win32;

namespace BrainStrap.UI.Pages;

/// <summary>Advanced Tools: timeline + rollback, repair center, cache manager, update inspector, crash analyzer, flag validator.</summary>
internal static class ToolsPage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("Tools", "Fix things, clean up, and undo anything. Every settings change is kept on a timeline you can roll back.", out var body);

        // ── Settings Timeline + Instant Rollback ──
        var tl = new StackPanel();
        var entries = Timeline.List();
        if (entries.Count == 0) tl.Children.Add(Ui.Small("Changes you make will appear here.", 0));
        foreach (var e in entries.Take(30))
        {
            var entry = e;
            tl.Children.Add(Ui.Status("info", $"{e.When:d MMM, HH:mm:ss}", e.Summary,
                entry == entries[0] ? (UIElement)Ui.Text("current", 12, "Dim") : Ui.Ghost("Roll back", () =>
                {
                    if (MessageBox.Show(host, $"Put every setting back to how it was on {entry.When:d MMM, HH:mm}?\n\nYou can undo this too: the current state is saved first.", "BrainStrap",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Timeline.Restore(entry.File);
                    Themes.Apply(AppState.S);
                    host.Refresh();
                })));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Settings Timeline · Instant Rollback"), tl));

        // ── Repair Center ──
        var repair = new StackPanel();
        async Task RunRepair()
        {
            repair.Children.Clear();
            repair.Children.Add(Ui.Small("Checking…"));
            var checks = await RepairCenter.RunAsync(s);
            repair.Children.Clear();
            foreach (var c in checks)
            {
                var check = c;
                Button? fix = null;
                if (c.FixLabel != null && c.Fix != null)
                    fix = Ui.Ghost(c.FixLabel, () => { var result = check.Fix!(); MessageBox.Show(host, result, "BrainStrap"); _ = RunRepair(); });
                repair.Children.Add(Ui.Status(c.Level, c.Title, c.Detail, fix));
            }
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Roblox Repair Center"), Ui.Desc("Checks the Roblox install, the Play button, the flag file, mods, old versions, disk space and the last session.", 0),
            Ui.Buttons(Ui.AsyncPill("Run checks", RunRepair)), repair));

        // ── Cache Manager ──
        var cache = new StackPanel();
        void DrawCache()
        {
            cache.Children.Clear();
            var items = CacheManager.Scan();
            foreach (var it in items)
            {
                var item = it;
                cache.Children.Add(Ui.Status(it.Bytes > 300L * 1024 * 1024 ? "warn" : "info", $"{it.Name} · {Ui.Mb(it.Bytes)}", it.What,
                    it.Bytes > 0 ? Ui.Ghost("Clean", () => { MessageBox.Show(host, CacheManager.Clean(item.Path), "BrainStrap"); DrawCache(); }) : null));
            }
            cache.Children.Add(Ui.Small($"Total {Ui.Mb(items.Sum(i => i.Bytes))}. Your Roblox login, the installed player and your settings are never touched.", 4));
        }
        DrawCache();
        body.Children.Add(Ui.Card(Ui.Lbl("Cache Manager"), cache,
            Ui.Buttons(Ui.Ghost("Clean everything listed", () => { MessageBox.Show(host, CacheManager.CleanAll(), "BrainStrap"); DrawCache(); }))));

        // ── Update Inspector ──
        var upd = new StackPanel();
        async Task DrawUpdates()
        {
            upd.Children.Clear();
            var list = await UpdateInspector.RefreshAsync();
            if (list.Count == 0) { upd.Children.Add(Ui.Small("No Roblox versions recorded yet.", 0)); return; }
            var newest = list[0];
            var days = (DateTime.Now - newest.FirstSeen).TotalDays;
            upd.Children.Add(Ui.KV("Installed", newest.Folder + (newest.Version.Length > 0 ? " · " + newest.Version : "")));
            upd.Children.Add(Ui.KV("Last update", days < 1 ? "today" : $"{days:0} day(s) ago"));
            foreach (var v in list.Skip(1).Take(10))
                upd.Children.Add(Ui.Text($"{v.FirstSeen:d MMM yyyy} · {v.Folder}{(v.Version.Length > 0 ? " · " + v.Version : "")}", 12, "Muted", mono: true));
        }
        _ = DrawUpdates();
        body.Children.Add(Ui.Card(Ui.Lbl("Update Inspector"), Ui.Desc("Each Roblox version this PC has had. After an update, flags that stop working are a sign Roblox changed them.", 0), new Border { Height = 8 }, upd));

        // ── Crash Report Analyzer ──
        var crash = new StackPanel();
        void Analyze(string file)
        {
            crash.Children.Clear();
            crash.Children.Add(Ui.Small(System.IO.Path.GetFileName(file), 0));
            BrainAiPage.RenderReport(crash, CrashAnalyzer.Analyze(file));
        }
        var latest = LogWatcher.NewestPlayerLog();
        if (latest != null) Analyze(latest); else crash.Children.Add(Ui.Small("No Roblox logs found yet.", 0));
        body.Children.Add(Ui.Card(Ui.Lbl("Crash Report Analyzer"), Ui.Desc("Reads Roblox's log and explains how the session ended. Only short matching lines are shown, with anything that looks like a login token removed.", 0),
            Ui.Buttons(Ui.Ghost("Pick another log", () =>
            {
                var dlg = new OpenFileDialog { Filter = "Roblox logs (*.log)|*.log", InitialDirectory = LogWatcher.LogDir };
                if (dlg.ShowDialog(host) == true) Analyze(dlg.FileName);
            }), Ui.Ghost("Open logs folder", () => Ui.Open(LogWatcher.LogDir))),
            crash));

        // ── FastFlag Validator ──
        var input = Ui.Field("{\n  \"DFIntTaskSchedulerTargetFps\": 144\n}", double.NaN, true);
        input.AcceptsReturn = true;
        input.MinHeight = 110;
        input.TextWrapping = TextWrapping.Wrap;
        var results = new StackPanel();
        body.Children.Add(Ui.Card(
            Ui.Lbl("FastFlag Validator"),
            Ui.Desc("Paste a ClientAppSettings.json (from a video, a friend or a website) to see what's valid, what Roblox may ignore, and what BrainStrap blocks because it could change gameplay.", 0),
            new Border { Height = 10 },
            input,
            Ui.Buttons(Ui.Pill("Validate", () =>
            {
                results.Children.Clear();
                foreach (var i in FlagValidator.ValidateJson(input.Text))
                    results.Children.Add(Ui.Status(i.Level, i.Flag, i.Message));
            })),
            results));
        return page;
    }
}
