using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainStrap.Core;
using Microsoft.Win32;

namespace BrainStrap.UI;

public partial class MainWindow : Window
{
    Settings S => AppState.S;
    readonly Catalog Cat = FlagCatalog.Load();
    readonly Dictionary<string, ScrollViewer> pages = new();
    readonly Dictionary<string, Func<MainWindow, FrameworkElement>> dynPages = new()
    {
        ["home"] = Pages.HomePage.Build,
        ["brainai"] = Pages.BrainAiPage.Build,
        ["performance"] = Pages.PerfPage.Build,
        ["servers"] = Pages.ServersPage.Build,
        ["studio"] = Pages.StudioPage.Build,
        ["tools"] = Pages.ToolsPage.Build,
        ["life"] = Pages.LifePage.Build,
        ["flags"] = Pages.FlagsPage.Build,
        ["mods"] = Pages.ModsPage.Build,
        ["extensions"] = Pages.ExtensionsPage.Build,
        ["store"] = Pages.StorePage.Build,
        ["flow"] = Pages.FlowPage.Build,
        ["connect"] = Pages.ConnectPage.Build,
        ["launch"] = Pages.LaunchPage.Build,
    };
    string current = "home";
    int groupSeq;
    bool rendering;

    public MainWindow(string? startPage = null)
    {
        InitializeComponent();
        pages["graphics"] = PageGraphics;
        pages["mods-classic"] = PageMods;
        pages["presence"] = PagePresence;
        pages["about"] = PageAbout;

        BuildNav();
        SourceInitialized += (_, _) => { Dwm.ApplyTitleBar(this, Themes.Effective(S).Dark); Dwm.Round(this); };
        // a maximised window with custom chrome hangs over the screen edge by the resize border; pull the content back in
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Loaded += (_, _) =>
        {
            LoadControls();
            ApplyBackground();
            UpdateStatus();
            _ = CheckUpdateAsync();
            Go(startPage != null && (pages.ContainsKey(startPage) || dynPages.ContainsKey(startPage)) ? startPage : "home");
            JumpLists.Update(S);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.K && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                new CommandPalette(this, Commands()).ShowDialog();
            }
        };
    }

    /// <summary>Every page in sidebar order (used by the screenshot mode).</summary>
    public IEnumerable<string> PageKeys => NavList.Children.OfType<RadioButton>().Select(r => (string)r.Tag);

    static readonly (string group, string key, string label, string icon)[] NavItems =
    {
        ("Play", "home", "Home", "home"), ("Play", "brainai", "BrainAI", "spark"), ("Play", "launch", "Launch", "rocket"),
        ("Pillars", "extensions", "BrainExtensions", "blocks"), ("Pillars", "mods", "BrainMods", "layers"), ("Pillars", "flags", "BrainFlags", "flag"), ("Pillars", "connect", "BrainConnect", "link"),
        ("Platform", "store", "BrainStore", "bag"), ("Platform", "flow", "BrainFlow", "flow"),
        ("Tune", "performance", "Performance", "pulse"), ("Tune", "servers", "Servers", "globe"), ("Tune", "studio", "Studio", "drop"), ("Tune", "tools", "Tools", "sliders"), ("Tune", "life", "BrainLife", "heart"),
        ("App", "presence", "Presence", "user"), ("App", "about", "About", "info"),
    };

    void BuildNav()
    {
        string? group = null;
        foreach (var it in NavItems)
        {
            if (it.group != group)
            {
                group = it.group;
                NavList.Children.Add(new TextBlock { Text = group.ToUpperInvariant(), Style = (Style)FindResource("Lbl"), FontSize = 9.5, Margin = new Thickness(14, NavList.Children.Count == 0 ? 4 : 16, 0, 6) });
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var no = new TextBlock { Text = (Array.IndexOf(NavItems, it) + 1).ToString("00"), Width = 30, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Normal };
            no.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            no.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            row.Children.Add(no);
            row.Children.Add(new TextBlock { Text = it.label, VerticalAlignment = VerticalAlignment.Center });
            var rb = new RadioButton { Style = (Style)FindResource("Nav"), GroupName = "nav", Tag = it.key, Content = row };
            rb.Click += Nav_Click;
            NavList.Children.Add(rb);
        }
    }

    async Task CheckUpdateAsync()
    {
        if (!S.AutoUpdate || Demo.On || (DateTime.UtcNow - S.LastUpdateCheck).TotalHours < 20) return;
        S.LastUpdateCheck = DateTime.UtcNow;
        SettingsStore.Save(S);
        var r = await Updater.CheckAsync(S);
        if (r == null) return;
        if (MessageBox.Show(this, $"BrainStrap {r.Version} is out (you have {Installer.Version}).\n\n{r.Notes}\n\nUpdate now? It takes a few seconds and BrainStrap restarts.", "BrainStrap update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        var problem = await Updater.ApplyAsync(r);
        if (problem == null) Application.Current.Shutdown(0);
        else MessageBox.Show(this, problem, "BrainStrap update");
    }

    void PlaySide_Click(object sender, RoutedEventArgs e) => Pages.HomePage.PlayRoblox();
    void Search_Click(object sender, RoutedEventArgs e) => new CommandPalette(this, Commands()).ShowDialog();
    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>The status card at the bottom of the sidebar.</summary>
    public void UpdateStatus()
    {
        var registered = ProtocolHandler.IsRegisteredToUs();
        var roblox = RobloxInstall.Newest();
        var level = !registered ? "Warn" : roblox == null ? "Warn" : "Ok";
        SideStatus.Text = !registered ? "Not set up yet" : roblox == null ? "Roblox not installed" : "Ready to play";
        SideSub.Text = $"{S.ActiveProfile} profile · v{Installer.Version}";
        SideDot.SetResourceReference(ShapeFill, level);
        SideHalo.SetResourceReference(ShapeFill, level);
        if (SideHalo.RenderTransform is ScaleTransform st && !st.HasAnimatedProperties)
        {
            var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 2.4, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = new System.Windows.Media.Animation.QuadraticEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            SideHalo.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.45, 0, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        }
    }

    /// <summary>Opens a page by key and ticks its sidebar entry.</summary>
    public void Go(string key)
    {
        if (key == "graphics") key = "flags";   // BrainFlags replaced the old Graphics page
        foreach (var child in NavList.Children.OfType<RadioButton>())
            child.IsChecked = (string)child.Tag == key;
        var changed = key != current;
        ShowPage(key);
        if (changed) Arrive();
    }

    /// <summary>Rebuilds the current page (after a change that affects what it shows).</summary>
    public void Refresh()
    {
        // keep the scroll position: a rebuilt page should not jump back to the top
        var at = (DynHost.Content as ScrollViewer)?.VerticalOffset ?? 0;
        ShowPage(current);
        if (at > 0 && DynHost.Content is ScrollViewer sv) sv.ScrollToVerticalOffset(at);
        UpdateStatus();
    }

    /// <summary>The page slides up a little and fades in.</summary>
    void Arrive()
    {
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        // the cards arrive one after another
        if (DynHost.Visibility == Visibility.Visible && DynHost.Content is ScrollViewer { Content: Panel body })
        {
            var i = 0;
            foreach (var child in body.Children.OfType<FrameworkElement>().Take(14))
            {
                var move = new TranslateTransform(0, 22);
                child.RenderTransform = move;
                var begin = TimeSpan.FromMilliseconds(45 * i++);
                move.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = begin, EasingFunction = ease });
                child.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = begin, EasingFunction = ease });
            }
        }
        else if (PageHost.RenderTransform is TranslateTransform tt)
            tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    /// <summary>Studio → Custom Backgrounds: animated silk, an image or a looping video behind the app.</summary>
    public void ApplyBackground()
    {
        BgLayer.Children.Clear();
        var st = S.Studio;
        if (st.Minimal || st.Background == "none") return;
        try
        {
            switch (st.Background)
            {
                case "silk":
                {
                    // the live silk, plus two soft glows in the accent colour that drift very slowly
                    var t = Themes.Effective(S);
                    Color C(string key) => ((SolidColorBrush)FindResource(key)).Color;
                    Color bg = (Color)ColorConverter.ConvertFromString(t.Bg), ink = C("Ink"), accent = C("Accent");
                    var (ga, gb) = Themes.Accents2(S);
                    BgLayer.Children.Add(new Silk(bg, ink, ga, gb, t.Dark, st.SilkStrength));
                    var veil = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    veil.GradientStops.Add(new GradientStop(Color.FromArgb(150, bg.R, bg.G, bg.B), 0));
                    veil.GradientStops.Add(new GradientStop(Color.FromArgb(105, bg.R, bg.G, bg.B), 0.62));
                    veil.GradientStops.Add(new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 1));
                    BgLayer.Children.Add(new Border { Background = veil });
                    break;
                }
                case "image" when File.Exists(st.BackgroundPath):
                {
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit(); img.UriSource = new Uri(st.BackgroundPath!); img.DecodePixelWidth = 1920; img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.EndInit();
                    BgLayer.Children.Add(new Border { Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill } });
                    break;
                }
                case "video" when File.Exists(st.BackgroundPath):
                {
                    var me = new MediaElement { Source = new Uri(st.BackgroundPath!), LoadedBehavior = MediaState.Manual, IsMuted = true, Stretch = Stretch.UniformToFill };
                    me.MediaEnded += (_, _) => { me.Position = TimeSpan.Zero; me.Play(); };
                    BgLayer.Children.Add(me);
                    me.Play();
                    break;
                }
            }
            var shade = new Border { Opacity = st.Background == "silk" ? 0 : st.BackgroundDim };
            shade.SetResourceReference(Border.BackgroundProperty, "Bg");
            BgLayer.Children.Add(shade);
        }
        catch (Exception ex)
        {
            Logger.Warn("background: " + ex.Message);
        }
    }

    List<(string title, string hint, Action run)> Commands()
    {
        var list = new List<(string, string, Action)>
        {
            ("Home", "page", () => Go("home")), ("BrainAI", "page · ask for settings in plain English", () => Go("brainai")),
            ("Launch", "page", () => Go("launch")), ("BrainFlags", "page · allowlist-aware flag editor with AI", () => Go("flags")),
            ("BrainExtensions", "page · built-in and installed extensions, BrainBuilder", () => Go("extensions")), ("BrainStore", "page · extensions, packs, presets, themes", () => Go("store")),
            ("BrainFlow", "page · automations and macros", () => Go("flow")), ("BrainConnect", "page · Discord, OBS, Spotify, webhooks", () => Go("connect")),
            ("Open BrainCompanion", "second-screen live dashboard", () => new Pages.CompanionWindow().Show()),
            ("Performance", "page · benchmark, monitors", () => Go("performance")), ("Servers", "page · server browser, ping", () => Go("servers")),
            ("BrainMods", "page · launcher packs", () => Go("mods")), ("Studio", "page · themes, backgrounds, layout", () => Go("studio")),
            ("Tools", "page · timeline, repair, cache", () => Go("tools")), ("BrainLife", "page · HUD, library, screenshots", () => Go("life")),
            ("Presence", "page · Discord", () => Go("presence")), ("About", "page", () => Go("about")),
            ("BrainBoost", "one-click optimization for this PC", () => { Pages.BrainAiPage.PendingRequest = "smoother while keeping the graphics looking good"; Go("brainai"); }),
            ("Run FPS Doctor", "BrainAI", () => Go("brainai")),
            ("Run a benchmark", "Performance", () => Go("performance")),
            ("Roll back settings", "Tools → Timeline", () => Go("tools")),
            ("Repair Roblox", "Tools → Repair Center", () => Go("tools")),
            ("Clean caches", "Tools → Cache Manager", () => MessageBox.Show(this, CacheManager.CleanAll(), "BrainStrap")),
            ("Connection diagnostics", "Servers", () => Go("servers")),
            ("Toggle safe mode", S.SafeMode ? "currently on" : "currently off", () => { S.SafeMode = !S.SafeMode; AppState.Save("Safe mode " + (S.SafeMode ? "on" : "off")); LoadControls(); }),
            ("Toggle Minimal Mode", "Studio", () => { S.Studio.Minimal = !S.Studio.Minimal; Themes.Apply(S); AppState.Save("Minimal mode"); ApplyBackground(); Refresh(); }),
            ("Open Mods folder", "folder", () => Open(Paths.Mods)),
            ("Open BrainStrap data folder", "folder", () => Open(Paths.Data)),
            ("Open screenshots", "folder", () => Open(Screenshots.Folder)),
            ("Copy diagnostics", "About", () => { Clipboard.SetText(Diagnostics.Build(S)); }),
        };
        foreach (var name in S.Profiles.Keys)
        {
            var n = name;
            list.Add(($"Use profile: {n}", "Graphics profile", () => { S.ActiveProfile = n; Themes.Apply(S); AppState.Save("Profile " + n); Refresh(); }));
        }
        foreach (var t in Themes.All(S))
        {
            var theme = t;
            list.Add(($"Theme: {theme.Name}", "Studio", () => { S.Theme = theme.Id; S.Studio.Minimal = false; Themes.Apply(S); Dwm.ApplyTitleBar(this, theme.Dark); AppState.Save("Theme " + theme.Name); Refresh(); }));
        }
        return list;
    }

    // ───────────────────────── helpers ─────────────────────────

    void Save() => AppState.Save();

    static TextBlock Text(string text, double size, FontWeight weight, string? brushKey = null)
    {
        var tb = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        if (brushKey != null) tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return tb;
    }

    TextBlock Small(string text, Thickness? margin = null)
    {
        var tb = new TextBlock { Text = text, Style = (Style)FindResource("Small") };
        if (margin.HasValue) tb.Margin = margin.Value;
        return tb;
    }

    RadioButton Chip(object content, string group, bool on, RoutedEventHandler click)
    {
        var rb = new RadioButton { Content = content, GroupName = group, Style = (Style)FindResource("Chip"), IsChecked = on };
        rb.Click += click;
        return rb;
    }

    Button Ghost(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("GhostButton") };
        b.Click += click;
        return b;
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Warn("could not open " + target + ": " + ex.Message); }
    }

    void SetMsg(TextBlock tb, string text, string brushKey = "Muted")
    {
        tb.Text = text;
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    // ───────────────────────── navigation ─────────────────────────

    void Nav_Click(object sender, RoutedEventArgs e) => ShowPage((string)((RadioButton)sender).Tag);

    void ShowPage(string key)
    {
        current = key;
        var at = Array.FindIndex(NavItems, n => n.key == key);
        Crumb.Text = (at >= 0 ? NavItems[at].label : key).ToUpperInvariant() + (at >= 0 ? $"      {at + 1:00} / {NavItems.Length:00}" : "");
        foreach (var kv in pages) kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
        if (dynPages.TryGetValue(key, out var build))
        {
            try { DynHost.Content = build(this); }
            catch (Exception ex)
            {
                Logger.Error("page " + key + " failed", ex);
                DynHost.Content = new TextBlock { Text = "This page hit a problem: " + ex.Message, Margin = new Thickness(44), TextWrapping = TextWrapping.Wrap };
            }
            DynHost.Visibility = Visibility.Visible;
            return;
        }
        DynHost.Content = null;
        DynHost.Visibility = Visibility.Collapsed;
        switch (key)
        {
            case "graphics": RenderGraphics(); break;
            case "mods-classic": RenderMods(); break;
        }
    }

    void LoadControls()
    {
        TglClose.IsChecked = S.CloseOnLaunch;
        TglSafe.IsChecked = S.SafeMode;
        TglUpdates.IsChecked = S.CheckForUpdates;
        TglKeep.IsChecked = S.KeepRegistered;
        TglStringFormat.IsChecked = S.FlagFormat == "string";
        TglMods.IsChecked = S.ModsEnabled;
        TglDiscord.IsChecked = S.Discord.Enabled;
        TglGameName.IsChecked = S.Discord.ShowGameName;
        TxtClientId.Text = S.Discord.ClientId;
        TxtImageKey.Text = S.Discord.LargeImageKey;
        TxtModsPath.Text = Paths.Mods;
        TxtVersion.Text = $"BrainStrap {Installer.Version}  ·  a launcher for the Roblox Windows player";
        TxtCatalogNote.Text = Cat.Note;
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        var on = cb.IsChecked == true;
        switch ((string)cb.Tag)
        {
            case "close": S.CloseOnLaunch = on; break;
            case "safe": S.SafeMode = on; break;
            case "updates": S.CheckForUpdates = on; break;
            case "keep": S.KeepRegistered = on; break;
            case "stringformat": S.FlagFormat = on ? "string" : "native"; break;
            case "mods": S.ModsEnabled = on; break;
            case "discord": S.Discord.Enabled = on; break;
            case "gamename": S.Discord.ShowGameName = on; break;
        }
        Save();
    }

    // ───────────────────────── 01 launch ─────────────────────────

    async Task RefreshLaunchAsync()
    {
        var registered = ProtocolHandler.IsRegisteredToUs();
        TxtHandler.Text = registered ? "BrainStrap opens when you press Play" : "Roblox's own launcher opens when you press Play";
        TxtHandler.SetResourceReference(TextBlock.ForegroundProperty, registered ? "Ok" : "Warn");
        BtnSetup.Content = registered && Installer.IsInstalled ? "Re-register BrainStrap" : "Set up BrainStrap";
        BtnRestore.IsEnabled = registered;
        SideStatus.Text = registered ? "Active" : "Not set up";
        SideDot.SetResourceReference(ShapeFill, registered ? "Ok" : "Warn");

        var installed = RobloxInstall.Newest();
        if (installed == null)
        {
            TxtInstalled.Text = "Not found. Install Roblox from roblox.com first.";
            TxtInstalled.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
        }
        else
        {
            TxtInstalled.Text = $"{installed.Name}   ·   {installed.Stamp.ToLocalTime():d MMM yyyy}";
            TxtInstalled.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        }

        TxtLatest.Text = "Checking…";
        TxtLatest.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var latest = await RobloxInstall.LatestVersionAsync();
        if (latest == null)
        {
            TxtLatest.Text = "Could not reach Roblox";
        }
        else if (installed != null && installed.Name.Equals(latest, StringComparison.OrdinalIgnoreCase))
        {
            TxtLatest.Text = $"{latest}   ·   up to date";
            TxtLatest.SetResourceReference(TextBlock.ForegroundProperty, "Ok");
        }
        else
        {
            TxtLatest.Text = $"{latest}   ·   Roblox will update on the next Play";
            TxtLatest.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
        }
    }

    // the Ellipse.Fill dependency property, without importing System.Windows.Shapes (which would clash with System.IO.Path)
    static readonly DependencyProperty ShapeFill = System.Windows.Shapes.Shape.FillProperty;

    async void BtnSetup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Installer.Install(S);
            SetMsg(TxtSetupMsg, "Done. The Play button on roblox.com now opens BrainStrap, which then starts Roblox.", "Ok");
        }
        catch (Exception ex)
        {
            Logger.Error("setup failed", ex);
            SetMsg(TxtSetupMsg, "Could not finish setup: " + ex.Message, "Bad");
        }
        await RefreshLaunchAsync();
    }

    async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProtocolHandler.Restore(S);
            Save();
            SetMsg(TxtSetupMsg, "Roblox's own launcher is back in charge of the Play button.", "Ok");
        }
        catch (Exception ex)
        {
            Logger.Error("restore failed", ex);
            SetMsg(TxtSetupMsg, "Could not restore: " + ex.Message, "Bad");
        }
        await RefreshLaunchAsync();
    }

    void BtnPrune_Click(object sender, RoutedEventArgs e)
    {
        var all = RobloxInstall.List();
        if (all.Count < 2)
        {
            SetMsg(TxtPruneMsg, "Only one Roblox version is installed. Nothing to remove.");
            return;
        }
        var ask = MessageBox.Show(this, $"Delete {all.Count - 1} old Roblox version folder(s) and keep {all[0].Name}?", "BrainStrap",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;
        var (removed, problem) = RobloxInstall.PruneOld();
        SetMsg(TxtPruneMsg, problem ?? $"Removed {removed} old version(s).", problem == null ? "Ok" : "Warn");
    }

    void BtnGetRoblox_Click(object sender, RoutedEventArgs e) => Open("https://www.roblox.com/download");

    // ───────────────────────── 02 graphics ─────────────────────────

    void SetVal(FlagProfile p, string id, string? value)
    {
        if (value == null) p.Values.Remove(id); else p.Values[id] = value;
        Save();
    }

    void RenderGraphics()
    {
        if (rendering) return;
        rendering = true;
        try
        {
            var prof = S.Current;

            // profiles
            ProfileChips.Children.Clear();
            var pg = "profiles" + (++groupSeq);
            foreach (var name in S.Profiles.Keys.OrderBy(k => k == "Default" ? "" : k, StringComparer.OrdinalIgnoreCase))
            {
                var n = name;
                ProfileChips.Children.Add(Chip(n, pg, n == S.ActiveProfile, (_, _) =>
                {
                    S.ActiveProfile = n;
                    Save();
                    Themes.Apply(S);
                    RenderGraphics();
                }));
            }
            BtnDelProfile.IsEnabled = S.ActiveProfile != "Default";

            // presets
            PresetButtons.Children.Clear();
            TxtPresetDesc.Text = "A preset replaces this profile's choices. You can still change anything afterwards.";
            foreach (var p in Cat.Presets)
            {
                var preset = p;
                var b = Ghost(preset.Name, (_, _) =>
                {
                    prof.Values = new Dictionary<string, string>(preset.Values);
                    Save();
                    RenderGraphics();
                });
                b.MouseEnter += (_, _) => TxtPresetDesc.Text = preset.Desc;
                PresetButtons.Children.Add(b);
            }
            PresetButtons.Children.Add(Ghost("Roblox defaults", (_, _) =>
            {
                prof.Values.Clear();
                Save();
                RenderGraphics();
            }));

            // every flag in the catalog
            FlagList.Children.Clear();
            string? lastGroup = null;
            foreach (var def in Cat.Flags)
            {
                if (def.Group != lastGroup)
                {
                    lastGroup = def.Group;
                    var h = new TextBlock { Text = def.Group.ToUpperInvariant(), Style = (Style)FindResource("Lbl"), Margin = new Thickness(0, FlagList.Children.Count == 0 ? 0 : 8, 0, 14) };
                    FlagList.Children.Add(h);
                }
                FlagList.Children.Add(BuildRow(def, prof));
            }

            TxtCustom.Text = string.Join(Environment.NewLine, prof.Custom.Select(kv => kv.Key + "=" + kv.Value));
            SetMsg(TxtCustomMsg, "");
            RenderGameRows();
        }
        finally
        {
            rendering = false;
        }
    }

    FrameworkElement BuildRow(FlagDef d, FlagProfile prof)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        var current = prof.Values.TryGetValue(d.Id, out var cv) ? cv : null;

        if (d.Type == "bool")
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
            left.Children.Add(Text(d.Name, 14, FontWeights.SemiBold));
            left.Children.Add(Small(d.Desc, new Thickness(0, 2, 0, 0)));

            var toggle = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = current == "true" };
            toggle.Click += (_, _) => SetVal(prof, d.Id, toggle.IsChecked == true ? "true" : null);
            Grid.SetColumn(toggle, 1);

            grid.Children.Add(left);
            grid.Children.Add(toggle);
            panel.Children.Add(grid);
            return panel;
        }

        panel.Children.Add(Text(d.Name, 14, FontWeights.SemiBold));
        panel.Children.Add(Small(d.Desc, new Thickness(0, 2, 0, 10)));

        var chips = new WrapPanel();
        var group = "f" + (++groupSeq);
        chips.Children.Add(Chip("Default", group, current == null, (_, _) =>
        {
            SetVal(prof, d.Id, null);
            if (d.Type == "int") RenderGraphics();
        }));

        if (d.Type == "choice")
        {
            foreach (var o in d.Options)
            {
                var opt = o;
                chips.Children.Add(Chip(opt.Label, group, current == opt.Value, (_, _) => SetVal(prof, d.Id, opt.Value)));
            }
            panel.Children.Add(chips);
            return panel;
        }

        // whole numbers: quick values plus an exact box
        foreach (var c in d.Chips)
        {
            var text = c.ToString();
            chips.Children.Add(Chip(text, group, current == text, (_, _) =>
            {
                SetVal(prof, d.Id, text);
                RenderGraphics();
            }));
        }
        panel.Children.Add(chips);

        var exact = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        exact.Children.Add(new TextBlock { Text = $"EXACT ({d.Min}–{d.Max})", Style = (Style)FindResource("Lbl"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var box = new TextBox { Style = (Style)FindResource("Field"), Width = 90, Text = current ?? "", FontFamily = (FontFamily)FindResource("MonoFont") };
        void Commit()
        {
            if (rendering) return;
            var raw = box.Text.Trim();
            if (raw.Length == 0) { if (current != null) { SetVal(prof, d.Id, null); RenderGraphics(); } return; }
            if (!int.TryParse(raw, out var n)) { box.Text = current ?? ""; return; }
            var clamped = Math.Clamp(n, d.Min, d.Max).ToString();
            if (clamped == current) { box.Text = clamped; return; }
            SetVal(prof, d.Id, clamped);
            RenderGraphics();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, a) => { if (a.Key == System.Windows.Input.Key.Enter) Commit(); };
        exact.Children.Add(box);
        panel.Children.Add(exact);
        return panel;
    }

    void BtnNewProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = Prompt.Ask(this, "New profile", "Name for the new profile");
        if (name == null) return;
        name = Regex.Replace(name, @"[^\w \-\.]", "").Trim();
        if (name.Length == 0) return;
        if (S.Profiles.ContainsKey(name)) { MessageBox.Show(this, "A profile with that name already exists.", "BrainStrap"); return; }
        // start from a copy of the current one
        var source = S.Current;
        S.Profiles[name] = new FlagProfile
        {
            Values = new Dictionary<string, string>(source.Values),
            Custom = new Dictionary<string, string>(source.Custom),
        };
        S.ActiveProfile = name;
        Save();
        RenderGraphics();
    }

    void BtnDelProfile_Click(object sender, RoutedEventArgs e)
    {
        if (S.ActiveProfile == "Default") return;
        var ask = MessageBox.Show(this, $"Delete the profile \"{S.ActiveProfile}\"?", "BrainStrap", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;
        var gone = S.ActiveProfile;
        S.Profiles.Remove(gone);
        foreach (var k in S.GameProfiles.Where(kv => kv.Value == gone).Select(kv => kv.Key).ToList()) S.GameProfiles.Remove(k);
        S.ActiveProfile = "Default";
        Save();
        RenderGraphics();
    }

    void BtnSaveCustom_Click(object sender, RoutedEventArgs e)
    {
        var next = new Dictionary<string, string>();
        var problems = new List<string>();
        var n = 0;
        foreach (var raw in TxtCustom.Text.Split('\n'))
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) { problems.Add($"line {n}: needs NAME=value"); continue; }
            var name = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim().Trim('"');
            var verdict = FlagValidator.Check(name, val);
            if (verdict.Level == "bad") { problems.Add($"line {n}: {name}: {verdict.Message}"); continue; }
            if (val.Length == 0 || val.Length > 200) { problems.Add($"line {n}: empty or too long value"); continue; }
            next[name] = val;
        }
        S.Current.Custom = next;
        Save();
        SetMsg(TxtCustomMsg,
            $"Saved {next.Count} custom flag(s)." + (problems.Count > 0 ? "  Skipped: " + string.Join("; ", problems) : ""),
            problems.Count > 0 ? "Warn" : "Ok");
    }

    void RenderGameRows()
    {
        GameRows.Children.Clear();
        foreach (var kv in S.GameProfiles.OrderBy(k => k.Key))
        {
            var place = kv.Key;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = $"Place {place}   →   {kv.Value}", FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var remove = Ghost("Remove", (_, _) => { S.GameProfiles.Remove(place); Save(); RenderGameRows(); });
            Grid.SetColumn(remove, 1);
            row.Children.Add(label);
            row.Children.Add(remove);
            GameRows.Children.Add(row);
        }
    }

    void BtnAssign_Click(object sender, RoutedEventArgs e)
    {
        var id = TxtPlaceId.Text.Trim();
        if (id.Length < 3 || id.Length > 20 || !id.All(char.IsAsciiDigit))
        {
            MessageBox.Show(this, "A place ID is just digits, like 920587237.", "BrainStrap");
            return;
        }
        S.GameProfiles[id] = S.ActiveProfile;
        Save();
        TxtPlaceId.Text = "";
        RenderGameRows();
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    void BtnExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "BrainStrap profile (*.json)|*.json", FileName = S.ActiveProfile + ".brainstrap-profile.json" };
        if (dlg.ShowDialog(this) != true) return;
        var data = new { format = 1, name = S.ActiveProfile, values = S.Current.Values, custom = S.Current.Custom };
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(data, Pretty));
    }

    /// <summary>Returns a safe value for a flag choice, or null when the value does not belong to that flag.</summary>
    static string? CleanValue(FlagDef d, string v)
    {
        switch (d.Type)
        {
            case "bool": return v == "true" ? "true" : null;
            case "int": return int.TryParse(v, out var n) ? Math.Clamp(n, d.Min, d.Max).ToString() : null;
            case "choice": return d.Options.Any(o => o.Value == v) ? v : null;
            default: return null;
        }
    }

    void BtnImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "BrainStrap profile (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > 256 * 1024) throw new InvalidDataException("That file is too large to be a profile.");
            using var doc = JsonDocument.Parse(File.ReadAllText(dlg.FileName));
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? "Imported"
                : Path.GetFileNameWithoutExtension(dlg.FileName);
            name = Regex.Replace(name, @"[^\w \-\.]", "").Trim();
            if (name.Length == 0) name = "Imported";
            if (name.Length > 32) name = name[..32];
            var unique = name;
            for (var i = 2; S.Profiles.ContainsKey(unique); i++) unique = $"{name} {i}";

            var prof = new FlagProfile();
            if (root.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
                foreach (var p in vals.EnumerateObject())
                {
                    var def = Cat.Flags.FirstOrDefault(f => f.Id == p.Name);
                    var clean = def == null || p.Value.ValueKind != JsonValueKind.String ? null : CleanValue(def, p.Value.GetString() ?? "");
                    if (clean != null) prof.Values[p.Name] = clean;
                }
            if (root.TryGetProperty("custom", out var cus) && cus.ValueKind == JsonValueKind.Object)
                foreach (var p in cus.EnumerateObject())
                {
                    var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "";
                    if (FlagCatalog.IsValidFlagName(p.Name) && v.Length is > 0 and <= 200) prof.Custom[p.Name] = v;
                }

            S.Profiles[unique] = prof;
            S.ActiveProfile = unique;
            Save();
            RenderGraphics();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not import that file: " + ex.Message, "BrainStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void BtnExportJson_Click(object sender, RoutedEventArgs e)
    {
        var flags = FlagCatalog.Resolve(Cat, S.Current, S.FlagFormat);
        var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "ClientAppSettings.json" };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, FlagWriter.Render(flags));
    }

    // ───────────────────────── 03 mods ─────────────────────────

    void RenderMods()
    {
        TxtModsPath.Text = Paths.Mods;
        ModRows.Children.Clear();
        var files = ModManager.Scan();
        if (files.Count == 0)
        {
            ModRows.Children.Add(Small("Nothing here yet. Open the Mods folder and add files."));
            return;
        }
        foreach (var f in files.Take(300))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = f.Rel, FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = f.Rel };
            var state = new TextBlock { Text = f.Allowed ? "ready" : "skipped: " + f.Reason, FontSize = 12, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            state.SetResourceReference(TextBlock.ForegroundProperty, f.Allowed ? "Ok" : "Warn");
            Grid.SetColumn(state, 1);
            row.Children.Add(name);
            row.Children.Add(state);
            ModRows.Children.Add(row);
        }
        if (files.Count > 300) ModRows.Children.Add(Small($"…and {files.Count - 300} more."));
    }

    void BtnOpenMods_Click(object sender, RoutedEventArgs e) => Open(Paths.Mods);
    void BtnRefreshMods_Click(object sender, RoutedEventArgs e) => RenderMods();

    void BtnApplyMods_Click(object sender, RoutedEventArgs e)
    {
        var v = RobloxInstall.Newest();
        if (v == null) { SetMsg(TxtModsMsg, "Roblox is not installed.", "Warn"); return; }
        if (RobloxInstall.IsRobloxRunning()) { SetMsg(TxtModsMsg, "Close Roblox first.", "Warn"); return; }
        try
        {
            var (applied, reverted, skipped) = ModManager.Reconcile(v.Folder, S.ModsEnabled);
            SetMsg(TxtModsMsg, $"{applied} file(s) applied, {reverted} put back, {skipped} skipped.", "Ok");
        }
        catch (Exception ex)
        {
            Logger.Error("apply mods failed", ex);
            SetMsg(TxtModsMsg, "Could not apply mods: " + ex.Message, "Bad");
        }
    }

    void BtnRevertMods_Click(object sender, RoutedEventArgs e)
    {
        var v = RobloxInstall.Newest();
        if (v == null) { SetMsg(TxtModsMsg, "Roblox is not installed.", "Warn"); return; }
        if (RobloxInstall.IsRobloxRunning()) { SetMsg(TxtModsMsg, "Close Roblox first.", "Warn"); return; }
        try
        {
            ModManager.RevertAll(v.Folder);
            SetMsg(TxtModsMsg, "Originals are back. Mods will be applied again on the next launch unless you switch them off above.", "Ok");
        }
        catch (Exception ex)
        {
            Logger.Error("revert mods failed", ex);
            SetMsg(TxtModsMsg, "Could not revert: " + ex.Message, "Bad");
        }
    }

    // ───────────────────────── 04 presence ─────────────────────────

    void Presence_LostFocus(object sender, RoutedEventArgs e)
    {
        S.Discord.ClientId = Regex.Replace(TxtClientId.Text, @"\D", "");
        S.Discord.LargeImageKey = Regex.Replace(TxtImageKey.Text, @"[^A-Za-z0-9_\-\.]", "");
        TxtClientId.Text = S.Discord.ClientId;
        TxtImageKey.Text = S.Discord.LargeImageKey;
        Save();
    }

    void BtnDiscordPortal_Click(object sender, RoutedEventArgs e) => Open("https://discord.com/developers/applications");

    // ───────────────────────── 06 about ─────────────────────────

    void BtnDiag_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Diagnostics.Build(S));
            SetMsg(TxtAboutMsg, "Copied. It contains no account name, login ticket, cookie or launch link.", "Ok");
        }
        catch (Exception ex)
        {
            SetMsg(TxtAboutMsg, "Could not copy: " + ex.Message, "Bad");
        }
    }

    void BtnOpenData_Click(object sender, RoutedEventArgs e) => Open(Paths.Data);
    void BtnOpenLogs_Click(object sender, RoutedEventArgs e) => Open(Paths.Logs);

    async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(this, "Put Roblox's own launcher back and remove BrainStrap's changes from the Roblox folder?", "BrainStrap",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;
        try
        {
            Installer.Uninstall(S);
            SetMsg(TxtAboutMsg, "Done. Roblox's launcher is back. You can delete %LOCALAPPDATA%\\BrainStrap whenever you like.", "Ok");
        }
        catch (Exception ex)
        {
            Logger.Error("uninstall failed", ex);
            SetMsg(TxtAboutMsg, "Could not finish: " + ex.Message, "Bad");
        }
        await RefreshLaunchAsync();
    }
}
