using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Sed.App;

/// <summary>One choice in a picker: the value that gets written, plus a label.</summary>
public sealed record PickerItem(string Value, string Label)
{
    public PickerItem(string value) : this(value, value) { }
}

/// <summary>
/// A filterable list dialog for choosing an asset name or a level object. Returns
/// the chosen value, or null if cancelled.
///
/// Lists can be large (a retail install has ~2,000 materials), so the filter is
/// the primary way in and the list is capped for rendering.
/// </summary>
public sealed class PickerDialog : Window
{
    private const int MaxShown = 500;

    private readonly IReadOnlyList<PickerItem> _items;
    private readonly TextBox _filter;
    private readonly ListBox _list;
    private readonly TextBlock _summary;

    public PickerDialog(string title, IReadOnlyList<PickerItem> items, string? current)
    {
        _items = items;

        Title = title;
        Width = 420;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _filter = new TextBox { PlaceholderText = "Filter…", Margin = new Thickness(8, 8, 8, 4) };
        _filter.TextChanged += (_, _) => Populate();
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Accept();
            else if (e.Key == Key.Escape) Close(null);
        };

        _summary = new TextBlock { Margin = new Thickness(8, 0, 8, 4), Foreground = Brushes.Gray, FontSize = 11 };

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<PickerItem>((item, _) =>
                new TextBlock { Text = item?.Label, FontSize = 12, Margin = new Thickness(4, 2) }),
        };
        _list.DoubleTapped += (_, _) => Accept();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); };

        var ok = new Button { Content = "Choose", Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        var clear = new Button { Content = "Clear", Margin = new Thickness(0, 0, 6, 0) };
        clear.Click += (_, _) => Close(string.Empty);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8),
        };
        buttons.Children.Add(clear);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(_filter, Dock.Top);
        root.Children.Add(_filter);
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = root;

        Populate();

        // Start on the current value so re-opening a field lands where you were.
        if (!string.IsNullOrEmpty(current))
        {
            var match = items.FirstOrDefault(i =>
                string.Equals(i.Value, current, StringComparison.OrdinalIgnoreCase));
            if (match is not null) _list.SelectedItem = match;
        }

        Opened += (_, _) => _filter.Focus();
    }

    private void Populate()
    {
        var query = _filter.Text?.Trim() ?? string.Empty;
        var matches = query.Length == 0
            ? _items
            : _items.Where(i => i.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        _list.ItemsSource = matches.Count > MaxShown ? matches.Take(MaxShown).ToList() : matches;
        _summary.Text = matches.Count > MaxShown
            ? $"{matches.Count} matches — showing the first {MaxShown}, keep typing to narrow"
            : $"{matches.Count} of {_items.Count}";
    }

    private void Accept()
    {
        if (_list.SelectedItem is PickerItem item) Close(item.Value);
        else if (_list.ItemCount == 1 && _list.ItemsSource?.Cast<PickerItem>().FirstOrDefault() is { } only)
            Close(only.Value);
    }
}
