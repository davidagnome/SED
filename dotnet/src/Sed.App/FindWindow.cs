using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Sed.Core.Model;
using Sed.Core.Query;

namespace Sed.App;

/// <summary>
/// Find / jump-to for sectors, surfaces, things and lights (the original's
/// `Q_SECTORS` / `Q_SURFS` / `Q_THINGS` dialogs). Type to filter by material,
/// name, template or index; optionally require flag bits. Picking a result
/// selects it and frames the camera on it; "Select all" adds every match to the
/// shared selection so a whole category can be edited at once.
/// </summary>
public sealed class FindWindow : Window
{
    /// <summary>Raised when a single result is picked — select it and jump to it.</summary>
    public Action<FindResult>? ResultChosen;

    /// <summary>Raised by "Select all" with every current match.</summary>
    public Action<IReadOnlyList<FindResult>>? ResultsSelected;

    private readonly ComboBox _kind;
    private readonly TextBox _text;
    private readonly TextBox _flags;
    private readonly ListBox _list;
    private readonly TextBlock _summary;

    private Level _level;
    private List<FindResult> _results = new();

    public FindWindow(Level level)
    {
        _level = level;
        Title = "Find";
        Width = 520;
        Height = 480;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _kind = new ComboBox
        {
            ItemsSource = new[] { "Sectors", "Surfaces", "Things", "Lights" },
            SelectedIndex = 1,
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 110,
        };
        _kind.SelectionChanged += (_, _) => Search();

        _text = new TextBox { PlaceholderText = "material / name / template / index", Width = 220 };
        _text.TextChanged += (_, _) => Search();
        _text.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };

        _flags = new TextBox { PlaceholderText = "flags (hex)", Width = 100, Margin = new Thickness(6, 0, 0, 0) };
        _flags.TextChanged += (_, _) => Search();

        var query = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8),
        };
        query.Children.Add(_kind);
        query.Children.Add(_text);
        query.Children.Add(_flags);

        _summary = new TextBlock
        {
            Margin = new Thickness(10, 0, 10, 6),
            Foreground = Brushes.Gray,
            FontSize = 11,
        };

        _list = new ListBox { Background = Brushes.Transparent, ItemTemplate = RowTemplate() };
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is FindResult r) ResultChosen?.Invoke(r);
        };

        var selectAll = new Button { Content = "Select all matches", Margin = new Thickness(10, 6) };
        selectAll.Click += (_, _) =>
        {
            if (_results.Count > 0) ResultsSelected?.Invoke(_results);
        };

        var root = new DockPanel();
        DockPanel.SetDock(query, Dock.Top);
        root.Children.Add(query);
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);
        DockPanel.SetDock(selectAll, Dock.Bottom);
        root.Children.Add(selectAll);
        root.Children.Add(_list);
        Content = root;

        Search();
    }

    /// <summary>Points the dialog at a different level (after File ▸ Open).</summary>
    public void SetLevel(Level level)
    {
        _level = level;
        Search();
    }

    /// <summary>Re-runs the current query — also call after edits change the level.</summary>
    public void Search()
    {
        long mask = 0;
        var flagText = _flags.Text?.Trim() ?? string.Empty;
        bool badFlags = flagText.Length > 0 && !TryParseFlags(flagText, out mask);

        var kind = _kind.SelectedIndex switch
        {
            0 => FindKind.Sector,
            2 => FindKind.Thing,
            3 => FindKind.Light,
            _ => FindKind.Surface,
        };

        _results = badFlags
            ? new List<FindResult>()
            : LevelQuery.Run(_level, new FindQuery
            {
                Kind = kind,
                Text = _text.Text ?? string.Empty,
                FlagMask = mask,
            });

        _list.ItemsSource = _results;
        _summary.Text = badFlags
            ? $"'{flagText}' is not a hex number — try e.g. 200 or 0x200."
            : _results.Count == 0
                ? "No matches."
                : $"{_results.Count} match(es) — click one to jump to it.";
    }

    private static bool TryParseFlags(string text, out long value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return long.TryParse(text, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static IDataTemplate RowTemplate() =>
        new FuncDataTemplate<FindResult>((result, _) => new TextBlock
        {
            Text = result?.Label,
            FontSize = 12,
            Margin = new Thickness(4, 3),
        });
}
