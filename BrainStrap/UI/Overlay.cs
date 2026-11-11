using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BrainStrap.Core;
using BrainStrap.Core.Perf;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BrainStrap.UI;

/// <summary>
/// BrainOverlay: YouTube, Spotify, Discord, Twitch (and any site you add) on top of Roblox, one hotkey away.
/// Everything here is BrainStrap's own windows floating above the game, the way the Steam and Discord overlays
/// look. Nothing is put inside Roblox. Sign-ins happen on each service's own page, inside the panel, and are
/// kept in BrainStrap's own browser profile on this PC; BrainStrap never sees a password.
/// </summary>
internal sealed class OverlayHost : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    const int HotkeyId = 0xB6;

    readonly Settings s;
    readonly System.Diagnostics.Process? roblox;
    readonly HwndSource? keys;
    OverlayBackdrop? backdrop;
    readonly Dictionary<string, OverlayPanel> panels = new();
    public bool IsOpen { get; private set; }
    public bool HotkeyWorks { get; }

    public OverlayHost(Settings settings, System.Diagnostics.Process? game)
    {
        s = settings;
        roblox = game;
        // a hidden message window receives the hotkey
        keys = new HwndSource(new HwndSourceParameters("BrainOverlayKeys") { Width = 0, Height = 0, WindowStyle = 0 });
        keys.AddHook(Hook);
        var (mods, vk) = Parse(s.Overlay.Hotkey);
        HotkeyWorks = RegisterHotKey(keys.Handle, HotkeyId, mods | 0x4000, vk);
        if (!HotkeyWorks) Logger.Warn("overlay hotkey " + s.Overlay.Hotkey + " is taken by another program");
        // panels that were pinned last time come back pinned
        foreach (var app in Apps(s).Where(a => a.Pinned && a.Enabled)) Panel(app).ShowPinned();
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x0312 && wp.ToInt32() == HotkeyId) { Toggle(); handled = true; }
        return IntPtr.Zero;
    }

    public static readonly string[] Hotkeys = { "Ctrl+Shift+B", "Shift+Tab", "Alt+Z", "Ctrl+Shift+Space", "F9" };

    public static (uint mods, uint vk) Parse(string combo)
    {
        uint mods = 0;
        var key = Key.B;
        foreach (var part in combo.Split('+'))
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "ctrl": mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "alt": mods |= 0x1; break;
                default:
                    if (Enum.TryParse<Key>(part.Trim(), true, out var k)) key = k;
                    break;
            }
        }
        return (mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    /// <summary>The apps the overlay offers: the built-in four, then anything you added.</summary>
    public static List<OverlayApp> Apps(Settings s)
    {
        foreach (var b in OverlayApp.Builtins)
            if (!s.Overlay.Apps.Any(a => a.Id == b.Id)) s.Overlay.Apps.Insert(Math.Min(s.Overlay.Apps.Count, Array.IndexOf(OverlayApp.Builtins, b)), b.Copy());
        return s.Overlay.Apps;
    }

    OverlayPanel Panel(OverlayApp app)
    {
        if (!panels.TryGetValue(app.Id, out var p) || p.IsShut)
        {
            p = new OverlayPanel(app, this);
            panels[app.Id] = p;
        }
        return p;
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        backdrop ??= new OverlayBackdrop(s, this);
        backdrop.Reveal();
        foreach (var p in panels.Values.Where(p => p.App.Open)) p.Reveal();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        foreach (var p in panels.Values) p.Retire();
        backdrop?.Retire();
        AppState.Save();
        // straight back into the game
        try { if (roblox is { HasExited: false }) SetForegroundWindow(roblox.MainWindowHandle); } catch { }
    }

    public void Launch(OverlayApp app)
    {
        app.Open = true;
        Panel(app).Reveal();
    }

    public void Dispose()
    {
        try { if (keys != null) { UnregisterHotKey(keys.Handle, HotkeyId); keys.Dispose(); } } catch { }
        foreach (var p in panels.Values) p.Shut();
        backdrop?.Close();
        AppState.Save();
    }

    // one browser profile for every panel, so you stay signed in between sessions
    static Task<CoreWebView2Environment>? env;
    public static Task<CoreWebView2Environment> Env() =>
        env ??= CoreWebView2Environment.CreateAsync(null, Directory.CreateDirectory(Path.Combine(Paths.Data, "OverlayProfile")).FullName,
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
}

/// <summary>The dimmed full-screen layer with the clock, the app dock and live stats.</summary>
internal sealed class OverlayBackdrop : Window
{
    readonly Settings s;
    readonly OverlayHost host;
    readonly TextBlock clock, date, stats;
    readonly StackPanel dockRow;
    readonly DispatcherTimer tick;
    readonly Grid stage;

    public OverlayBackdrop(Settings settings, OverlayHost owner)
    {
        s = settings;
        host = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        SetResourceReference(ForegroundProperty, "Ink");
        Width = SystemParameters.PrimaryScreenWidth; Height = SystemParameters.PrimaryScreenHeight;
        Left = 0; Top = 0;

        var root = new Grid();
        // the dim: darker at the edges, like a film frame
        var dim = new Border();
        var vg = new RadialGradientBrush { RadiusX = 0.8, RadiusY = 0.8 };
        vg.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0));
        vg.GradientStops.Add(new GradientStop(Color.FromArgb(225, 0, 0, 0), 1));
        dim.Background = vg;
        dim.MouseLeftButtonDown += (_, _) => host.Close();
        root.Children.Add(dim);
        root.Children.Add(new Border { Height = 64, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Black, IsHitTestVisible = false });
        root.Children.Add(new Border { Height = 64, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Black, IsHitTestVisible = false });

        stage = new Grid { Margin = new Thickness(90, 96, 90, 96) };
        root.Children.Add(stage);
        // clock, top left
        var left = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        date = Ui.Text("", 12, "Muted", mono: true);
        left.Children.Add(date);
        clock = new TextBlock { FontSize = 120, LineHeight = 116, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(-6, 4, 0, 0) };
        clock.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        left.Children.Add(clock);
        stats = Ui.Text("", 13, "Muted", mono: true);
        stats.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(stats);
        stage.Children.Add(left);
        // the hint, top right
        var hint = Ui.Text($"{s.Overlay.Hotkey.ToUpperInvariant()}  OR  CLICK ANYWHERE  ·  BACK TO THE GAME", 11, "Dim", mono: true);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.VerticalAlignment = VerticalAlignment.Top;
        hint.Margin = new Thickness(0, 4, 0, 0);
        stage.Children.Add(hint);
        // the dock, bottom
        var dockWrap = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        dockWrap.Children.Add(Ui.Lbl("BrainOverlay"));
        dockRow = new StackPanel { Orientation = Orientation.Horizontal };
        dockWrap.Children.Add(dockRow);
        stage.Children.Add(dockWrap);
        Fill();

        Content = root;
        tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Clock(), Dispatcher);
        Clock();
        Closed += (_, _) => tick.Stop();
    }

    void Fill()
    {
        dockRow.Children.Clear();
        foreach (var app in OverlayHost.Apps(s).Where(a => a.Enabled))
        {
            var a = app;
            var sp = new StackPanel { Width = 150 };
            var glyph = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
            glyph.SetResourceReference(Border.BorderBrushProperty, "Line2");
            glyph.Child = Icons.Make(a.Icon, 26, 1.6);
            sp.Children.Add(glyph);
            var name = new TextBlock { Text = a.Name, FontSize = 30, Margin = new Thickness(-1, 0, 0, 0) };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            sp.Children.Add(name);
            sp.Children.Add(Ui.Text(a.Pinned ? "PINNED" : a.Open ? "OPEN" : a.Caption, 10, "Dim", mono: true));
            var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("ReadoutButton"), Margin = new Thickness(0, 0, 34, 0) };
            b.Click += (_, _) => { host.Launch(a); Fill(); };
            dockRow.Children.Add(b);
        }
    }

    void Clock()
    {
        clock.Text = DateTime.Now.ToString("HH:mm");
        date.Text = DateTime.Now.ToString("dddd d MMMM").ToUpperInvariant();
        var live = History.ReadLive();
        stats.Text = live == null ? "NOT IN A GAME" :
            $"{(live.GameName ?? "ROBLOX").ToUpperInvariant()}   ·   {(live.Measuring && live.Fps > 0 ? $"{live.Fps:0} FPS" : "— FPS")}   ·   {(live.Ping is { Samples: > 0 } p ? $"{p.Avg:0} MS" : "— MS")}   ·   {(DateTime.UtcNow - live.Started).TotalMinutes:0} MIN";
    }

    public void Reveal()
    {
        Fill();
        Clock();
        tick.Start();
        Opacity = 0;
        Show();
        Activate();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        // the stage drifts in from slightly larger, like a camera settling
        var sc = new ScaleTransform(1.04, 1.04);
        stage.RenderTransformOrigin = new Point(0.5, 0.5);
        stage.RenderTransform = sc;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
    }

    public void Retire()
    {
        tick.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) host.Close();
        base.OnKeyDown(e);
    }
}

