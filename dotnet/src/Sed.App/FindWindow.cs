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
/// `Q_SECTORS` / `Q_SURFS` / `Q_THINGS` dialogs). The Quick tab filters by
/// material/name/template/index plus a flag mask; the Fields tab is the
/// original's per-field query builder — a comparison operator and value per
/// field (material, adjoin sector/surface, each flag word, geo/light/tex, …),
/// all ANDed. Picking a result selects it and frames the camera on it;
/// "Select all" adds every match to the shared selection.
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
    private readonly StackPanel _fieldRows;
    private readonly ListBox _list;
    private readonly TextBlock _summary;

    // Per-kind field rows for the Fields tab.
    private List<FieldRow> _rows = new();

    private sealed class FieldRow
    {
        public FindField Field;
        public ComboBox Op = new();
        public TextBox Value = new();
    }

    private Level _level;
    private List<FindResult> _results = new();

    public FindWindow(Level level)
    {
        _level = level;
        Title = "Find";
        Width = 560;
        Height = 520;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _kind = new ComboBox
        {
            ItemsSource = new[] { "Sectors", "Surfaces", "Things", "Lights" },
            SelectedIndex = 1,
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 110,
        };
        _kind.SelectionChanged += (_, _) => { RebuildFieldRows(); Search(); };

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

        _fieldRows = new StackPanel { Margin = new Thickness(10, 4) };
        var fieldsScroll = new ScrollViewer { Content = _fieldRows, MaxHeight = 240 };
        var fieldsHint = new TextBlock
        {
            Text = "Per-field criteria — choose an operator and enter a value; every active row must match. " +
                   "For flags and numbers, SET / NOT SET is a bitmask test.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(10, 2, 10, 0),
        };
        var fieldsPanel = new StackPanel();
        fieldsPanel.Children.Add(fieldsHint);
        fieldsPanel.Children.Add(fieldsScroll);

        var tabs = new TabControl
        {
            Margin = new Thickness(0, 0, 0, 4),
            ItemsSource = new[]
            {
                new { Header = "Quick", Content = (object)query },
                new { Header = "Fields", Content = (object)fieldsPanel },
            },
        };
        tabs.SelectionChanged += (_, _) => Search();
        var tabTemplate = new FuncDataTemplate<object>((item, _) =>
            item is { } i && i.GetType().GetProperty("Content") is { } p
                ? (Control)(p.GetValue(i) ?? new TextBlock())
                : new TextBlock());
        tabs.ItemTemplate = tabTemplate;

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
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);
        DockPanel.SetDock(selectAll, Dock.Bottom);
        root.Children.Add(selectAll);
        root.Children.Add(_list);
        Content = root;

        RebuildFieldRows();
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

        var kind = Kind;

        var criteria = new List<FieldCriterion>();
        string? badValue = null;
        foreach (var row in _rows)
        {
            var op = (CompareOp)row.Op.SelectedIndex;
            if (op == CompareOp.None) continue;
            var text = row.Value.Text?.Trim() ?? string.Empty;
            if (text.Length == 0) continue;

            var criterion = new FieldCriterion { Field = row.Field, Op = op, Text = text };
            var valueKind = FieldCriteria.KindOf(row.Field);
            switch (valueKind)
            {
                case FieldValueKind.Integer:
                case FieldValueKind.Flags:
                    if (TryParseLong(text, out var longValue)) criterion.Long = longValue;
                    else { badValue = $"'{text}' is not a valid integer for {FieldCriteria.Label(row.Field)}."; }
                    break;
                case FieldValueKind.Double:
                    if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var dbl))
                        criterion.Number = dbl;
                    else { badValue = $"'{text}' is not a valid number for {FieldCriteria.Label(row.Field)}."; }
                    break;
                case FieldValueKind.Color:
                    if (FieldCriteria.TryParseColor(text, out var color)) criterion.Color = color;
                    else { badValue = $"'{text}' is not a color — try e.g. 1 0 0 or 255 0 0."; }
                    break;
            }
            if (badValue is not null) break;
            criteria.Add(criterion);
        }

        _results = badFlags || badValue is not null
            ? new List<FindResult>()
            : LevelQuery.Run(_level, new FindQuery
            {
                Kind = kind,
                Text = _text.Text ?? string.Empty,
                FlagMask = mask,
                Fields = criteria,
            });

        _list.ItemsSource = _results;
        _summary.Text = badFlags
            ? $"'{flagText}' is not a hex number — try e.g. 200 or 0x200."
            : badValue ?? (_results.Count == 0
                ? "No matches."
                : $"{_results.Count} match(es) — click one to jump to it.");
    }

    private FindKind Kind => _kind.SelectedIndex switch
    {
        0 => FindKind.Sector,
        2 => FindKind.Thing,
        3 => FindKind.Light,
        _ => FindKind.Surface,
    };

    /// <summary>Rebuilds the Fields tab rows for the current kind.</summary>
    private void RebuildFieldRows()
    {
        _fieldRows.Children.Clear();
        _rows = new List<FieldRow>();

        foreach (var field in FieldCriteria.Fields[Kind])
        {
            var row = new FieldRow { Field = field };
            var valueKind = FieldCriteria.KindOf(field);

            var ops = FieldCriteria.Operators(valueKind);
            row.Op.ItemsSource = ops.Select(o => o switch
            {
                CompareOp.None => "—",
                CompareOp.Equal => "=",
                CompareOp.NotEqual => "<>",
                CompareOp.Above => ">",
                CompareOp.Below => "<",
                CompareOp.Contains => valueKind == FieldValueKind.String ? "contains" : "set",
                CompareOp.NotContains => valueKind == FieldValueKind.String ? "doesn't contain" : "not set",
                _ => o.ToString(),
            }).ToList();
            row.Op.SelectedIndex = 0;
            row.Op.MinWidth = 110;
            row.Op.Margin = new Thickness(0, 1);

            row.Value.Width = 190;
            row.Value.Margin = new Thickness(4, 1, 0, 0);
            var captured = row;
            row.Op.SelectionChanged += (_, _) => Search();
            row.Value.TextChanged += (_, _) => Search();
            row.Value.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };

            var name = new TextBlock
            {
                Text = FieldCriteria.Label(field),
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            };

            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1) };
            line.Children.Add(name);
            line.Children.Add(row.Op);
            line.Children.Add(row.Value);
            _fieldRows.Children.Add(line);
            _rows.Add(captured);
        }
    }

    private static bool TryParseFlags(string text, out long value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return long.TryParse(text, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseLong(string text, out long value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            return long.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        return long.TryParse(text, System.Globalization.NumberStyles.Integer,
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
