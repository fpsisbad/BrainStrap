using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BrainStrap.Core;
using BrainStrap.Core.Perf;

namespace BrainStrap.UI;

/// <summary>Custom Launch Animations: the launch screen shown between pressing Play and Roblox appearing.</summary>
internal sealed class LaunchSplash : Window
{
    readonly TextBlock status, detail, tip, percent;
    readonly Border bar, track;
    readonly TranslateTransform sweep = new(-140, 0);
    readonly string style;
    const double CardWidth = 500;

    public LaunchSplash(Settings s)
    {
        style = s.Studio.Splash;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = CardWidth + 80;
        SizeToContent = SizeToContent.Height;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        SetResourceReference(ForegroundProperty, "Ink");

        var card = new Border { CornerRadius = new CornerRadius(26), Margin = new Thickness(40), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Bg");
        card.SetResourceReference(Border.BorderBrushProperty, "CardLineHover");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 46, ShadowDepth = 14, Opacity = 0.6, Color = Colors.Black, Direction = 270 };

        // everything inside is clipped to the rounded card
        var layers = new Grid();
        layers.SizeChanged += (_, _) => layers.Clip = new RectangleGeometry(new Rect(0, 0, layers.ActualWidth, layers.ActualHeight), 25, 25);
        var theme = Themes.Effective(s);
        Color Res(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        if (style == "silk")
            layers.Children.Add(new Silk((Color)ColorConverter.ConvertFromString(theme.Bg), Res("Ink"), Res("Accent"), theme.Dark, 1, 3.2, 1.9, -0.4));
        if (!string.IsNullOrEmpty(s.Studio.SplashImage) && File.Exists(s.Studio.SplashImage))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit(); img.UriSource = new Uri(s.Studio.SplashImage); img.DecodePixelWidth = 1000; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
                layers.Children.Add(new Border { Opacity = 0.4, Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill } });
            }
            catch { }
        }
        // a veil so the text always reads, whatever is behind it
        var veil = new Border { Opacity = style == "silk" ? 0.38 : 0 };
        veil.SetResourceReference(Border.BackgroundProperty, "Bg");
        layers.Children.Add(veil);

        var body = new StackPanel { Margin = new Thickness(36, 34, 36, 30) };
        if (style != "minimal")
        {
            // the mark, with a soft halo that breathes
            var halo = new System.Windows.Shapes.Ellipse { Width = 120, Height = 120, Opacity = 0.5, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            var glow = new RadialGradientBrush();
            var ac = Res("Accent");
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(120, ac.R, ac.G, ac.B), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, ac.R, ac.G, ac.B), 1));
            halo.Fill = glow;
            var breathe = new DoubleAnimation(0.85, 1.25, TimeSpan.FromSeconds(style == "pulse" ? 0.8 : 1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            ((ScaleTransform)halo.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
            ((ScaleTransform)halo.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
            var logo = new Image { Width = 68, Height = 68, Source = new BitmapImage(new Uri("pack://application:,,,/Assets/brainstrap-mark.png")) };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            var mark = new Grid { Width = 120, Height = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-26, -26, 0, -14) };
            mark.Children.Add(halo);
            mark.Children.Add(logo);
            body.Children.Add(mark);
        }
        var eyebrow = Ui.Text("BRAINSTRAP  ·  " + s.ActiveProfile.ToUpperInvariant() + " PROFILE", 10, "Muted", FontWeights.Medium, true);
        eyebrow.Margin = new Thickness(0, 0, 0, 6);
        body.Children.Add(eyebrow);
        status = Ui.Text("Starting…", 26, "Ink", FontWeights.ExtraBold);
        status.LineHeight = 31;
        body.Children.Add(status);
        detail = Ui.Text("", 12, "Muted", mono: true);
        detail.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(detail);
        tip = Ui.Text("", 12.5, "Accent");
        tip.Margin = new Thickness(0, 10, 0, 0);
        tip.Visibility = Visibility.Collapsed;
        body.Children.Add(tip);

        // progress: a sweeping light while waiting, a filling bar while downloading
        track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Margin = new Thickness(0, 24, 0, 0), ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "Pill");
        bar = new Border { Width = 140, Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = sweep };
        bar.SetResourceReference(Border.BackgroundProperty, "Accent");
        track.Child = bar;
        sweep.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-140, CardWidth - 70, TimeSpan.FromSeconds(1.25)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
        body.Children.Add(track);
        percent = Ui.Text("", 10.5, "Dim", mono: true);
        percent.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(percent);

        layers.Children.Add(body);
        card.Child = layers;
        Content = card;
        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
    }

    public void Status(string text) => status.Text = text;

    public void Detail(string text, string? tipText)
    {
        detail.Text = text;
        if (!string.IsNullOrEmpty(tipText)) { tip.Text = tipText; tip.Visibility = Visibility.Visible; }
    }

    /// <summary>Switches the bar from "waiting" to a real 0..1 amount.</summary>
    public void Progress(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        sweep.BeginAnimation(TranslateTransform.XProperty, null);
        sweep.X = 0;
        var w = Math.Max(1, track.ActualWidth);
        bar.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(6, w * fraction), TimeSpan.FromMilliseconds(220)));
        percent.Text = fraction >= 1 ? "" : $"{fraction * 100:0}%";
    }

    /// <summary>Feeds the Roblox installer's progress into this window.</summary>
    public IProgress<RobloxDeploy.Step> Reporter() => new Progress<RobloxDeploy.Step>(p =>
    {
        status.Text = p.Stage;
        detail.Text = p.Detail;
        Progress(p.Fraction);
    });

    public async Task FinishAsync(string? text = null)
    {
        status.Text = text ?? "Have fun.";
        Progress(1);
        await Task.Delay(520);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
        var done = new TaskCompletionSource();
        fade.Completed += (_, _) => done.TrySetResult();
        BeginAnimation(OpacityProperty, fade);
        await Task.WhenAny(done.Task, Task.Delay(800));
        Close();
    }
}

