using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrainStrap.Core;
using BrainStrap.Core.Perf;
using BrainStrap.Core.Servers;
using Microsoft.Win32;

namespace BrainStrap.UI.Pages;

/// <summary>BrainLife: the HUD, game library, playtime, screenshots, battery profiles, Game Mode+, companion apps, jump list, backup.</summary>
internal static class LifePage
{
    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("BrainLife", "Everything around the game: an FPS overlay, your library and playtime, screenshots, and BrainStrap looking after your PC while you play.", out var body);

        // ── BrainHUD ──
        var op = new Slider { Minimum = 0.3, Maximum = 1, Value = s.Hud.Opacity, Width = 220 };
        op.LostMouseCapture += (_, _) => { s.Hud.Opacity = op.Value; AppState.Save("HUD opacity"); };
        body.Children.Add(Ui.Card(
            Ui.Lbl("BrainHUD"),
            Ui.ToggleRow("Show the HUD while playing", "A small, click-through overlay with FPS, ping, CPU/GPU, temperatures and session time. It's a separate window on top of the game, not something inside Roblox. Ctrl+Shift+H hides it.",
                s.Hud.Enabled, on => { s.Hud.Enabled = on; AppState.Save("HUD " + (on ? "on" : "off")); }),
            Ui.Lbl("Corner"),
            Ui.Chips(new[] { ("Top left", "top-left"), ("Top right", "top-right"), ("Bottom left", "bottom-left"), ("Bottom right", "bottom-right") }, s.Hud.Corner,
                v => { s.Hud.Corner = v; AppState.Save("HUD corner"); }),
            Ui.Row(Ui.Text("Opacity  ", 13, "Muted"), op),
            new Border { Height = 10 },
            Ui.ToggleRow("FPS", "", s.Hud.ShowFps, on => { s.Hud.ShowFps = on; AppState.Save(); }),
            Ui.ToggleRow("Ping", "", s.Hud.ShowPing, on => { s.Hud.ShowPing = on; AppState.Save(); }),
            Ui.ToggleRow("CPU, GPU, RAM and temperatures", "", s.Hud.ShowHardware, on => { s.Hud.ShowHardware = on; AppState.Save(); }),
            Ui.ToggleRow("Session clock", "", s.Hud.ShowClock, on => { s.Hud.ShowClock = on; AppState.Save(); }),
            Ui.Buttons(Ui.AsyncPill("Preview for 5 seconds", async () =>
            {
                var hud = new Hud(s);
                hud.Show();
                var fake = new LiveState
                {
                    Measuring = true, Fps = 143, Low1 = 97, Started = DateTime.UtcNow.AddMinutes(-42),
                    Ping = new PingStats(30, 38, 4, 0, 31, 52),
                    Hw = new LiveSample(31, 64, 1_450_000_000, 2_100_000_000, 68, 61, 6_000_000_000, 22),
                };
                for (var i = 0; i < 5; i++) { hud.Update(fake, DateTime.UtcNow - fake.Started); await Task.Delay(1000); }
                hud.Close();
            }, ghost: true))));

        // ── Game Library + Playtime Journal ──
        var lib = new WrapPanel();
        var games = History.Playtime(DateTime.Now.AddDays(-90)).Where(g => g.placeId != null).Take(12).ToList();
        var fromLogs = games.Count == 0 ? LogWatcher.RecentPlaces(20).Take(8).Select(p => p.placeId).ToList() : new List<string>();
        foreach (var g in games) lib.Children.Add(GameTile(host, g.placeId!, g.game, $"{g.minutes:0} min · {g.sessions} session(s)"));
        foreach (var id in fromLogs) lib.Children.Add(GameTile(host, id, "Place " + id, "recently played"));
        if (lib.Children.Count == 0) lib.Children.Add(Ui.Small("Games you play show up here with one-click Play.", 0));
        var week = History.Playtime(DateTime.Now.AddDays(-7));
        var total = History.Playtime();
        var journal = new StackPanel();
        journal.Children.Add(Ui.Cols(3,
            Ui.Stat($"{week.Sum(x => x.minutes) / 60:0.#} h", "this week"),
            Ui.Stat($"{total.Sum(x => x.minutes) / 60:0.#} h", "all time (tracked)"),
            Ui.Stat(total.Count.ToString(), "games")));
        foreach (var g in week.Take(6)) journal.Children.Add(Ui.KV(g.game, $"{g.minutes:0} min this week · {g.sessions} session(s)"));
        body.Children.Add(Ui.Card(Ui.Lbl("Game Library"), lib));
        body.Children.Add(Ui.Card(Ui.Lbl("Playtime Journal"), journal));