/// <summary>One app in its own movable, resizable window. It remembers where you put it.</summary>
internal sealed class OverlayPanel : Window
{
    public OverlayApp App { get; }
    readonly OverlayHost host;
    WebView2? web;
    public bool IsShut { get; private set; }
    readonly TextBlock pin;

    public OverlayPanel(OverlayApp app, OverlayHost owner)
    {
        App = app;
        host = owner;
        Title = app.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Topmost = true;
        MinWidth = 320; MinHeight = 220;
        Width = app.W > 0 ? app.W : 760; Height = app.H > 0 ? app.H : 470;
        var area = SystemParameters.WorkArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = app.X > 0 && app.X < area.Right - 80 ? app.X : area.Right - Width - 60;
        Top = app.Y > 0 && app.Y < area.Bottom - 80 ? app.Y : 120;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        SetResourceReference(BackgroundProperty, "Panel2");
        SetResourceReference(ForegroundProperty, "Ink");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 38, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

        var root = new DockPanel();
        var bar = new DockPanel { Height = 38, LastChildFill = true };
        bar.SetResourceReference(Panel.BackgroundProperty, "Bg");
        Button Cap(string text, string tip, Action click)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("CapButton"), Content = Ui.Text(text, 11.5, "Muted", mono: true), ToolTip = tip, Height = 30, Width = 40 };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += (_, _) => click();
            return b;
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };
        pin = Ui.Text(app.Pinned ? "PINNED" : "PIN", 11.5, app.Pinned ? "Ink" : "Muted", mono: true);
        var pinBtn = new Button { Style = (Style)Application.Current.FindResource("CapButton"), Content = pin, Width = 64, Height = 30, ToolTip = "Keep this on screen while you play" };
        WindowChrome.SetIsHitTestVisibleInChrome(pinBtn, true);
        pinBtn.Click += (_, _) =>
        {
            App.Pinned = !App.Pinned;
            pin.Text = App.Pinned ? "PINNED" : "PIN";
            pin.SetResourceReference(TextBlock.ForegroundProperty, App.Pinned ? "Ink" : "Muted");
            AppState.Save();
        };
        right.Children.Add(Cap("↻", "Reload", () => web?.Reload()));
        right.Children.Add(Cap("↗", "Open in your browser", () => { if (web?.Source != null) Ui.Open(web.Source.ToString()); }));
        right.Children.Add(pinBtn);
        right.Children.Add(Cap("✕", "Close", () => { App.Open = false; App.Pinned = false; Shut(); }));
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        var name = new TextBlock { Text = app.Name, FontSize = 19, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 2) };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        bar.Children.Add(name);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        var holder = new Grid();
        var wait = Ui.Text("Loading " + app.Name + "…", 13, "Muted");
        wait.HorizontalAlignment = HorizontalAlignment.Center;
        wait.VerticalAlignment = VerticalAlignment.Center;
        holder.Children.Add(wait);
        root.Children.Add(holder);
        Content = root;

        SourceInitialized += (_, _) => Dwm.Round(this);
        Loaded += async (_, _) =>
        {
            try
            {
                web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 16, 17, 20) };
                holder.Children.Add(web);
                await web.EnsureCoreWebView2Async(await OverlayHost.Env());
                var core = web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.IsStatusBarEnabled = false;
                // links that want a new window open in the same panel (https only)
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) core.Navigate(e.Uri);
                };
                core.NavigationStarting += (_, e) =>
                {
                    if (!e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
                };
                core.Navigate(app.Url);
                wait.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Warn("overlay panel: " + ex.Message);
                wait.Text = "This needs Microsoft's WebView2 runtime, which Roblox installs. Start Roblox once through BrainStrap and try again.";
                wait.TextWrapping = TextWrapping.Wrap;
                wait.MaxWidth = 420;
            }
        };
        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        Closing += (_, e) => { if (!IsShut) { e.Cancel = true; Retire(); } };
    }

    void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        App.X = Left; App.Y = Top; App.W = Width; App.H = Height;
    }

    public void Reveal()
    {
        if (!IsVisible) Show();
        Activate();
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    public void ShowPinned()
    {
        App.Open = true;
        Show();
    }

    /// <summary>Hidden when the overlay closes, unless it is pinned.</summary>
    public void Retire()
    {
        if (App.Pinned) return;
        Hide();
    }

    public void Shut()
    {
        IsShut = true;
        try { web?.Dispose(); } catch { }
        Close();
    }
}
