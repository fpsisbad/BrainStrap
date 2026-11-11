using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BrainStrap.Core;

namespace BrainStrap.UI;

/// <summary>BrainThemes: built-in themes, the gallery, the user's own themes and share codes.</summary>
internal static class Themes
{
    static ThemeDef T(string id, string name, bool dark, string bg, string panel, string panel2, string ink, string muted, string dim, string pill, string pillH, string accent, string? accent2 = null) =>
        new() { Id = id, Name = name, Dark = dark, Bg = bg, Panel = panel, Panel2 = panel2, Ink = ink, Muted = muted, Dim = dim, Pill = pill, PillHover = pillH, Accent = accent, Accent2 = accent2 };

    public static readonly ThemeDef[] BuiltIn =
    {
        T("atelier",  "Atelier",  true,  "#0C0C0E", "#141416", "#1B1B1E", "#F3F0EA", "#A19E97", "#6C6A66", "#1F1F22", "#2A2A2E", "#F3F0EA", "#F3F0EA"),
        T("aurora",   "Aurora",   true,  "#08070E", "#12101C", "#1A1728", "#F4F2FF", "#A9A4C2", "#6F6A8A", "#221E33", "#2C2742", "#8B6CFF", "#38D6FF"),
        T("nebula",   "Nebula",   true,  "#0A070F", "#150F1D", "#1D152A", "#FBF2FF", "#B5A3C4", "#77688A", "#291D38", "#352648", "#9B5CFF", "#FF5CA8"),
        T("silk",     "Silk",     true,  "#121317", "#18191D", "#1D1E23", "#F2F3F5", "#A1A3AA", "#6E7077", "#26272C", "#303137", "#F2F3F5"),
        T("obsidian", "Obsidian", true,  "#0A0A0C", "#111114", "#16161A", "#FFFFFF", "#9B9BA3", "#62626A", "#1C1C21", "#26262C", "#FFFFFF"),
        T("ember",    "Ember",    true,  "#17110E", "#1E1713", "#241B16", "#FBF1EA", "#B3A196", "#7A6A5F", "#2E221B", "#3A2B22", "#FF8A5B", "#FF4D8D"),
        T("aqua",     "Aqua",     true,  "#0E1516", "#131C1E", "#182325", "#EAFBF9", "#8FB0AD", "#5C7673", "#1D2C2E", "#26393C", "#5EE0D0", "#5B8CFF"),
        T("violet",   "Violet",   true,  "#131119", "#191622", "#1F1B2A", "#F2EEFF", "#A59CC2", "#6D6585", "#272235", "#322C44", "#A98BFF", "#FF7AD9"),
        T("rose",     "Rose",     true,  "#171116", "#1E161D", "#251B24", "#FFEFF5", "#B79BA8", "#7C6573", "#2F2230", "#3C2B3E", "#FF7AA8", "#FFB36B"),
        T("daylight", "Daylight", false, "#F3F4F6", "#FFFFFF", "#EEF0F3", "#111216", "#5C5F66", "#8C8F96", "#E4E6EA", "#D8DBE0", "#111216"),
    };

    /// <summary>Featured community-style themes that ship with BrainStrap.</summary>
    public static readonly ThemeDef[] Gallery =
    {
        T("g-midnight", "Midnight", true, "#0B0F1A", "#111727", "#161E31", "#E8EEFF", "#8E9AB8", "#5A6582", "#1C2540", "#26304F", "#6EA8FF", "#9B7CFF"),
        T("g-forest",   "Forest",   true, "#0E1410", "#141C16", "#19231B", "#ECF7EE", "#93AE98", "#5E7563", "#1F2C22", "#29392D", "#7BE495", "#3DD6C6"),
        T("g-sunset",   "Sunset",   true, "#1A1013", "#22151A", "#2A1A20", "#FFF0EC", "#C49A9F", "#86656A", "#33202A", "#432A36", "#FFB36B", "#FF5C8A"),
        T("g-gold",     "Royal Gold", true, "#121110", "#1A1815", "#211E1A", "#FAF4E6", "#B5AA92", "#7A715E", "#2A261F", "#363027", "#F5C451"),
        T("g-arctic",   "Arctic",   false, "#EEF4F8", "#FFFFFF", "#E6EEF3", "#0D1B24", "#4E6472", "#86A0AE", "#DDE8EE", "#CEDDE6", "#1B8FD6"),
        T("g-terminal", "Terminal", true, "#050805", "#0A100A", "#0F170F", "#C8FFC8", "#6FAF6F", "#3F6F3F", "#132013", "#1A2E1A", "#39FF6A"),
    };

