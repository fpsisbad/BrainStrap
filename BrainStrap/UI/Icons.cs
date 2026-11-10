using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BrainStrap.UI;

/// <summary>BrainStrap's own line icons (drawn on a 24 x 24 grid). They take the colour of the text around them.</summary>
internal static class Icons
{
    static readonly Dictionary<string, string> Data = new()
    {
        ["home"] = "M3 10.5 L12 3 L21 10.5 V20 A1 1 0 0 1 20 21 H15 V15 H9 V21 H4 A1 1 0 0 1 3 20 Z",
        ["spark"] = "M11 3 L12.9 8.1 L18 10 L12.9 11.9 L11 17 L9.1 11.9 L4 10 L9.1 8.1 Z M18.5 14.5 L19.3 16.7 L21.5 17.5 L19.3 18.3 L18.5 20.5 L17.7 18.3 L15.500 17.5 L17.7 16.7 Z",
        ["rocket"] = "M12 2.5 C16 5 17.5 9 17 14 L14.500 16.500 H9.5 L7 14 C6.5 9 8 5 12 2.5 Z M9.5 16.500 L8 21 L12 18.5 L16 21 L14.500 16.500 M12 9 A1.6 1.6 0 1 0 12 12.2 A1.6 1.6 0 1 0 12 9 Z",
        ["play"] = "M7 4.5 L19 12 L7 19.5 Z",
        ["blocks"] = "M4 4 H10.5 V10.5 H4 Z M13.5 4 H20 V10.5 H13.5 Z M4 13.5 H10.5 V20 H4 Z M16.750 13.5 V20 M13.5 16.750 H20",
        ["layers"] = "M12 3 L21 8 L12 13 L3 8 Z M3 12.5 L12 17.5 L21 12.5 M3 16.500 L12 21.5 L21 16.500",
        ["flag"] = "M5 21 V4 M5 4.5 H17 L15 8.500 L17 12.5 H5",
        ["link"] = "M9.5 14.500 L14.500 9.5 M11 6.5 L12.2 5.3 A4.5 4.5 0 0 1 18.7 11.8 L17.5 13 M13 17.5 L11.8 18.7 A4.5 4.5 0 0 1 5.3 12.2 L6.5 11",
        ["bag"] = "M5 8 H19 L18 20.500 H6 Z M9 8 V6.5 A3 3 0 0 1 15 6.5 V8",
        ["flow"] = "M4 4 H10 V10 H4 Z M14 14 H20 V20 H14 Z M10 7 H14 A3 3 0 0 1 17 10 V14",
        ["pulse"] = "M2.5 12 H7 L10 4.5 L14 19.500 L17 12 H21.5",
        ["globe"] = "M12 2.5 A9.5 9.5 0 1 0 12 21.5 A9.5 9.5 0 1 0 12 2.5 Z M2.5 12 H21.5 M12 2.5 C15.200 5.5 15.200 18.5 12 21.5 M12 2.5 C8.800 5.5 8.800 18.5 12 21.5",
        ["drop"] = "M12 3 C12 3 18 9.2 18 14 A6 6 0 0 1 6 14 C6 9.2 12 3 12 3 Z",
        ["sliders"] = "M4 6 H13 M17 6 H20 M4 12 H7 M11 12 H20 M4 18 H13 M17 18 H20 M15 3.5 V8.500 M9 9.5 V14.500 M15 15.500 V20.500",
        ["heart"] = "M12 20.500 C12 20.500 4 15.500 4 9.5 A4.2 4.2 0 0 1 12 7.5 A4.2 4.2 0 0 1 20 9.5 C20 15.500 12 20.500 12 20.500 Z",
        ["user"] = "M12 12 A4 4 0 1 0 12 4 A4 4 0 1 0 12 12 Z M4.5 20.500 A7.5 7.5 0 0 1 19.500 20.500",
        ["info"] = "M12 2.5 A9.5 9.5 0 1 0 12 21.5 A9.5 9.5 0 1 0 12 2.5 Z M12 11 V16.500 M12 7.5 V8",
        ["download"] = "M12 3.5 V15 M7 10.5 L12 15.500 L17 10.5 M4.5 20 H19.500",
        ["check"] = "M4.5 12.5 L10 18 L19.500 7",
        ["shield"] = "M12 2.5 L20 5.5 V11.5 C20 16.500 16.500 20 12 21.5 C7.5 20 4 16.500 4 11.5 V5.5 Z",
        ["bolt"] = "M13 2.5 L4.5 13.5 H11 L10 21.5 L19.500 9.5 H13 Z",
        ["search"] = "M10.5 3.5 A7 7 0 1 0 10.5 17.5 A7 7 0 1 0 10.5 3.5 Z M15.500 15.500 L20.500 20.500",
        ["clock"] = "M12 2.5 A9.5 9.5 0 1 0 12 21.5 A9.5 9.5 0 1 0 12 2.5 Z M12 7 V12 L15.500 14",
        ["cpu"] = "M6.5 6.5 H17.5 V17.5 H6.5 Z M9.5 9.5 H14.500 V14.500 H9.5 Z M9 3 V6.5 M15 3 V6.5 M9 17.5 V21 M15 17.5 V21 M3 9 H6.5 M3 15 H6.5 M17.5 9 H21 M17.5 15 H21",
        ["target"] = "M12 2.5 A9.5 9.5 0 1 0 12 21.5 A9.5 9.5 0 1 0 12 2.5 Z M12 8 A4 4 0 1 0 12 16 A4 4 0 1 0 12 8 Z M12 1 V5 M12 19 V23 M1 12 H5 M19 12 H23",
        ["wand"] = "M5 19 L15.500 8.500 M14 4 L14.800 6.2 L17 7 L14.800 7.800 L14 10 L13.2 7.800 L11 7 L13.2 6.2 Z M19 11 L19.500 12.5 L21 13 L19.500 13.5 L19 15 L18.5 13.5 L17 13 L18.5 12.5 Z",
    };

    /// <summary>An icon that follows the surrounding text colour.</summary>
    public static FrameworkElement Make(string name, double size = 18, double stroke = 1.8, string? brush = null)
    {
        var path = new Path
        {
            StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Width = 24, Height = 24, SnapsToDevicePixels = false,
        };
        try { path.Data = Geometry.Parse(Data.TryGetValue(name, out var d) ? d : Data["spark"]); }
        catch { path.Data = Geometry.Parse("M12 10 A2 2 0 1 0 12 14 A2 2 0 1 0 12 10 Z"); }
        if (brush != null) path.SetResourceReference(Shape.StrokeProperty, brush);
        else path.SetBinding(Shape.StrokeProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self });
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } }, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>An icon inside a soft rounded badge (page headers, tiles).</summary>
    public static Border Badge(string name, double box = 40, string brush = "Ink", string back = "Pill")
    {
        var b = new Border { Width = box, Height = box, CornerRadius = new CornerRadius(box * 0.3), Child = Make(name, box * 0.48, 1.8, brush), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, back);
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        return b;
    }
}
