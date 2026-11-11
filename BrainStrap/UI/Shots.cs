using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrainStrap.UI;

/// <summary>
/// "BrainStrap.exe --shots &lt;folder&gt;": opens every page and saves a picture of it. The build uses this so the
/// real interface can be looked at after every change. It changes no settings and starts nothing.
/// </summary>
internal static class Shots
{
    public static async Task RunAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        try
        {
            // 1) the very first run, exactly as a new user sees it
            var first = new MainWindow("home") { WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, ShowInTaskbar = false };
            first.Show();
            await Task.Delay(2200);
            Save(first, Path.Combine(dir, "first-home.png"));
            first.Close();

            // 2) every page with sample data, so the pictures show the app in use
            BrainStrap.Core.Demo.Dress(BrainStrap.Core.AppState.S);
            var win = new MainWindow("home") { WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, ShowInTaskbar = false };
            win.Show();
            await Task.Delay(1800);
            var n = 0;
            foreach (var key in win.PageKeys)
            {
                try
                {
                    win.Go(key);
                    await Task.Delay(key is "store" or "brainai" or "launch" ? 2600 : 1100);
                    Save(win, Path.Combine(dir, $"{++n:00}-{key}.png"));
                }
                catch (Exception ex) { log.AppendLine(key + ": " + ex); }
            }
            // the same pages in a tall window, to see everything below the fold
            win.Height = 2300;
            await Task.Delay(600);
            n = 0;
            foreach (var key in win.PageKeys)
            {
                try
                {
                    win.Go(key);
                    await Task.Delay(key is "store" or "brainai" or "launch" ? 2600 : 900);
                    Save(win, Path.Combine(dir, $"tall-{++n:00}-{key}.png"));
                }
                catch (Exception ex) { log.AppendLine("tall " + key + ": " + ex); }
            }
            win.Close();
            foreach (var (name, make) in Extra())
            {
                try
                {
                    var w = make();
                    w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = 0; w.Top = 0; w.ShowInTaskbar = false;
                    w.Show();
                    await Task.Delay(1500);
                    Save(w, Path.Combine(dir, name + ".png"));
                    w.Close();
                }
                catch (Exception ex) { log.AppendLine(name + ": " + ex); }
            }
        }
        catch (Exception ex) { log.AppendLine(ex.ToString()); }
        File.WriteAllText(Path.Combine(dir, "log.txt"), log.Length == 0 ? "ok" : log.ToString());
    }

    static IEnumerable<(string, Func<Window>)> Extra()
    {
        yield return ("x-splash", () => new LaunchSplash(BrainStrap.Core.AppState.S));
        yield return ("x-overlay", () => { var w = new OverlayBackdrop(BrainStrap.Core.AppState.S, new OverlayHost(BrainStrap.Core.AppState.S, null)); w.Width = 1920; w.Height = 1080; return w; });
        yield return ("x-companion", () => new Pages.CompanionWindow());
    }

    static void Save(Window w, string path)
    {
        if (w.Content is not FrameworkElement root || root.ActualWidth < 1 || root.ActualHeight < 1) return;
        int pw = (int)Math.Ceiling(root.ActualWidth), ph = (int)Math.Ceiling(root.ActualHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(w.Background ?? Brushes.Black, null, new Rect(0, 0, pw, ph));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, pw, ph));
        }
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var f = File.Create(path)) enc.Save(f);
        // a lighter copy for the website
        var name = Path.GetFileNameWithoutExtension(path);
        if (name == "x-overlay" || (name.Length > 3 && char.IsDigit(name[0])))
        {
            var web = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "web")).FullName;
            var jpg = new JpegBitmapEncoder { QualityLevel = 90 };
            jpg.Frames.Add(BitmapFrame.Create(rtb));
            using var j = File.Create(Path.Combine(web, (name == "x-overlay" ? "overlay" : name[3..]) + ".jpg"));
            jpg.Save(j);
        }
    }
}