        // ── Screenshot Gallery ──
        var shots = Screenshots.List(48);
        var grid = new WrapPanel();
        foreach (var f in shots)
        {
            var file = f;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(file.FullName);
                bmp.DecodePixelWidth = 220;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                var img = new Image { Source = bmp, Width = 180, Height = 101, Stretch = Stretch.UniformToFill };
                var btn = new Button { Content = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = img }, Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Cursor = Ui.Hand, ToolTip = $"{file.Name} · {file.LastWriteTime:d MMM yyyy HH:mm}" };
                btn.Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
                btn.Click += (_, _) => Ui.Open(file.FullName);
                grid.Children.Add(btn);
            }
            catch
            {
            }
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Screenshot Gallery"),
            shots.Count == 0 ? (UIElement)Ui.Small("No screenshots yet. Roblox saves them to Pictures\\Roblox when you press Print Screen in a game.", 0) : grid,
            Ui.Buttons(Ui.Ghost("Open folder", () => Ui.Open(Screenshots.Folder)))));

        // ── battery, game mode, companions, jump list ──
        var profiles = new DarkCombo { Width = 220 };
        foreach (var p in s.Profiles.Keys) profiles.Items.Add(p);
        profiles.SelectedItem = s.Life.BatteryProfile;
        profiles.SelectionChanged += (_, _) => { s.Life.BatteryProfile = profiles.SelectedItem as string; AppState.Save("Battery profile"); };
        body.Children.Add(Ui.Card(
            Ui.Lbl("Battery-Aware Profiles"),
            Ui.ToggleRow("Switch profile on battery", "When your laptop is unplugged, BrainStrap uses this profile instead (unless the game has its own).",
                s.Life.BatteryAware, on => { s.Life.BatteryAware = on; AppState.Save("Battery-aware " + (on ? "on" : "off")); }),
            Ui.Row(Ui.Text("Profile on battery  ", 13, "Muted"), profiles),
            Ui.Desc("Tip: ask BrainAI for \"my laptop gets hot and loud\" and save the result as a profile for this.")));

        body.Children.Add(Ui.Card(
            Ui.Lbl("Game Mode+"),
            Ui.ToggleRow("Give Roblox priority while it runs", "Switches Windows to the High performance power plan (only when plugged in) and gives Roblox above-normal priority. Both go back to how they were when you stop playing.",
                s.Life.GameModePlus, on => { s.Life.GameModePlus = on; AppState.Save("Game Mode+ " + (on ? "on" : "off")); }),
            GameModePlus.IsPowerSaver() ? Ui.Status("warn", "Windows is in Power saver mode right now", "That limits FPS a lot. Game Mode+ switches it for you while you play.") : (UIElement)new Border()));

        var comp = new StackPanel();
        foreach (var c in s.Life.Companions.ToList())
        {
            var item = c;
            comp.Children.Add(Ui.Status("info", System.IO.Path.GetFileNameWithoutExtension(c.Path), c.Path + (c.CloseAfter ? " · closes when you stop playing" : " · stays open"),
                Ui.Row(
                    Ui.Ghost(item.CloseAfter ? "Keep open" : "Close after", () => { item.CloseAfter = !item.CloseAfter; AppState.Save("Companion app"); host.Refresh(); }),
                    Ui.Ghost("Remove", () => { s.Life.Companions.Remove(item); AppState.Save("Companion app removed"); host.Refresh(); }))));
        }
        body.Children.Add(Ui.Card(
            Ui.Lbl("Companion Apps"),
            Ui.Desc("Programs to start with Roblox, like Discord, OBS or a music app. If one is already open it's left alone.", 0),
            new Border { Height = 8 },
            comp,
            Ui.Buttons(Ui.Ghost("Add a program", () =>
            {
                var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
                if (dlg.ShowDialog(host) != true) return;
                s.Life.Companions.Add(new Companion { Path = dlg.FileName });
                AppState.Save("Companion app added");
                host.Refresh();
            }))));

        body.Children.Add(Ui.Card(
            Ui.Lbl("Taskbar"),
            Ui.ToggleRow("Jump List", "Right-click BrainStrap in the taskbar to jump straight into favourite and recent games.",
                s.Life.JumpList, on => { s.Life.JumpList = on; AppState.Save("Jump list"); JumpLists.Update(s); })));

        // ── backup ──
        var msg = Ui.Small("");
        body.Children.Add(Ui.Card(
            Ui.Lbl("Backup & Restore"),
            Ui.Desc("Your whole setup (settings, profiles, themes, favourites, layout) in one file. The cloud AI key and private-server links are left out on purpose.", 0),
            Ui.Buttons(
                Ui.Pill("Export backup", () =>
                {
                    var dlg = new SaveFileDialog { Filter = "BrainStrap backup (*.json)|*.json", FileName = $"BrainStrap-backup-{DateTime.Now:yyyyMMdd}.json" };
                    if (dlg.ShowDialog(host) != true) return;
                    Backup.Export(s, dlg.FileName);
                    Ui.Msg(msg, "Backup saved.", "Ok");
                }),
                Ui.Ghost("Restore from backup", () =>
                {
                    var dlg = new OpenFileDialog { Filter = "BrainStrap backup (*.json)|*.json" };
                    if (dlg.ShowDialog(host) != true) return;
                    var restored = Backup.Import(dlg.FileName);
                    AppState.S = restored;
                    AppState.Save("Restored from backup");
                    Themes.Apply(restored);
                    host.Refresh();
                })),
            msg));

        body.Children.Add(Ui.Card(Ui.Lbl("Command Palette"), Ui.Title("Press Ctrl+K anywhere in BrainStrap", 15), Ui.Desc("Type to jump to any page or run any action: BrainBoost, benchmark, rollback, clean caches, open folders…")));
        return page;
    }

    public static FrameworkElement GameTile(MainWindow host, string placeId, string name, string sub)
    {
        var icon = new Image { Width = 64, Height = 64, Stretch = Stretch.UniformToFill };
        var iconFrame = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = icon, Margin = new Thickness(0, 0, 12, 0) };
        iconFrame.SetResourceReference(Border.BackgroundProperty, "Pill");
        var title = Ui.Title(name, 13.5);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        var info = new StackPanel { Width = 150 };
        info.Children.Add(title);
        info.Children.Add(Ui.Text(sub, 11.5, "Dim"));
        var play = Ui.Pill("Play", () => JoinLinks.Join(placeId, null));
        play.Height = 28;
        play.Margin = new Thickness(0, 6, 0, 0);
        play.HorizontalAlignment = HorizontalAlignment.Left;
        info.Children.Add(play);
        var row = Ui.Row(iconFrame, info);
        var card = new Border { Child = row, Padding = new Thickness(12), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 10, 10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Panel2");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        _ = Task.Run(async () =>
        {
            var g = await RobloxApi.GameAsync(placeId);
            if (g == null) return;
            var file = await RobloxApi.IconAsync(g.UniverseId);
            await host.Dispatcher.InvokeAsync(() =>
            {
                title.Text = g.Name;
                if (file == null) return;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.UriSource = new Uri(file); bmp.DecodePixelWidth = 128; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                    icon.Source = bmp;
                }
                catch { }
            });
        });
        return card;
    }
}
