using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainStrap.Core;
using Microsoft.Win32;

namespace BrainStrap.UI.Pages;

/// <summary>Customization Studio: theme editor with live preview, gallery and share codes, fonts, minimal mode, backgrounds, launch animation and sound, layout builder, profile themes.</summary>
internal static class StudioPage
{
    static readonly (string key, string label)[] ColorKeys =
    {
        ("Bg", "Background"), ("Panel", "Cards"), ("Panel2", "Inputs"), ("Ink", "Text"), ("Muted", "Secondary text"),
        ("Dim", "Faint text"), ("Pill", "Buttons"), ("PillHover", "Button hover"), ("Accent", "Accent"),
    };

    static readonly (string id, string label)[] WidgetNames =
    {
        ("play", "Quick Play"), ("status", "Status"), ("boost", "BrainBoost"), ("session", "Last session"),
        ("server", "Server"), ("suggest", "Suggestions"), ("tip", "Tip"),
    };

    public static FrameworkElement Build(MainWindow host)
    {
        var s = AppState.S;
        var page = Ui.Page("Studio", "Make BrainStrap yours: themes you can edit and share, fonts, backgrounds, the launch screen and sound, and the Home layout.", out var body);
        void Applied(string why) { Themes.Apply(s); Dwm.ApplyTitleBar(host, Themes.Effective(s).Dark); AppState.Save(why); }

        // ── BrainThemes gallery ──
        var gallery = new WrapPanel();
        void AddSwatch(ThemeDef t, WrapPanel into)
        {
            var b = new Button { Cursor = Ui.Hand, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = t.Name };
            var tile = new Border { Width = 128, Height = 82, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(s.Theme == t.Id ? 2 : 1),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.Bg)) };
            tile.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Theme == t.Id ? t.Accent : t.Pill));
            var inner = new StackPanel { Margin = new Thickness(10) };
            var name = new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.Ink)) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var hex in new[] { t.Panel, t.Pill, t.Muted, t.Accent })
                bar.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 5, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) });
            inner.Children.Add(name);
            inner.Children.Add(bar);
            tile.Child = inner;
            b.Content = tile;
            b.Template = NoChrome();
            b.Click += (_, _) => { s.Theme = t.Id; s.Studio.Minimal = false; Applied("Theme " + t.Name); host.Refresh(); };
            into.Children.Add(b);
        }
        foreach (var t in Themes.BuiltIn) AddSwatch(t, gallery);
        var featured = new WrapPanel();
        foreach (var t in Themes.Gallery) AddSwatch(t, featured);
        var mine = new WrapPanel();
        foreach (var t in s.Studio.CustomThemes) AddSwatch(t, mine);
        var community = new WrapPanel();
        var galleryUrl = Ui.Field(s.Studio.GalleryUrl, 420, true);
        body.Children.Add(Ui.Card(
            Ui.Lbl("BrainThemes"), gallery,
            Ui.Lbl("Featured", 6), featured,
            s.Studio.CustomThemes.Count > 0 ? Ui.Lbl("Your themes", 6) : (UIElement)new Border(), mine,
            Ui.Lbl("Theme Marketplace", 10),
            Ui.Desc("Paste the https address of a community theme list (a JSON list of theme codes, see the README). Only colours are ever read from it.", 0),
            new Border { Height = 8 },
            Ui.Row(galleryUrl, new Border { Width = 10 }, Ui.AsyncPill("Load", async () =>
            {
                s.Studio.GalleryUrl = galleryUrl.Text.Trim();
                AppState.Save();
                community.Children.Clear();
                var themes = await Themes.FetchGalleryAsync(s.Studio.GalleryUrl);
                if (themes.Count == 0) { community.Children.Add(Ui.Small("No valid themes found at that address.")); return; }
                foreach (var t in themes)
                {
                    var theme = t;
                    community.Children.Add(Ui.Ghost("Add " + theme.Name, () => { s.Studio.CustomThemes.Add(theme); AppState.Save("Theme added from marketplace"); host.Refresh(); }));
                }
            })),
            community));

        // ── Theme editor with live preview ──
        var baseTheme = Themes.Effective(s);
        var edit = new ThemeDef
        {
            Id = "c-" + Guid.NewGuid().ToString("N")[..8], Name = baseTheme.Name + " (mine)", Dark = baseTheme.Dark,
            Bg = baseTheme.Bg, Panel = baseTheme.Panel, Panel2 = baseTheme.Panel2, Ink = baseTheme.Ink, Muted = baseTheme.Muted,
            Dim = baseTheme.Dim, Pill = baseTheme.Pill, PillHover = baseTheme.PillHover, Accent = baseTheme.Accent,
        };
        var editor = new StackPanel();
        var nameBox = Ui.Field(edit.Name, 260);
        var editMsg = Ui.Small("");
        void Preview()
        {
            // live preview: temporarily treat the edited theme as the active one
            var keepTheme = s.Theme;
            var keepAccent = s.Accent;
            var keepMinimal = s.Studio.Minimal;
            s.Studio.CustomThemes.RemoveAll(x => x.Id == "__preview");
            var copy = Clone(edit);
            copy.Id = "__preview";
            s.Studio.CustomThemes.Add(copy);
            s.Theme = "__preview"; s.Accent = null; s.Studio.Minimal = false;
            Themes.Apply(s);
            s.Theme = keepTheme; s.Accent = keepAccent; s.Studio.Minimal = keepMinimal;
            s.Studio.CustomThemes.RemoveAll(x => x.Id == "__preview");
        }
        foreach (var (key, label) in ColorKeys)
        {
            var k = key;
            var prop = typeof(ThemeDef).GetProperty(k)!;
            var sw = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1) };
            sw.SetResourceReference(Border.BorderBrushProperty, "Line2");
            var box = Ui.Field((string)prop.GetValue(edit)!, 110, true);
            void Sync()
            {
                var v = box.Text.Trim();
                if (!v.StartsWith('#')) v = "#" + v;
                if (!Themes.IsHex(v)) return;
                prop.SetValue(edit, v.ToUpperInvariant());
                sw.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(v));
                Preview();
            }
            sw.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString((string)prop.GetValue(edit)!));
            box.TextChanged += (_, _) => Sync();
            var row = Ui.Row(sw, box, Ui.Text("   " + label, 13, "Muted"));
            row.Margin = new Thickness(0, 0, 0, 8);
            editor.Children.Add(row);
        }
        var codeBox = Ui.Field("", 520, true);
        body.Children.Add(Ui.Card(
            Ui.Lbl("Theme editor · Live Theme Preview"),
            Ui.Desc("Change any colour (hex like #FF8A5B) and the whole app updates as you type.", 0),
            new Border { Height = 10 },
            Ui.Row(Ui.Text("Name  ", 13, "Muted"), nameBox, new Border { Width = 16 }, Ui.Toggle(edit.Dark, on => { edit.Dark = on; Preview(); }), Ui.Text("  Dark theme", 13, "Muted")),
            new Border { Height = 12 },
            editor,
            Ui.Buttons(
                Ui.Pill("Save theme", () =>
                {
                    edit.Name = System.Text.RegularExpressions.Regex.Replace(nameBox.Text, @"[^\w \-\.']", "").Trim();
                    if (edit.Name.Length == 0) edit.Name = "My theme";
                    var saved = Clone(edit);
                    saved.Id = "c-" + Guid.NewGuid().ToString("N")[..8];
                    s.Studio.CustomThemes.Add(saved);
                    s.Theme = saved.Id;
                    s.Studio.Minimal = false;
                    Applied("Theme saved: " + saved.Name);
                    host.Refresh();
                }),
                Ui.Ghost("Copy share code", () => { edit.Name = nameBox.Text.Trim(); Ui.Copy(Themes.ToCode(edit)); Ui.Msg(editMsg, "Share code copied. Anyone can paste it into BrainStrap's Studio.", "Ok"); }),
                Ui.Ghost("Undo preview", () => { Themes.Apply(s); host.Refresh(); }),
                s.Studio.CustomThemes.Any(t => t.Id == s.Theme)
                    ? Ui.Ghost("Delete current custom theme", () => { s.Studio.CustomThemes.RemoveAll(t => t.Id == s.Theme); s.Theme = "silk"; Applied("Theme deleted"); host.Refresh(); })
                    : (UIElement)new Border()),
            Ui.Lbl("Import a share code", 12),
            Ui.Row(codeBox, new Border { Width = 10 }, Ui.Pill("Import", () =>
            {
                var t = Themes.FromCode(codeBox.Text);
                if (t == null) { Ui.Msg(editMsg, "That isn't a valid BrainStrap theme code (they start with BST1.).", "Warn"); return; }
                s.Studio.CustomThemes.Add(t);
                s.Theme = t.Id;
                s.Studio.Minimal = false;
                Applied("Theme imported: " + t.Name);
                host.Refresh();
            })),
            Ui.Desc("Theme codes made with the theme builder on the BrainStrap website work here too."),
            editMsg));

        // ── accent, minimal, fonts ──
        var fonts = new DarkCombo { Width = 280, IsEditable = false, Margin = new Thickness(0, 0, 10, 0) };
        fonts.Items.Add("BrainStrap default (Segoe UI)");
        foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x)) fonts.Items.Add(f);
        fonts.SelectedItem = s.Studio.Font ?? "BrainStrap default (Segoe UI)";
        fonts.SelectionChanged += (_, _) =>
        {
            s.Studio.Font = fonts.SelectedIndex <= 0 ? null : fonts.SelectedItem as string;
            Applied("Font " + (s.Studio.Font ?? "default"));
        };
        body.Children.Add(Ui.Card(
            Ui.Lbl("Accent"),
            Ui.Chips(Themes.Accents.Select(a => (a.Name, a.Hex)), s.Accent ?? "", v => { s.Accent = v.Length == 0 ? null : v; Applied("Accent"); }),
            new Border { Height = 10 },
            Ui.ToggleRow("Minimal Mode", "Pure black and white, no descriptions: just the controls.", s.Studio.Minimal, on => { s.Studio.Minimal = on; Applied("Minimal mode " + (on ? "on" : "off")); host.Refresh(); }),
            Ui.Lbl("Custom Fonts"),
            Ui.Row(fonts)));

        // ── backgrounds ──
        var dim = new Slider { Minimum = 0, Maximum = 0.95, Value = s.Studio.BackgroundDim, Width = 260 };
        dim.ValueChanged += (_, _) => { s.Studio.BackgroundDim = dim.Value; host.ApplyBackground(); };
        dim.LostMouseCapture += (_, _) => AppState.Save("Background dim");
        body.Children.Add(Ui.Card(
            Ui.Lbl("Custom Backgrounds"),
            Ui.Chips(new[] { ("None", "none"), ("Animated silk", "silk"), ("Image", "image"), ("Video", "video") }, s.Studio.Background, v =>
            {
                if (v is "image" or "video")
                {
                    var dlg = new OpenFileDialog { Filter = v == "image" ? "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" : "Videos|*.mp4;*.wmv;*.mov" };
                    if (dlg.ShowDialog(host) != true) { host.Refresh(); return; }
                    s.Studio.BackgroundPath = dlg.FileName;
                }
                s.Studio.Background = v;
                Applied("Background " + v);
                host.ApplyBackground();
                host.Refresh();
            }),
            Ui.Row(Ui.Text("Dim  ", 13, "Muted"), dim),
            Ui.Desc("Videos loop silently. Cards turn slightly see-through over a background.")));

        // ── launch animation + sound ──
        var vol = new Slider { Minimum = 0, Maximum = 1, Value = s.Studio.LaunchSoundVolume, Width = 200 };
        vol.LostMouseCapture += (_, _) => { s.Studio.LaunchSoundVolume = vol.Value; AppState.Save("Launch sound volume"); };
        body.Children.Add(Ui.Card(
            Ui.Lbl("Custom Launch Animations"),
            Ui.Chips(new[] { ("Silk", "silk"), ("Pulse", "pulse"), ("Minimal", "minimal"), ("Off", "none") }, s.Studio.Splash, v => { s.Studio.Splash = v; AppState.Save("Launch screen " + v); }),
            Ui.Buttons(
                Ui.AsyncPill("Preview", async () =>
                {
                    if (s.Studio.Splash == "none") return;
                    var sp = new LaunchSplash(s);
                    sp.Show();
                    sp.Status("Applying your setup…");
                    sp.Detail($"{s.ActiveProfile} profile · preview", null);
                    await Task.Delay(2200);
                    await sp.FinishAsync();
                }, ghost: true),
                Ui.Ghost(s.Studio.SplashImage != null ? "Change splash image" : "Add a splash image", () =>
                {
                    var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
                    if (dlg.ShowDialog(host) == true) { s.Studio.SplashImage = dlg.FileName; AppState.Save("Splash image"); host.Refresh(); }
                }),
                s.Studio.SplashImage != null ? Ui.Ghost("Remove image", () => { s.Studio.SplashImage = null; AppState.Save("Splash image removed"); host.Refresh(); }) : (UIElement)new Border()),
            Ui.Lbl("Custom Launch Sounds", 10),
            Ui.Small(s.Studio.LaunchSound != null ? System.IO.Path.GetFileName(s.Studio.LaunchSound) : "No sound", 0),
            Ui.Buttons(
                Ui.Ghost("Choose sound", () =>
                {
                    var dlg = new OpenFileDialog { Filter = "Sounds|*.mp3;*.wav;*.wma;*.m4a" };
                    if (dlg.ShowDialog(host) == true) { s.Studio.LaunchSound = dlg.FileName; AppState.Save("Launch sound"); host.Refresh(); }
                }),
                Ui.Ghost("Test", () => Launcher.PlayLaunchSound(s)),
                s.Studio.LaunchSound != null ? Ui.Ghost("Remove", () => { s.Studio.LaunchSound = null; AppState.Save("Launch sound removed"); host.Refresh(); }) : (UIElement)new Border()),
            Ui.Row(Ui.Text("Volume  ", 13, "Muted"), vol)));

        // ── layout builder ──
        var layout = new StackPanel();
        var order = s.Studio.HomeWidgets;
        for (var i = 0; i < order.Count; i++)
        {
            var idx = i;
            var id = order[i];
            var label = WidgetNames.FirstOrDefault(w => w.id == id).label ?? id;
            var shown = !s.Studio.HiddenWidgets.Contains(id);
            var up = Ui.Ghost("↑", () => { if (idx == 0) return; (order[idx - 1], order[idx]) = (order[idx], order[idx - 1]); AppState.Save("Home layout"); host.Refresh(); });
            var down = Ui.Ghost("↓", () => { if (idx >= order.Count - 1) return; (order[idx + 1], order[idx]) = (order[idx], order[idx + 1]); AppState.Save("Home layout"); host.Refresh(); });
            up.Height = down.Height = 30;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = Ui.Row(Ui.Toggle(shown, on => { if (on) s.Studio.HiddenWidgets.Remove(id); else s.Studio.HiddenWidgets.Add(id); AppState.Save("Home layout"); }),
                Ui.Text($"   {idx + 1}. {label}", 14, "Ink", FontWeights.SemiBold));
            var right = Ui.Row(up, down);
            Grid.SetColumn(right, 1);
            g.Children.Add(left);
            g.Children.Add(right);
            layout.Children.Add(g);
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Layout Builder · Home widgets"), Ui.Desc("Turn widgets on or off and move them up or down.", 0), new Border { Height = 10 }, layout));

        // ── profile themes ──
        var pt = new StackPanel();
        foreach (var name in s.Profiles.Keys.OrderBy(k => k == "Default" ? "" : k))
        {
            var prof = s.Profiles[name];
            var combo = new DarkCombo { Width = 220 };
            combo.Items.Add("Follow the main theme");
            var all = Themes.All(s).ToList();
            foreach (var t in all) combo.Items.Add(t.Name);
            combo.SelectedIndex = prof.Theme == null ? 0 : Math.Max(0, all.FindIndex(t => t.Id == prof.Theme) + 1);
            combo.SelectionChanged += (_, _) =>
            {
                prof.Theme = combo.SelectedIndex <= 0 ? null : all[combo.SelectedIndex - 1].Id;
                Applied("Profile theme for " + name);
            };
            pt.Children.Add(Ui.KV(name, combo, 200));
        }
        body.Children.Add(Ui.Card(Ui.Lbl("Profile Themes"), Ui.Desc("Give each graphics profile its own look, so you can see at a glance which one is active.", 0), new Border { Height = 10 }, pt));
        return page;
    }

    static ThemeDef Clone(ThemeDef t) => new()
    {
        Id = t.Id, Name = t.Name, Dark = t.Dark, Bg = t.Bg, Panel = t.Panel, Panel2 = t.Panel2, Ink = t.Ink,
        Muted = t.Muted, Dim = t.Dim, Pill = t.Pill, PillHover = t.PillHover, Accent = t.Accent,
    };

    static ControlTemplate NoChrome()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }
}