/// <summary>
/// BrainSight: a crosshair in the centre of the screen. It is an ordinary see-through window that belongs to
/// BrainStrap and ignores the mouse, the same idea as the crosshair many gaming monitors can draw. It shows only
/// while Roblox is the window in front, and it never reads or changes anything in the game.
/// </summary>
internal sealed class CrosshairOverlay : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    public CrosshairOverlay(Settings s)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        var box = s.Crosshair.Size * 2 + 24;
        Width = box; Height = box;
        Left = (SystemParameters.PrimaryScreenWidth - box) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - box) / 2;
        Content = Draw(s.Crosshair, box);
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x80000 | 0x08000000);   // click-through, no taskbar entry, layered, never takes focus
        };
    }

    /// <summary>The crosshair picture (also used for the preview on the BrainLife page).</summary>
    public static FrameworkElement Draw(CrosshairSettings c, double box)
    {
        var canvas = new Canvas { Width = box, Height = box, Opacity = c.Opacity, IsHitTestVisible = false };
        var color = Themes.IsHex(c.Color) ? (Color)ColorConverter.ConvertFromString(c.Color) : Colors.White;
        var fill = new SolidColorBrush(color);
        var edge = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        double mid = box / 2, t = c.Thickness, len = c.Size, gap = c.Gap;
        void Line(double x1, double y1, double x2, double y2)
        {
            if (c.Outline) canvas.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = edge, StrokeThickness = t + 2, StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Square });
            canvas.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = fill, StrokeThickness = t });
        }
        void Ring(double radius, bool solid)
        {
            var e = new System.Windows.Shapes.Ellipse { Width = radius * 2, Height = radius * 2, Stroke = solid ? (c.Outline ? edge : null) : fill, StrokeThickness = solid ? 1 : t, Fill = solid ? fill : null };
            Canvas.SetLeft(e, mid - radius); Canvas.SetTop(e, mid - radius);
            if (!solid && c.Outline)
            {
                var o = new System.Windows.Shapes.Ellipse { Width = radius * 2 + 2, Height = radius * 2 + 2, Stroke = edge, StrokeThickness = t + 2 };
                Canvas.SetLeft(o, mid - radius - 1); Canvas.SetTop(o, mid - radius - 1);
                canvas.Children.Add(o);
            }
            canvas.Children.Add(e);
        }
        switch (c.Shape)
        {
            case "dot":
                Ring(Math.Max(1.5, t * 1.5), true);
                break;
            case "circle":
                Ring(len / 2, false);
                Ring(Math.Max(1, t * 0.8), true);
                break;
            case "tee":
                Line(mid - gap - len, mid, mid - gap, mid); Line(mid + gap, mid, mid + gap + len, mid); Line(mid, mid + gap, mid, mid + gap + len);
                break;
            default:
                Line(mid - gap - len, mid, mid - gap, mid); Line(mid + gap, mid, mid + gap + len, mid);
                Line(mid, mid - gap - len, mid, mid - gap); Line(mid, mid + gap, mid, mid + gap + len);
                break;
        }
        return canvas;
    }

    /// <summary>Shows the crosshair only while Roblox is the window in front.</summary>
    public void Sync(System.Diagnostics.Process roblox)
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            Visibility = pid == (uint)roblox.Id ? Visibility.Visible : Visibility.Hidden;
        }
        catch { }
    }
}

