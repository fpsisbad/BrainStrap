using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BrainStrap.UI;

/// <summary>
/// The living silk behind the app: the same draped-satin maths as the RBLX.BRAIN website's shader, worked out
/// on the processor at a small size and smoothly scaled up (silk has no sharp edges, so it stays crisp-looking).
/// It takes its colours from the theme, costs about a millisecond a frame, and stops while the window is
/// minimised or in the background.
/// </summary>
internal sealed class Silk : Image
{
    const int W = 240, H = 150;
    readonly WriteableBitmap bmp = new(W, H, 96, 96, PixelFormats.Bgr32, null);
    readonly int[] px = new int[W * H];
    readonly DispatcherTimer timer;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly float[] lo = new float[3], hi = new float[3], sh = new float[3];
    readonly float seedX, seedY, speed;
    bool busy;

    public Silk(Color bg, Color ink, Color accent, bool dark, double strength = 1, double speed = 1.5, double seedX = 0.6, double seedY = -1.2)
    {
        this.seedX = (float)seedX; this.seedY = (float)seedY; this.speed = (float)speed;
        var k = (float)Math.Clamp(strength, 0.2, 1);
        // tint: mostly the text colour, pulled a little toward the accent
        float[] V(Color c) => new[] { c.R / 255f, c.G / 255f, c.B / 255f };
        float[] b = V(bg), i = V(ink), a = V(accent);
        for (var c = 0; c < 3; c++)
        {
            var tint = i[c] * 0.62f + a[c] * 0.38f;
            if (dark)
            {
                lo[c] = b[c] * 0.50f;                              // deep folds
                hi[c] = b[c] + (tint - b[c]) * 0.25f * k;          // lit cloth
                sh[c] = tint * 0.30f * k;                          // the sheen along a fold
            }
            else
            {
                lo[c] = b[c] + (i[c] - b[c]) * 0.16f * k;
                hi[c] = b[c] + (1f - b[c]) * 0.75f;
                sh[c] = 0.22f * k;
            }
        }
        Source = bmp;
        Stretch = Stretch.UniformToFill;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Render(4f);
        bmp.WritePixels(new Int32Rect(0, 0, W, H), px, W * 4, 0);

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(66), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        timer.Stop();
        Loaded += (_, _) => timer.Start();
        Unloaded += (_, _) => timer.Stop();
    }

    void Tick()
    {
        if (busy || !IsVisible) return;
        var win = Window.GetWindow(this);
        if (win == null || win.WindowState == WindowState.Minimized || !win.IsActive) return;
        busy = true;
        var t = 4f + (float)clock.Elapsed.TotalSeconds * speed;
        Task.Run(() => Render(t)).ContinueWith(_ =>
        {
            try { bmp.WritePixels(new Int32Rect(0, 0, W, H), px, W * 4, 0); } catch { }
            busy = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    void Render(float t)
    {
        // fold direction and its normal (the same constants as the website's shader, dark tone)
        var dl = MathF.Sqrt(1f + 1.15f * 1.15f);
        float dx = 1f / dl, dy = 1.15f / dl, ex = -dy, ey = dx;
        var ll = MathF.Sqrt(.55f * .55f + .7f * .7f + .45f * .45f);
        float lx = .55f / ll, ly = .7f / ll, lz = .45f / ll;
        var gl = MathF.Sqrt(1f + .55f * .55f);
        float gx = 1f / gl, gy = .55f / gl;
        var scale = 4.2f / Math.Min(W, H);

        Parallel.For(0, H, y =>
        {
            var py = ((H - 1 - y) + .5f - .5f * H) * scale;
            var row = y * W;
            for (var x = 0; x < W; x++)
            {
                var pxp = (x + .5f - .5f * W) * scale;
                float qx = pxp + seedX, qy = py + seedY;
                float u = qx * dx + qy * dy, v = qx * ex + qy * ey;

                // slope of the cloth, worked out exactly instead of by sampling the height four times
                var (s1, c1) = MathF.SinCos(.55f * v + .05f * t);
                var ca1 = MathF.Cos(1.05f * u + .7f * s1 + .035f * t);
                var (s2, c2) = MathF.SinCos(.9f * v - .04f * t);
                var ca2 = MathF.Cos(2.2f * u + .35f * v + .5f * s2 - .03f * t);
                var ca3 = MathF.Cos(4.3f * u - .6f * v + .05f * t);
                var ca4 = MathF.Cos(.8f * v + .3f * u + .02f * t);
                var hu = .9f * ca1 * 1.05f + .38f * ca2 * 2.2f + .12f * ca3 * 4.3f + .25f * ca4 * .3f;
                var hv = .9f * ca1 * .7f * c1 * .55f + .38f * ca2 * (.35f + .45f * c2) - .12f * ca3 * .6f + .25f * ca4 * .8f;
                float nx = -(hu * dx + hv * ex), ny = -(hu * dy + hv * ey), nz = 1.1f;
                var nl = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                nx *= nl; ny *= nl; nz *= nl;

                var ndl = nx * lx + ny * ly + nz * lz;
                var dif = Math.Clamp(ndl, 0f, 1f);
                var rz = Math.Clamp(2f * ndl * nz - lz, 0f, 1f);
                var r2 = rz * rz; var r4 = r2 * r2;
                var sheen = r4 * r4 * r2;                                   // ^10: a broad satin highlight
                var cr = 1f - nz; var crease = cr * MathF.Sqrt(cr);
                var light = Smooth(-1.6f, 1.4f, pxp * gx + py * gy);       // lit from the upper right
                var kd = MathF.Pow(dif, 1.8f) * light;
                float ux = (x + .5f) / W - .5f, uy = (y + .5f) / H - .5f;
                var vig = 1f - .18f * Smooth(.5f, 1.1f, MathF.Sqrt(ux * ux + uy * uy));

                int Ch(int c)
                {
                    var val = (lo[c] + (hi[c] - lo[c]) * kd + sheen * sh[c] * light - crease * .05f) * vig;
                    return (int)(Math.Clamp(val, 0f, 1f) * 255f + .5f);
                }
                px[row + x] = (Ch(0) << 16) | (Ch(1) << 8) | Ch(2);
            }
        });
    }
}
