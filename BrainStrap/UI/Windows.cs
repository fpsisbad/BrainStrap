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
    readonly TextBlock status, detail, tip;
    readonly string style;

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
        Width = 460;
        SizeToContent = SizeToContent.Height;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        var card = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(30, 28, 30, 26), Margin = new Thickness(24), ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "Bg");
        card.SetResourceReference(Border.BorderBrushProperty, "Line2");
        card.BorderThickness = new Thickness(1);
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.5, Color = Colors.Black };

        var layers = new Grid();
        if (style == "silk")
        {
            // a slowly moving satin sheen across the card
            var accent = ((SolidColorBrush)Application.Current.FindResource("Accent")).Color;
            var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), Opacity = 0.16 };
            sheen.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            var mid = new GradientStop(accent, 0.3);
            sheen.GradientStops.Add(mid);
            sheen.GradientStops.Add(new GradientStop(Colors.Transparent, 0.6));
            mid.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0.05, 0.95, TimeSpan.FromSeconds(2.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            layers.Children.Add(new Border { Background = sheen, Margin = new Thickness(-30, -28, -30, -26) });
        }
        if (!string.IsNullOrEmpty(s.Studio.SplashImage) && File.Exists(s.Studio.SplashImage))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit(); img.UriSource = new Uri(s.Studio.SplashImage); img.DecodePixelWidth = 920; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
                layers.Children.Add(new Border { Margin = new Thickness(-30, -28, -30, -26), Opacity = 0.35, Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill } });
            }
            catch { }
        }

        var body = new StackPanel();
        var logo = new Image { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, Source = new BitmapImage(new Uri("pack://application:,,,/Assets/brainstrap-mark.png")) };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        if (style == "pulse")
        {
            var st = new ScaleTransform(1, 1);
            logo.RenderTransform = st;
            logo.RenderTransformOrigin = new Point(0.5, 0.5);
            var pulse = new DoubleAnimation(1, 1.12, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        if (style != "minimal") body.Children.Add(logo);
        var title = Ui.Text("BrainStrap", 26, "Ink", FontWeights.Bold);
        title.Margin = new Thickness(0, style == "minimal" ? 0 : 16, 0, 4);
        body.Children.Add(title);
        status = Ui.Text("Starting…", 14, "Muted", FontWeights.SemiBold);
        detail = Ui.Text("", 12, "Dim", mono: true);
        detail.Margin = new Thickness(0, 6, 0, 0);
        tip = Ui.Text("", 12.5, "Accent");
        tip.Margin = new Thickness(0, 10, 0, 0);
        tip.Visibility = Visibility.Collapsed;
        body.Children.Add(status);
        body.Children.Add(detail);
        body.Children.Add(tip);

        // indeterminate progress line
        var track = new Border { Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 20, 0, 0), ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "Pill");
        var bar = new Border { Width = 110, Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        bar.SetResourceReference(Border.BackgroundProperty, "Accent");
        var tt = new TranslateTransform(-110, 0);
        bar.RenderTransform = tt;
        track.Child = bar;
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-110, 420, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase() });
        body.Children.Add(track);

        layers.Children.Add(body);
        card.Child = layers;
        Content = card;
        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
    }

    public void Status(string text) => status.Text = text;

    public void Detail(string text, string? tipText)
    {
        detail.Text = text;
        if (!string.IsNullOrEmpty(tipText)) { tip.Text = tipText; tip.Visibility = Visibility.Visible; }
    }

    public async Task FinishAsync()
    {
        status.Text = "Have fun.";
        await Task.Delay(500);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        var done = new TaskCompletionSource();
        fade.Completed += (_, _) => done.TrySetResult();
        BeginAnimation(OpacityProperty, fade);
        await Task.WhenAny(done.Task, Task.Delay(800));
        Close();
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