    /// <summary>Quick accent colours that sit on top of any theme.</summary>
    public static readonly (string Name, string Hex)[] Accents =
    {
        ("Theme default", ""), ("Ember", "#FF8A5B"), ("Aqua", "#5EE0D0"), ("Violet", "#A98BFF"),
        ("Rose", "#FF7AA8"), ("Lime", "#B6F55C"), ("Sky", "#6EB5FF"), ("Gold", "#F5C451"),
    };

    static readonly ThemeDef MinimalDark = T("minimal", "Minimal", true, "#000000", "#0A0A0A", "#111111", "#FFFFFF", "#9A9A9A", "#5E5E5E", "#1A1A1A", "#262626", "#FFFFFF");

    public static IEnumerable<ThemeDef> All(Settings s) => BuiltIn.Concat(Gallery).Concat(s.Studio.CustomThemes);

    public static ThemeDef Get(Settings s, string id) => All(s).FirstOrDefault(t => t.Id == id) ?? BuiltIn[0];

    /// <summary>The theme in effect right now: Minimal mode, then the active profile's theme, then the chosen theme.</summary>
    public static ThemeDef Effective(Settings s)
    {
        if (s.Studio.Minimal) return MinimalDark;
        var pt = s.Current.Theme;
        return pt != null && All(s).Any(t => t.Id == pt) ? Get(s, pt) : Get(s, s.Theme);
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static void Put(ResourceDictionary r, string key, Color c) => r[key] = new SolidColorBrush(c);

    public static bool IsHex(string? h) => h != null && Regex.IsMatch(h, "^#[0-9A-Fa-f]{6}$");

    static Color A(Color c, double alpha) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static Brush Grad(Point from, Point to, params (Color c, double at)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        foreach (var (c, at) in stops) g.GradientStops.Add(new GradientStop(c, at));
        g.Freeze();
        return g;
    }

    /// <summary>The bundled Manrope, then Windows' own font if a glyph is missing.</summary>
    public static FontFamily DefaultFont => new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Manrope, Segoe UI Variable Display, Segoe UI");

    /// <summary>The two colours of the accent gradient in effect.</summary>
    public static (Color a, Color b) Accents2(Settings s)
    {
        var t = Effective(s);
        if (!s.Studio.Minimal && IsHex(s.Accent))
        {
            // a hand-picked accent: pair it with a lighter version of itself
            var c = C(s.Accent!);
            return (c, Mix(c, C(t.Ink), 0.45));
        }
        var a = C(t.Accent);
        return (a, IsHex(t.Accent2) ? C(t.Accent2!) : Mix(a, C(t.Dark ? t.Muted : t.Dim), 0.35));
    }

    public static void Apply(Settings s)
    {
        var t = Effective(s);
        var r = Application.Current.Resources;
        Color bg = C(t.Bg), panel = C(t.Panel), ink = C(t.Ink);
        var glass = s.Studio.Background != "none" && !s.Studio.Minimal;   // see-through surfaces over the live backdrop
        var (accent, accent2) = Accents2(s);
        var mid = Mix(accent, accent2, 0.5);
        var lum = (0.299 * mid.R + 0.587 * mid.G + 0.114 * mid.B) / 255.0;

        Put(r, "Bg", glass && t.Dark ? Mix(bg, Colors.Black, 0.25) : bg);
        Put(r, "Panel", glass ? A(panel, 0.72) : panel);
        Put(r, "Panel2", C(t.Panel2));
        Put(r, "Ink", ink);
        Put(r, "Muted", C(t.Muted));
        Put(r, "Dim", C(t.Dim));
        Put(r, "Line", A(ink, t.Dark ? 0.08 : 0.10));
        Put(r, "Line2", A(ink, t.Dark ? 0.15 : 0.18));
        // controls are frosted (a veil of the text colour) so the backdrop shows through them
        Put(r, "Pill", glass ? A(ink, t.Dark ? 0.09 : 0.07) : C(t.Pill));
        Put(r, "PillHover", glass ? A(ink, t.Dark ? 0.16 : 0.12) : C(t.PillHover));
        Put(r, "NavHover", A(ink, 0.06));
        Put(r, "NavActive", A(ink, t.Dark ? 0.11 : 0.09));
        Put(r, "FieldBrush", glass ? (t.Dark ? A(Colors.Black, 0.34) : A(Colors.White, 0.72)) : C(t.Panel2));
        Put(r, "SidebarBrush", glass ? A(Mix(bg, panel, 0.5), t.Dark ? 0.62 : 0.80) : panel);
        Put(r, "Accent", accent);
        Put(r, "Accent2", accent2);
        // the signature gradient: buttons, switches, the selected page, progress
        r["AccentGrad"] = Grad(new Point(0, 0), new Point(1, 1), (accent, 0), (accent2, 1));
        r["AccentGradH"] = Grad(new Point(0, 0), new Point(1, 0), (accent, 0), (accent2, 1));
        r["AccentFade"] = Grad(new Point(0, 0), new Point(0, 1), (A(mid, 0.34), 0), (A(mid, 0), 1));
        Put(r, "AccentInk", lum > 0.6 ? C("#111111") : C("#FFFFFF"));
        Put(r, "AccentSoft", A(accent, 0.13));
        Put(r, "AccentLine", A(accent, 0.45));
        r["GlowColor"] = accent;

        var top = Mix(panel, ink, 0.04);
        r["CardBrush"] = glass
            ? Grad(new Point(0, 0), new Point(0, 1), (A(top, t.Dark ? 0.80 : 0.86), 0), (A(panel, t.Dark ? 0.66 : 0.78), 1))
            : Grad(new Point(0, 0), new Point(0, 1), (top, 0), (panel, 1));
        r["CardLine"] = Grad(new Point(0, 0), new Point(1, 1), (A(ink, t.Dark ? 0.19 : 0.16), 0), (A(ink, 0.05), 1));
        r["CardLineHover"] = Grad(new Point(0, 0), new Point(1, 1), (A(Mix(ink, accent, 0.5), 0.42), 0), (A(ink, 0.10), 1));
        r["HeroBrush"] = Grad(new Point(0, 0), new Point(1, 1), (A(Mix(panel, accent, 0.42), glass ? 0.80 : 1), 0), (A(panel, glass ? 0.64 : 1), 0.52), (A(Mix(panel, accent2, 0.30), glass ? 0.74 : 1), 1));
        r["HeroLine"] = Grad(new Point(0, 0), new Point(1, 1), (A(accent, 0.75), 0), (A(ink, 0.10), 0.5), (A(accent2, 0.55), 1));

        // Custom Fonts: any font installed on this PC; otherwise the bundled Manrope
        var font = s.Studio.Font;
        r["UiFont"] = !string.IsNullOrWhiteSpace(font) && Fonts.SystemFontFamilies.Any(f => f.Source == font) ? new FontFamily(font) : DefaultFont;
    }

    // ── share codes ──────────────────────────────────────────────
    // BST1.<base64url of ["name", dark, bg, panel, panel2, ink, muted, dim, pill, pillHover, accent]>
    // The website's theme builder makes the same codes. A code only holds colours and a name, so it can't carry anything harmful.

    public static string ToCode(ThemeDef t)
    {
        var arr = new object[] { t.Name, t.Dark ? 1 : 0, t.Bg, t.Panel, t.Panel2, t.Ink, t.Muted, t.Dim, t.Pill, t.PillHover, t.Accent };
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(arr))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "BST1." + b64;
    }

