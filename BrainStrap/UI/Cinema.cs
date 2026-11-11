using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using BrainStrap.Core;
using BrainStrap.Core.Servers;

namespace BrainStrap.UI;

/// <summary>
/// The cinematic layer: a 3D ring of your games on Home, pages that swing away in 3D when you move between them,
/// film grain, and an anamorphic lens flare drifting across the backdrop. All of it is drawn by the graphics card
/// through WPF's own 3D, and it pauses while the window is in the background.
/// </summary>
internal static class Cinema
{
    internal sealed record Card(string Title, string Sub, string? PlaceId, Action Click);

    static Color Hue(string name, double sat, double light)
    {
        var h = name.Aggregate(17, (a, c) => a * 31 + c) & 0xFFFF;
        double hue = h % 360 / 60.0, c2 = (1 - Math.Abs(2 * light - 1)) * sat, x = c2 * (1 - Math.Abs(hue % 2 - 1)), m = light - c2 / 2;
        var (r, g, b) = hue < 1 ? (c2, x, 0.0) : hue < 2 ? (x, c2, 0.0) : hue < 3 ? (0.0, c2, x) : hue < 4 ? (0.0, x, c2) : hue < 5 ? (x, 0.0, c2) : (c2, 0.0, x);
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    /// <summary>A card's face, drawn once as a picture: a poster with the game's icon (when it has loaded) and its name.</summary>
    static ImageSource Poster(Card c, BitmapSource? icon)
    {
        const int W = 360, H = 480;
        var grid = new Grid { Width = W, Height = H };
        var deep = Hue(c.Title, 0.35, 0.16);
        var bg = new LinearGradientBrush(Hue(c.Title, 0.45, 0.30), deep, 70);
        grid.Children.Add(new Border { Background = bg });
        if (icon != null) grid.Children.Add(new Image { Source = icon, Stretch = Stretch.UniformToFill, Opacity = 0.92 });
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.30));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(235, 6, 6, 8), 1));
        grid.Children.Add(new Border { Background = shade });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(26, 0, 26, 26) };
        var sub = new TextBlock { Text = c.Sub.ToUpperInvariant(), FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)) };
        sub.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        var title = new TextBlock { Text = c.Title, FontSize = 54, LineHeight = 52, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(-2, 6, 0, 0) };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        sp.Children.Add(sub);
        sp.Children.Add(title);
        grid.Children.Add(sp);
        grid.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(2) });
        grid.Measure(new Size(W, H));
        grid.Arrange(new Rect(0, 0, W, H));
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(grid);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>The 3D ring of games for the Home page. It turns slowly, leans toward the mouse, and a click on a card plays it.</summary>
    public static FrameworkElement Stage(IReadOnlyList<Card> cards, double height = 420)
    {
        var vp = new Viewport3D { Height = height, ClipToBounds = false, Cursor = Cursors.Hand, Margin = new Thickness(0, -10, 0, 0) };
        var cam = new PerspectiveCamera(new Point3D(0, 0.9, 14), new Vector3D(0, -0.07, -1), new Vector3D(0, 1, 0), 36);
        vp.Camera = cam;
        var world = new Model3DGroup();
        world.Children.Add(new AmbientLight(Color.FromRgb(70, 70, 76)));
        world.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.15, -0.1, -1)));
        var ring = new Model3DGroup();
        var spin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var ringT = new Transform3DGroup();
        ringT.Children.Add(new RotateTransform3D(spin));
        ringT.Children.Add(new TranslateTransform3D(0, 0.55, 0));
        ring.Transform = ringT;
        world.Children.Add(ring);

        var n = cards.Count;
        var radius = Math.Max(2.4, n * 0.52);
        const double w = 1.5, h = 2.0;
        var models = new Dictionary<GeometryModel3D, Card>();
        for (var i = 0; i < n; i++)
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new(-w / 2, -h / 2, 0), new(w / 2, -h / 2, 0), new(w / 2, h / 2, 0), new(-w / 2, h / 2, 0) },
                TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
                Normals = new Vector3DCollection { new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1) },
            };
            var face = new ImageBrush(Poster(cards[i], null));
            var model = new GeometryModel3D(mesh, new DiffuseMaterial(face))
            {
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(20, 20, 24))),
            };
            var t = new Transform3DGroup();
            t.Children.Add(new TranslateTransform3D(0, 0, radius));
            t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 360.0 * i / n)));
            model.Transform = t;
            ring.Children.Add(model);
            models[model] = cards[i];
            // the reflection on the floor
            var floorMat = new DiffuseMaterial(new ImageBrush(Poster(cards[i], null)) { Opacity = 0.13 });
            var floor = new GeometryModel3D(mesh, floorMat) { BackMaterial = floorMat };
            var ft = new Transform3DGroup();
            ft.Children.Add(new ScaleTransform3D(1, -1, 1));
            ft.Children.Add(new TranslateTransform3D(0, -h - 0.06, radius));
            ft.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 360.0 * i / n)));
            floor.Transform = ft;
            ring.Children.Add(floor);
            if (cards[i].PlaceId is { } pid) _ = LoadIcon(cards[i], pid, face, (DiffuseMaterial)floor.Material);
        }
        vp.Children.Add(new ModelVisual3D { Content = world });

        // a slow turn, a camera that settles in when the page opens, and a lean toward the mouse
        spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(0, -360, TimeSpan.FromSeconds(Math.Max(46, n * 9))) { RepeatBehavior = RepeatBehavior.Forever });
        if (!Demo.On)
            cam.BeginAnimation(ProjectionCamera.PositionProperty, new Point3DAnimation(new Point3D(0, 2.2, 20), new Point3D(0, 0.9, 14), TimeSpan.FromMilliseconds(1600)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
        Point target = default, now = default;
        vp.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(vp);
            target = new Point((p.X / Math.Max(1, vp.ActualWidth) - 0.5) * 2, (p.Y / Math.Max(1, vp.ActualHeight) - 0.5) * 2);
        };
        vp.MouseLeave += (_, _) => target = default;
        var lean = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            if (Math.Abs(target.X - now.X) < 0.001 && Math.Abs(target.Y - now.Y) < 0.001) return;
            now = new Point(now.X + (target.X - now.X) * 0.08, now.Y + (target.Y - now.Y) * 0.08);
            cam.LookDirection = new Vector3D(now.X * 0.10, -0.07 - now.Y * 0.05, -1);
            cam.UpDirection = new Vector3D(-now.X * 0.03, 1, 0);
        }, vp.Dispatcher);
        lean.Stop();
        vp.Loaded += (_, _) => lean.Start();
        vp.Unloaded += (_, _) => lean.Stop();

        vp.MouseLeftButtonUp += (_, e) =>
        {
            if (VisualTreeHelper.HitTest(vp, e.GetPosition(vp)) is RayMeshGeometry3DHitTestResult hit && hit.ModelHit is GeometryModel3D m && models.TryGetValue(m, out var card))
                card.Click();
        };
        return vp;
    }

    static async Task LoadIcon(Card c, string placeId, ImageBrush face, DiffuseMaterial floor)
    {
        try
        {
            var g = await RobloxApi.GameAsync(placeId);
            if (g == null) return;
            var file = await RobloxApi.IconAsync(g.UniverseId);
            if (file == null || !File.Exists(file)) return;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var img = new BitmapImage();
                img.BeginInit(); img.UriSource = new Uri(file); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 480; img.EndInit();
                img.Freeze();
                var poster = Poster(c with { Title = g.Name }, img);
                face.ImageSource = poster;
                floor.Brush = new ImageBrush(poster) { Opacity = 0.13 };
            });
        }
        catch (Exception ex) { Logger.Info("stage icon: " + ex.Message); }
    }

    /// <summary>
    /// A page swinging away in 3D: a picture of the old page is put on a plane that turns and falls back into the
    /// dark while the new page rises into place.
    /// </summary>
    /// <summary>A picture of the page as it is now, taken just before it changes.</summary>
    public static (BitmapSource shot, double w, double h)? Snap(FrameworkElement page)
    {
        try
        {
            double pw = page.ActualWidth, ph = page.ActualHeight;
            if (pw < 50 || ph < 50) return null;
            var dpi = VisualTreeHelper.GetDpi(page);
            var shot = new RenderTargetBitmap((int)(pw * dpi.DpiScaleX), (int)(ph * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            shot.Render(page);
            shot.Freeze();
            return (shot, pw, ph);
        }
        catch { return null; }
    }

    public static void Swap(Grid layer, (BitmapSource shot, double w, double h) snap, FrameworkElement newPage)
    {
        try
        {
            var (shot, pw, ph) = snap;

            const double fov = 40;
            var aspect = ph / pw;
            var dist = 1 / Math.Tan(fov * Math.PI / 360);
            var vp = new Viewport3D { IsHitTestVisible = false, Width = pw, Height = ph, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            vp.Camera = new PerspectiveCamera(new Point3D(0, 0, dist), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), fov);
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new(-1, -aspect, 0), new(1, -aspect, 0), new(1, aspect, 0), new(-1, aspect, 0) },
                TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
            };
            var brush = new ImageBrush(shot);
            var plane = new GeometryModel3D(mesh, new DiffuseMaterial(brush));
            var turn = new AxisAngleRotation3D(new Vector3D(0.15, 1, 0), 0);
            var push = new TranslateTransform3D();
            var tg = new Transform3DGroup();
            tg.Children.Add(new RotateTransform3D(turn, new Point3D(-1, 0, 0)));
            tg.Children.Add(push);
            plane.Transform = tg;
            var world = new Model3DGroup();
            world.Children.Add(new AmbientLight(Colors.White));
            world.Children.Add(plane);
            vp.Children.Add(new ModelVisual3D { Content = world });
            layer.Children.Add(vp);

            var d = TimeSpan.FromMilliseconds(560);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            turn.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(0, 32, d) { EasingFunction = ease });
            push.BeginAnimation(TranslateTransform3D.OffsetZProperty, new DoubleAnimation(0, -2.6, d) { EasingFunction = ease });
            var fade = new DoubleAnimation(1, 0, d) { EasingFunction = ease };
            fade.Completed += (_, _) => layer.Children.Remove(vp);
            brush.BeginAnimation(Brush.OpacityProperty, fade);

            // the new page rises from slightly below and slightly larger
            var up = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sc = new ScaleTransform(1.035, 1.035);
            var tr = new TranslateTransform(0, 28);
            newPage.RenderTransformOrigin = new Point(0.5, 0.2);
            newPage.RenderTransform = new TransformGroup { Children = { sc, tr } };
            var begin = TimeSpan.FromMilliseconds(120);
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.035, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = begin, EasingFunction = up });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.035, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = begin, EasingFunction = up });
            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(700)) { BeginTime = begin, EasingFunction = up });
            newPage.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(480)) { BeginTime = begin, EasingFunction = up });
        }
        catch (Exception ex) { Logger.Info("swap: " + ex.Message); }
    }

    /// <summary>Film grain over the whole window: a small noise tile that jumps around a dozen times a second.</summary>
    public static FrameworkElement Grain(double opacity = 0.05)
    {
        const int S = 160;
        var rng = new Random(7);
        var px = new byte[S * S * 4];
        for (var i = 0; i < S * S; i++)
        {
            var v = (byte)rng.Next(256);
            px[i * 4] = v; px[i * 4 + 1] = v; px[i * 4 + 2] = v; px[i * 4 + 3] = 255;
        }
        var bmp = BitmapSource.Create(S, S, 96, 96, PixelFormats.Bgra32, null, px, S * 4);
        bmp.Freeze();
        var brush = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, S, S), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        var layer = new Border { Background = brush, Opacity = opacity, IsHitTestVisible = false };
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) =>
        {
            var w = Window.GetWindow(layer);
            if (w == null || !w.IsActive) return;
            brush.Viewport = new Rect(rng.Next(S), rng.Next(S), S, S);
        }, layer.Dispatcher);
        layer.Unloaded += (_, _) => timer.Stop();
        return layer;
    }

    /// <summary>The vignette: the edges of the frame fall off into black.</summary>
    public static FrameworkElement Vignette(double strength = 0.55)
    {
        var g = new RadialGradientBrush { RadiusX = 0.75, RadiusY = 0.8, Center = new Point(0.55, 0.45), GradientOrigin = new Point(0.55, 0.45) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * strength), 0, 0, 0), 1));
        return new Border { Background = g, IsHitTestVisible = false };
    }

    /// <summary>An anamorphic lens flare: a long thin streak of light with a soft bloom, drifting and breathing.</summary>
    public static FrameworkElement Flare(Color color, double y = -210, double x = 260)
    {
        var grid = new Grid { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 1600, Height = 260 };
        Border Streak(double height, byte alpha, double width)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0.5));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
            return new Border { Height = height, Width = width, Background = b, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        var bloom = new System.Windows.Shapes.Ellipse { Width = 260, Height = 160, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var rg = new RadialGradientBrush();
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(70, color.R, color.G, color.B), 0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        bloom.Fill = rg;
        grid.Children.Add(bloom);
        grid.Children.Add(Streak(14, 26, 1500));
        grid.Children.Add(Streak(3, 95, 1100));
        var core = new System.Windows.Shapes.Ellipse { Width = 16, Height = 10, Fill = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(core);
        var move = new TranslateTransform(x, y);
        grid.RenderTransform = move;
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, x - 220, TimeSpan.FromSeconds(19)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, y + 40, TimeSpan.FromSeconds(13)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        grid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(4.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        return grid;
    }
}