/// <summary>
/// BrainHUD: a small always-on-top, click-through window with FPS, ping and hardware numbers.
/// It is a normal separate window drawn over the game (like a sticky note), not something inside Roblox.
/// Works in windowed and borderless fullscreen. Ctrl+Shift+H hides and shows it.
/// </summary>
internal sealed class Hud : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    const int HotkeyId = 0xB5;

    readonly Settings s;
    readonly TextBlock fpsT, pingT, hwT, clockT;
    readonly Border card;

    public Hud(Settings settings)
    {
        s = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        Opacity = s.Hud.Opacity;

        var mono = (FontFamily)Application.Current.FindResource("MonoFont");
        TextBlock T(double size, string brush)
        {
            var t = new TextBlock { FontFamily = mono, FontSize = size, Margin = new Thickness(0, 1, 0, 1) };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }
        fpsT = T(15, "Ink");
        fpsT.FontWeight = FontWeights.Bold;
        pingT = T(12, "Muted");
        hwT = T(12, "Muted");
        clockT = T(11, "Dim");
        var sp = new StackPanel();
        if (s.Hud.ShowFps) sp.Children.Add(fpsT);
        if (s.Hud.ShowPing) sp.Children.Add(pingT);
        if (s.Hud.ShowHardware) sp.Children.Add(hwT);
        if (s.Hud.ShowClock) sp.Children.Add(clockT);
        card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 14, 8), Child = sp, BorderThickness = new Thickness(1) };
        var bg = ((SolidColorBrush)Application.Current.FindResource("Bg")).Color;
        card.Background = new SolidColorBrush(Color.FromArgb(0xCC, bg.R, bg.G, bg.B));
        card.SetResourceReference(Border.BorderBrushProperty, "Line2");
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE);
            RegisterHotKey(h, HotkeyId, 0x0002 | 0x0004 | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(Key.H));   // Ctrl+Shift+H, no repeat
            HwndSource.FromHwnd(h)?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == 0x0312 && wp.ToInt32() == HotkeyId) { card.Visibility = card.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible; handled = true; }
                return IntPtr.Zero;
            });
        };
        Closed += (_, _) => { try { UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId); } catch { } };
        SizeChanged += (_, _) => Place();
        fpsT.Text = "BrainHUD";
    }

    void Place()
    {
        double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight, m = 14;
        Left = s.Hud.Corner.EndsWith("right") ? sw - ActualWidth - m : m;
        Top = s.Hud.Corner.StartsWith("bottom") ? sh - ActualHeight - m - 40 : m;
    }

    public void Update(LiveState live, TimeSpan elapsed)
    {
        fpsT.Text = live.Measuring ? (live.Fps > 0 ? $"{live.Fps:0} FPS · 1% {live.Low1:0}" : "measuring…") : "FPS: set up in Performance";
        pingT.Text = live.Ping is { Samples: > 0 } p
            ? (p.LossPct >= 100 ? "PING  server ignores ping" : $"PING  {p.Avg:0} ms · ±{p.Jitter:0}{(p.LossPct > 0 ? $" · {p.LossPct:0}% loss" : "")}")
            : "PING  waiting for server";
        if (live.Hw is { } hw)
            hwT.Text = $"CPU {hw.CpuPct:0}%  GPU {hw.GpuPct:0}%  RAM {Ui.Mb(hw.RamBytes)}" +
                       (hw.CpuTemp is { } c ? $"  {c:0}°C" : "") + (hw.GpuTemp is { } g ? $"  GPU {g:0}°C" : "");
        clockT.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}  ·  {DateTime.Now:HH:mm}";
    }
}

