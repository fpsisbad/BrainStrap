using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainStrap.UI;

/// <summary>A tiny "type a name" dialog that follows the current theme.</summary>
internal static class Prompt
{
    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var app = Application.Current;
        var win = new Window
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 400,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            FontFamily = (FontFamily)app.FindResource("UiFont"),
            FontSize = 14,
        };
        win.SetResourceReference(Control.BackgroundProperty, "Bg");
        win.SetResourceReference(Control.ForegroundProperty, "Ink");
        win.SourceInitialized += (_, _) => Dwm.ApplyTitleBar(win, true);

        var box = new TextBox
        {
            Text = initial,
            MaxLength = 32,
            Style = (Style)app.FindResource("Field"),
            Margin = new Thickness(0, 10, 0, 18),
        };
        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)app.FindResource("PillButton") };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)app.FindResource("GhostButton") };

        string? result = null;
        ok.Click += (_, _) => { result = box.Text.Trim(); win.DialogResult = true; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        win.Content = panel;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        return win.ShowDialog() == true && !string.IsNullOrWhiteSpace(result) ? result : null;
    }
}
