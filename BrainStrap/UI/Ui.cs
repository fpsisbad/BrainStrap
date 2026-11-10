using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BrainStrap.Core;

namespace BrainStrap.UI;

/// <summary>Small builders so every code-built page looks like the XAML ones (same styles, same theme colours).</summary>
internal static class Ui
{
    static Style Sty(string key) => (Style)Application.Current.FindResource(key);
    static bool Minimal => AppState.S.Studio.Minimal;

    public static TextBlock H1(string text) => new() { Text = text, Style = Sty("H1") };

    public static TextBlock Sub(string text) => new() { Text = text, Style = Sty("Sub") };

    public static TextBlock Lbl(string text, double top = 0) => new() { Text = text.ToUpperInvariant(), Style = Sty("Lbl"), Margin = new Thickness(0, top, 0, 10) };

    public static TextBlock Small(string text, double top = 2)
    {
        var t = new TextBlock { Text = text, Style = Sty("Small"), Margin = new Thickness(0, top, 0, 0) };
        return t;
    }

    /// <summary>A description line. Minimal Mode hides these.</summary>
    public static UIElement Desc(string text, double top = 2) => Minimal ? new Border() : Small(text, top);

    public static TextBlock Title(string text, double size = 14.5) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = size, TextWrapping = TextWrapping.Wrap };

    public static TextBlock Text(string text, double size = 13, string brush = "Ink", FontWeight? weight = null, bool mono = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (mono) t.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont");
        return t;
    }

    public static TextBlock Big(string text, string brush = "Ink") => Text(text, 30, brush, FontWeights.ExtraBold);

    public static Border Card(params UIElement[] children)
    {
        var sp = new StackPanel();
        foreach (var c in children) sp.Children.Add(c);
        return new Border { Style = Sty("Card"), Child = sp };
    }

    public static Border Card(Panel content) => new() { Style = Sty("Card"), Child = content };

    public static Button Pill(string text, Action click)
    {
        var b = new Button { Content = text, Style = Sty("PillButton"), Margin = new Thickness(0, 0, 8, 8) };
        b.Click += (_, _) => Guard(click);
        return b;
    }

    public static Button Ghost(string text, Action click)
    {
        var b = new Button { Content = text, Style = Sty("GhostButton"), Margin = new Thickness(0, 0, 8, 8) };
        b.Click += (_, _) => Guard(click);
        return b;
    }

    public static Button AsyncPill(string text, Func<Task> click, bool ghost = false)
    {
        var b = new Button { Content = text, Style = Sty(ghost ? "GhostButton" : "PillButton"), Margin = new Thickness(0, 0, 8, 8) };
        b.Click += async (_, _) =>
        {
            var old = b.Content;
            b.IsEnabled = false;
            b.Content = "Working…";
            try { await click(); }
            catch (Exception ex) { Logger.Error("action failed", ex); MessageBox.Show(ex.Message, "BrainStrap"); }
            finally { b.IsEnabled = true; b.Content = old; }
        };
        return b;
    }

    static void Guard(Action a)
    {
        try { a(); }
        catch (Exception ex) { Logger.Error("action failed", ex); MessageBox.Show(ex.Message, "BrainStrap"); }
    }

    public static WrapPanel Buttons(params UIElement[] items)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    public static StackPanel Row(params UIElement[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) sp.Children.Add(i);
        return sp;
    }

    public static CheckBox Toggle(bool on, Action<bool> changed)
    {
        var c = new CheckBox { Style = Sty("Toggle"), IsChecked = on };
        c.Click += (_, _) => changed(c.IsChecked == true);
        return c;
    }

    /// <summary>Title + description on the left, a switch on the right.</summary>
    public static Grid ToggleRow(string title, string desc, bool on, Action<bool> changed)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(Title(title));
        if (desc.Length > 0) left.Children.Add(Desc(desc));
        var t = Toggle(on, changed);
        Grid.SetColumn(t, 1);
        g.Children.Add(left);
        g.Children.Add(t);
        return g;
    }

    static int seq;

    public static WrapPanel Chips(IEnumerable<(string label, string value)> items, string? current, Action<string> picked)
    {
        var w = new WrapPanel();
        var group = "chips" + (++seq);
        foreach (var (label, value) in items)
        {
            var v = value;
            var rb = new RadioButton { Content = label, GroupName = group, Style = Sty("Chip"), IsChecked = v == current };
            rb.Click += (_, _) => Guard(() => picked(v));
            w.Children.Add(rb);
        }
        return w;
    }

    public static TextBox Field(string text = "", double width = double.NaN, bool mono = false)
    {
        var t = new TextBox { Text = text, Style = Sty("Field"), Width = width };
        if (mono) t.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont");
        return t;
    }

    public static Grid KV(string label, UIElement value, double labelWidth = 170)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label.ToUpperInvariant(), Style = Sty("Lbl"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 1);
        g.Children.Add(l);
        g.Children.Add(value);
        return g;
    }

    public static Grid KV(string label, string value, string brush = "Ink") => KV(label, Text(value, 13, brush, FontWeights.SemiBold));

    /// <summary>Two (or more) columns that wrap on narrow windows.</summary>
    public static UniformGrid Cols(int n, params UIElement[] items)
    {
        var g = new UniformGrid { Columns = n };
        foreach (var i in items)
        {
            if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(i);
        }
        return g;
    }

    /// <summary>A big number with a caption, for stat tiles.</summary>
    public static StackPanel Stat(string value, string caption, string brush = "Ink")
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 24, 8) };
        sp.Children.Add(Big(value, brush));
        sp.Children.Add(Small(caption, 0));
        return sp;
    }

    public static string LevelBrush(string level) => level switch
    {
        "ok" or "info" => "Ok",
        "warn" or "medium" => "Warn",
        "bad" or "high" => "Bad",
        _ => "Muted",
    };

    /// <summary>A coloured dot + title + text, used for checks and findings.</summary>
    public static Grid Status(string level, string title, string detail, UIElement? action = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        dot.SetResourceReference(Shape.FillProperty, LevelBrush(level));
        var body = new StackPanel();
        body.Children.Add(Title(title, 14));
        if (detail.Length > 0) body.Children.Add(Small(detail));
        Grid.SetColumn(body, 1);
        g.Children.Add(dot);
        g.Children.Add(body);
        if (action != null)
        {
            if (action is FrameworkElement fe) { fe.Margin = new Thickness(16, 0, 0, 0); fe.VerticalAlignment = VerticalAlignment.Top; }
            Grid.SetColumn(action, 2);
            g.Children.Add(action);
        }
        return g;
    }

    /// <summary>A meter bar 0..1.</summary>
    public static Border Meter(double value, string brush = "Accent", double height = 6)
    {
        var track = new Grid { Height = height };
        var bg = new Border { CornerRadius = new CornerRadius(height / 2) };
        bg.SetResourceReference(Border.BackgroundProperty, "Pill");
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        track.Children.Add(bg);
        track.Children.Add(fill);
        track.SizeChanged += (_, _) => fill.Width = Math.Max(0, track.ActualWidth * Math.Clamp(value, 0, 1));
        return new Border { Child = track, Margin = new Thickness(0, 6, 0, 0) };
    }

    /// <summary>Frametime Graph: a line of frame times with the 60/30 FPS lines marked. Spikes are stutters.</summary>
    public static FrameworkElement Graph(IReadOnlyList<float> ms, double height = 140, string brush = "Accent", IReadOnlyList<float>? compare = null)
    {
        var canvas = new Canvas { Height = height, ClipToBounds = true, Background = Brushes.Transparent };
        void Draw()
        {
            canvas.Children.Clear();
            var w = canvas.ActualWidth;
            if (w <= 0 || ms.Count < 2) return;
            var max = Math.Max(40, Math.Min(120, Math.Max(ms.Max(), compare?.DefaultIfEmpty(0).Max() ?? 0) * 1.1));
            foreach (var (fpsLine, label) in new[] { (1000.0 / 60, "60 fps"), (1000.0 / 30, "30 fps") })
            {
                if (fpsLine > max) continue;
                var y = height - fpsLine / max * height;
                var line = new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 4 } };
                line.SetResourceReference(Shape.StrokeProperty, "Line2");
                canvas.Children.Add(line);
                var t = Text(label, 10, "Dim", mono: true);
                Canvas.SetLeft(t, 4);
                Canvas.SetTop(t, y - 15);
                canvas.Children.Add(t);
            }
            void Series(IReadOnlyList<float> data, string b, double thickness, double opacity)
            {
                var pl = new Polyline { StrokeThickness = thickness, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round };
                pl.SetResourceReference(Shape.StrokeProperty, b);
                for (var i = 0; i < data.Count; i++)
                    pl.Points.Add(new Point(i * w / (data.Count - 1), height - Math.Min(max, data[i]) / max * height));
                canvas.Children.Add(pl);
            }
            if (compare != null && compare.Count > 1) Series(compare, "Dim", 1.2, 0.8);
            Series(ms, brush, 1.6, 1);
        }
        canvas.SizeChanged += (_, _) => Draw();
        return new Border { Child = canvas, Margin = new Thickness(0, 8, 0, 6) };
    }

    /// <summary>A small sparkline for ping/FPS history.</summary>
    public static FrameworkElement Spark(IReadOnlyList<double> values, double height = 40, string brush = "Accent")
    {
        var canvas = new Canvas { Height = height, ClipToBounds = true, Background = Brushes.Transparent };
        canvas.SizeChanged += (_, _) =>
        {
            canvas.Children.Clear();
            if (values.Count < 2) return;
            double mn = values.Min(), mx = values.Max();
            if (mx - mn < 1) { mx += 1; mn -= 1; }
            var pl = new Polyline { StrokeThickness = 1.6 };
            pl.SetResourceReference(Shape.StrokeProperty, brush);
            for (var i = 0; i < values.Count; i++)
                pl.Points.Add(new Point(i * canvas.ActualWidth / (values.Count - 1), height - 3 - (values[i] - mn) / (mx - mn) * (height - 6)));
            canvas.Children.Add(pl);
        };
        return canvas;
    }

    public static ScrollViewer Page(string title, string sub, out StackPanel body)
    {
        body = new StackPanel { Margin = new Thickness(44, 14, 44, 44), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        if (title.Length > 0)
        {
            body.Children.Add(H1(title));
            body.Children.Add(Minimal ? new Border { Height = 18 } : Sub(sub));
        }
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body };
    }

    /// <summary>The big panel at the top of a page: a small label, a large headline, a line of text and the main actions.</summary>
    public static Border Hero(string eyebrow, string title, string sub, params UIElement[] actions)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 210, 0) };
        var eye = Lbl(eyebrow);
        eye.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        sp.Children.Add(eye);
        var h = new TextBlock { Text = title, FontSize = 44, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap, LineHeight = 50, Margin = new Thickness(0, 4, 0, 10) };
        sp.Children.Add(h);
        if (!Minimal && sub.Length > 0)
        {
            var t = Text(sub, 15, "Muted");
            t.LineHeight = 23;
            t.MaxWidth = 520;
            t.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(t);
        }
        if (actions.Length > 0)
        {
            var w = Buttons(actions);
            w.Margin = new Thickness(0, 22, 0, 0);
            sp.Children.Add(w);
        }
        // the mark, large and faint, sits behind the right-hand side
        var mark = new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/brainstrap-mark.png")),
            Width = 250, Height = 250, Opacity = 0.11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -60, -50, -60),
            IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-10),
        };
        RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
        var grid = new Grid { ClipToBounds = true };
        grid.Children.Add(mark);
        grid.Children.Add(sp);
        var card = new Border { CornerRadius = new CornerRadius(26), Padding = new Thickness(34, 32, 34, 32), Margin = new Thickness(0, 4, 0, 16), BorderThickness = new Thickness(1), Child = grid, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardLineHover");
        return card;
    }

    /// <summary>A pill button with an icon in front of the text.</summary>
    public static Button IconPill(string icon, string text, Action click, bool ghost = false, double height = 44)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = Icons.Make(icon, 16, 2.1);
        ic.Margin = new Thickness(0, 0, 9, 0);
        row.Children.Add(ic);
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = row, Style = Sty(ghost ? "GhostButton" : "PillButton"), Margin = new Thickness(0, 0, 10, 8), Height = height, FontSize = 14, Padding = new Thickness(22, 0, 24, 0) };
        b.Click += (_, _) => Guard(click);
        return b;
    }

    /// <summary>A glass tile: an icon, a big value and a caption. Clickable when given an action.</summary>
    public static FrameworkElement StatTile(string icon, string value, string caption, string brush = "Ink", Action? click = null)
    {
        var sp = new StackPanel();
        var badge = Icons.Badge(icon, 36, brush == "Ink" ? "Muted" : brush);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.Margin = new Thickness(0, 0, 0, 14);
        sp.Children.Add(badge);
        var v = Text(value, 21, brush, FontWeights.ExtraBold);
        v.TextWrapping = TextWrapping.NoWrap;
        v.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(v);
        sp.Children.Add(Small(caption, 2));
        if (click != null)
        {
            var b = new Button { Content = sp, Style = Sty("TileButton"), Padding = new Thickness(18) };
            b.Click += (_, _) => Guard(click);
            return b;
        }
        return new Border { Style = Sty("Tile"), Padding = new Thickness(18), Child = sp };
    }

    /// <summary>Equal-width tiles in a row with gaps between them.</summary>
    public static Grid TileRow(params FrameworkElement[] tiles)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        for (var i = 0; i < tiles.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tiles[i].Margin = new Thickness(i == 0 ? 0 : 6, 0, i == tiles.Length - 1 ? 0 : 6, 0);
            Grid.SetColumn(tiles[i], i);
            g.Children.Add(tiles[i]);
        }
        return g;
    }

    /// <summary>A small rounded label, e.g. "NEW" or "MANAGED BY BRAINSTRAP".</summary>
    public static Border Tag(string text, string brush = "Muted")
    {
        var t = Text(text.ToUpperInvariant(), 9.5, brush, FontWeights.Bold, true);
        var b = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 3, 8, 3), Child = t, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Pill");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        return b;
    }

    /// <summary>A card heading with an icon badge, a title and a line of description.</summary>
    public static Grid Head(string icon, string title, string desc = "")
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = Icons.Badge(icon, 40);
        badge.Margin = new Thickness(0, 0, 14, 0);
        badge.VerticalAlignment = VerticalAlignment.Top;
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Title(title, 16.5));
        if (desc.Length > 0) sp.Children.Add(Desc(desc));
        Grid.SetColumn(sp, 1);
        g.Children.Add(badge);
        g.Children.Add(sp);
        return g;
    }

    public static void Msg(TextBlock t, string text, string brush = "Muted")
    {
        t.Text = text;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Warn("could not open " + target + ": " + ex.Message); }
    }

    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }

    public static string Mb(long bytes) => bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.0} GB" : $"{bytes / 1048576.0:0} MB";

    public static Cursor Hand => Cursors.Hand;
}