/// <summary>A small notice in the corner (used when a session ends with a crash or disconnect).</summary>
internal sealed class Toast : Window
{
    readonly TaskCompletionSource closed = new();
    public Task ClosedTask => closed.Task;

    public Toast(string title, string text, string? actionLabel = null, Action? action = null)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(title, 15, "Ink", FontWeights.Bold));
        var body = Ui.Text(text, 13, "Muted");
        body.Margin = new Thickness(0, 6, 0, 12);
        sp.Children.Add(body);
        var buttons = new WrapPanel();
        if (actionLabel != null && action != null) buttons.Children.Add(Ui.Pill(actionLabel, () => { action(); Close(); }));
        buttons.Children.Add(Ui.Ghost("Dismiss", Close));
        sp.Children.Add(buttons);
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 18, 20, 10), Margin = new Thickness(16), Child = sp, BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Bg");
        card.SetResourceReference(Border.BorderBrushProperty, "Line2");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.45 };
        Content = card;
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 8;
            Top = wa.Bottom - ActualHeight - 8;
        };
        Closed += (_, _) => closed.TrySetResult();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) => { timer.Stop(); if (IsVisible) Close(); };
        timer.Start();
    }
}

/// <summary>Command Palette (Ctrl+K): type to find any page or action.</summary>
internal sealed class CommandPalette : Window
{
    public CommandPalette(Window owner, List<(string title, string hint, Action run)> commands)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 560;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        var box = Ui.Field();
        box.FontSize = 15;
        var list = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 10, 0, 0) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SetResourceReference(Control.ForegroundProperty, "Ink");

        void Fill()
        {
            list.Items.Clear();
            var q = box.Text.Trim().ToLowerInvariant();
            foreach (var c in commands.Where(c => q.Length == 0 || (c.title + " " + c.hint).ToLowerInvariant().Contains(q)).Take(40))
            {
                var row = new StackPanel { Margin = new Thickness(4, 6, 4, 6), Tag = c.run };
                row.Children.Add(Ui.Title(c.title, 14));
                row.Children.Add(Ui.Text(c.hint, 12, "Dim"));
                list.Items.Add(row);
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        void Run()
        {
            if (list.SelectedItem is StackPanel { Tag: Action a }) { Close(); a(); }
        }
        box.TextChanged += (_, _) => Fill();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && list.SelectedIndex < list.Items.Count - 1) { list.SelectedIndex++; list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Up && list.SelectedIndex > 0) { list.SelectedIndex--; list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Enter) { Run(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        list.MouseDoubleClick += (_, _) => Run();
        Deactivated += (_, _) => { if (IsVisible) Close(); };

        var head = Ui.Lbl("Command palette · Enter to run · Esc to close");
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(box, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(box);
        dock.Children.Add(list);
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Child = dock, BorderThickness = new Thickness(1), Margin = new Thickness(10) };
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Line2");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.5 };
        Content = card;
        Loaded += (_, _) => { Fill(); box.Focus(); };
    }
}
