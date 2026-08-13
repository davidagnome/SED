using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Sed.App;

/// <summary>
/// The command-key remapper (the original's configurable shortcuts): one row
/// per command, showing the semicolon-separated gesture list (e.g.
/// <c>Ctrl+Z</c> or <c>Ctrl+Y; Ctrl+Shift+Z</c>). Saves to the settings and
/// rebuilds the bindings, so menu items and the view's chord handling pick the
/// new gestures up immediately.
/// </summary>
public sealed class KeyBindingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<TextBox> _boxes = new();

    public KeyBindingsWindow(AppSettings settings, CommandKeys bindings)
    {
        _settings = settings;

        Title = "Key Bindings";
        Width = 460;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        var rows = new StackPanel { Margin = new Thickness(12, 10) };
        foreach (var (name, label, _) in CommandKeys.All)
        {
            var current = bindings.Gestures(name);
            var box = new TextBox
            {
                Text = string.Join("; ", current.Select(Friendly)),
                Width = 200,
            };
            _boxes.Add(box);

            var nameText = new TextBlock
            {
                Text = label,
                Width = 200,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2) };
            line.Children.Add(nameText);
            line.Children.Add(box);
            rows.Children.Add(line);
        }

        var hint = new TextBlock
        {
            Text = "One or more gestures per command, separated by ';'. Examples: Ctrl+Z, F9, Shift+F9, Ctrl+Shift+T.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(12, 4, 12, 0),
        };

        var save = new Button { Content = "Save", Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) =>
        {
            for (int i = 0; i < CommandKeys.All.Count; i++)
            {
                var (name, _, _) = CommandKeys.All[i];
                var text = _boxes[i].Text?.Trim() ?? string.Empty;
                _settings.KeyBindings[name] = text;
            }
            _settings.Save();
            Close();
        };
        var defaults = new Button { Content = "Restore defaults", Margin = new Thickness(0, 0, 6, 0) };
        defaults.Click += (_, _) =>
        {
            for (int i = 0; i < CommandKeys.All.Count; i++)
            {
                var (_, _, dflts) = CommandKeys.All[i];
                _boxes[i].Text = string.Join("; ", dflts.Select(Friendly));
            }
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        buttons.Children.Add(defaults);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var scroll = new ScrollViewer { Content = rows };
        root.Children.Add(scroll);
        Content = root;
    }

    /// <summary>Prettier key names for the dialog (OemPlus → '=', Escape → 'Esc').</summary>
    private static string Friendly(KeyGesture g)
    {
        var s = g.ToString();
        s = s.Replace("OemPlus", "=").Replace("OemMinus", "-").Replace("Escape", "Esc");
        return s;
    }
}
