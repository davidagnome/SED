using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Sed.App;

/// <summary>Minimal modal helpers — Avalonia's core has no MessageBox.</summary>
public static class Dialogs
{
    /// <summary>Yes/No confirmation, returning the choice.</summary>
    public static Task<bool> Confirm(Window owner, string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var window = new Window
        {
            Title = title,
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22)),
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var yes = new Button { Content = "Yes", Margin = new Thickness(0, 0, 6, 0) };
        yes.Click += (_, _) => { tcs.TrySetResult(true); window.Close(); };
        var no = new Button { Content = "No" };
        no.Click += (_, _) => { tcs.TrySetResult(false); window.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(text);
        window.Content = root;

        window.Opened += (_, _) => no.Focus();
        window.ShowDialog(owner);
        return tcs.Task;
    }
}