    public static ThemeDef? FromCode(string code)
    {
        try
        {
            code = code.Trim();
            if (!code.StartsWith("BST1.") || code.Length > 2000) return null;
            var b64 = code[5..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            var a = doc.RootElement;
            if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 11) return null;
            string S(int i) => a[i].GetString() ?? "";
            var name = Regex.Replace(S(0), @"[^\w \-\.']", "").Trim();
            var colors = Enumerable.Range(2, 9).Select(S).ToArray();
            if (name.Length == 0 || name.Length > 32 || !colors.All(IsHex)) return null;
            var t = T("c-" + Guid.NewGuid().ToString("N")[..8], name, a[1].GetInt32() == 1,
                colors[0], colors[1], colors[2], colors[3], colors[4], colors[5], colors[6], colors[7], colors[8]);
            return t;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Theme Marketplace: reads a JSON list of share codes from an https address the user chose. Only valid codes are kept.</summary>
    public static async Task<List<ThemeDef>> FetchGalleryAsync(string url)
    {
        var list = new List<ThemeDef>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https") return list;
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(8) };
            var text = await http.GetStringAsync(u);
            if (text.Length > 512 * 1024) return list;
            using var doc = JsonDocument.Parse(text);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("themes");
            foreach (var it in items.EnumerateArray().Take(200))
            {
                var code = it.ValueKind == JsonValueKind.String ? it.GetString() : it.TryGetProperty("code", out var c) ? c.GetString() : null;
                if (code != null && FromCode(code) is { } t) list.Add(t);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("theme gallery: " + ex.GetType().Name);
        }
        return list;
    }
}

internal static class Dwm
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Rounded window corners on Windows 11 (ignored on Windows 10).</summary>
    public static void Round(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            var round = 2;                                  // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        }
        catch
        {
        }
    }

    public static void ApplyTitleBar(Window w, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            var v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref v, sizeof(int));
        }
        catch
        {
        }
    }
}