/// <summary>A dropdown in the BrainStrap style. (WPF's own ComboBox can't be recoloured without replacing its whole template.)</summary>
internal sealed class DarkCombo : Button
{
    public List<object> Items { get; } = new();
    public bool IsEditable { get; set; }
    public event EventHandler? SelectionChanged;
    int index = -1;
    readonly TextBlock label = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    readonly System.Windows.Controls.Primitives.Popup popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, VerticalOffset = 6 };

    public DarkCombo()
    {
        Style = (Style)Application.Current.FindResource("GhostButton");
        Margin = new Thickness(0);
        var row = new DockPanel();
        var arrow = new TextBlock { Text = "▾", Margin = new Thickness(10, 0, 0, 0), Opacity = 0.7 };
        DockPanel.SetDock(arrow, Dock.Right);
        row.Children.Add(arrow);
        row.Children.Add(label);
        Content = row;
        popup.PlacementTarget = this;
        Click += (_, _) => Open();
    }

    public int SelectedIndex
    {
        get => index;
        set
        {
            var v = value >= 0 && value < Items.Count ? value : -1;
            if (v == index) return;
            index = v;
            label.Text = v >= 0 ? Items[v]?.ToString() ?? "" : "";
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem
    {
        get => index >= 0 ? Items[index] : null;
        set => SelectedIndex = value == null ? -1 : Items.IndexOf(value);
    }

    void Open()
    {
        var list = new StackPanel();
        for (var i = 0; i < Items.Count; i++)
        {
            var n = i;
            var item = new Button
            {
                Content = Items[i]?.ToString(), Style = (Style)Application.Current.FindResource("GhostButton"), Height = 32, Margin = new Thickness(0, 0, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Left, FontWeight = n == index ? FontWeights.Bold : FontWeights.SemiBold,
            };
            if (n != index) item.Background = Brushes.Transparent;
            item.Click += (_, _) => { popup.IsOpen = false; SelectedIndex = n; };
            list.Children.Add(item);
        }
        var frame = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(6), BorderThickness = new Thickness(1), MinWidth = ActualWidth, MaxHeight = 320,
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list },
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Panel2");
        frame.SetResourceReference(Border.BorderBrushProperty, "Line2");
        popup.Child = frame;
        popup.IsOpen = true;
    }
}
