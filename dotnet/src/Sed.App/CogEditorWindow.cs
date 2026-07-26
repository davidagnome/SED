using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Sed.Core.Editing;
using Sed.Core.Model;
using Sed.Formats.Cogs;

namespace Sed.App;

/// <summary>
/// Edits the level's placed COGs (`U_COGFORM.PAS`). A placed COG in the JKL is a
/// script name plus a list of bare positional values — meaningless on their own.
/// This resolves the script from the game archives and labels each value with the
/// symbol it feeds, so you edit "door0 (thing)" rather than "value 0".
///
/// When the script cannot be found — a level may reference one that isn't in the
/// configured install — the values are still editable positionally rather than
/// the window refusing to show anything.
/// </summary>
public sealed class CogEditorWindow : Window
{
    private readonly EditHistory _history;
    private readonly CogScriptLibrary? _scripts;

    private readonly ListBox _list;
    private readonly StackPanel _detail;
    private readonly TextBlock _summary;

    private Level _level;
    private Cog? _selected;

    public CogEditorWindow(Level level, EditHistory history, CogScriptLibrary? scripts)
    {
        _level = level;
        _history = history;
        _scripts = scripts;

        Title = "COGs";
        Width = 760;
        Height = 560;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _list = new ListBox { Background = Brushes.Transparent };
        _list.SelectionChanged += (_, _) =>
        {
            _selected = _list.SelectedItem as Cog;
            BuildDetail();
        };

        _summary = new TextBlock { Margin = new Thickness(6, 6, 6, 4), Foreground = Brushes.Gray, FontSize = 11 };

        var addBtn = SmallButton("Add", AddCog);
        var deleteBtn = SmallButton("Delete", DeleteCog);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 2, 6, 6),
            Spacing = 4,
        };
        buttons.Children.Add(addBtn);
        buttons.Children.Add(deleteBtn);

        var left = new DockPanel { Width = 260 };
        DockPanel.SetDock(_summary, Dock.Top);
        left.Children.Add(_summary);
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(_list);

        _detail = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };

        var root = new DockPanel();
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3c)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = left,
        };
        DockPanel.SetDock(border, Dock.Left);
        root.Children.Add(border);
        root.Children.Add(new ScrollViewer { Content = _detail });
        Content = root;

        Refresh();
    }

    public void SetLevel(Level level)
    {
        _level = level;
        _selected = null;
        Refresh();
    }

    /// <summary>Rebuilds both panes — call after an edit or an undo.</summary>
    public void Refresh()
    {
        var items = _level.Cogs.ToList();
        _list.ItemsSource = items;
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Cog>((c, _) =>
            new TextBlock
            {
                Text = c is null ? string.Empty : $"{c.Num}: {c.Name}",
                FontSize = 12,
                Margin = new Thickness(4, 2),
            });

        if (_selected is not null && items.Contains(_selected)) _list.SelectedItem = _selected;
        else if (_selected is not null) _selected = null;

        int unresolved = _scripts is null
            ? items.Count
            : items.Count(c => _scripts.Get(c.Name) is null);

        _summary.Text = _scripts is null
            ? $"{items.Count} placed COG(s) — no game archives open, so scripts cannot be resolved."
            : $"{items.Count} placed COG(s), {unresolved} script(s) unresolved";

        BuildDetail();
    }

    private void BuildDetail()
    {
        _detail.Children.Clear();
        if (_selected is not { } cog)
        {
            _detail.Children.Add(Note("Select a COG."));
            return;
        }

        Heading($"{cog.Num}: {cog.Name}");

        _detail.Children.Add(InspectorPanel.Row("Script", InspectorPanel.TextField(cog.Name, text =>
        {
            if (string.Equals(text, cog.Name, StringComparison.OrdinalIgnoreCase)) return;
            // A different script means a different symbol layout, so the old
            // positional values no longer mean anything — start clean.
            Commit(new SetCogScriptCommand(cog, text));
        })));

        var script = _scripts?.Get(cog.Name);
        if (script is null)
        {
            _detail.Children.Add(Note(_scripts is null
                ? "No game archives open — editing values positionally."
                : $"Script '{cog.Name}' not found in the open archives — editing values positionally."));
            BuildPositional(cog);
            return;
        }

        var symbols = script.LevelValues;
        if (symbols.Count == 0)
        {
            _detail.Children.Add(Note("This script takes no values from the level."));
            if (cog.Values.Count > 0) BuildPositional(cog);
            return;
        }

        Heading("Values");
        for (int i = 0; i < symbols.Count; i++)
        {
            int index = i;
            var symbol = symbols[i];
            var value = i < cog.Values.Count ? cog.Values[i] : symbol.Default ?? string.Empty;

            var label = $"{symbol.Name}  ({symbol.Type.ToString().ToLowerInvariant()})";
            var row = InspectorPanel.Row(label, InspectorPanel.TextField(value, text =>
                Commit(new SetCogValueCommand(cog, index, text.Trim()))));
            _detail.Children.Add(row);

            if (!string.IsNullOrWhiteSpace(symbol.Description))
                _detail.Children.Add(new TextBlock
                {
                    Text = symbol.Description,
                    FontSize = 10,
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(98, 0, 4, 4),
                    TextWrapping = TextWrapping.Wrap,
                });
        }

        // A level whose value count disagrees with the script is worth surfacing:
        // every later symbol would be reading the wrong value.
        if (cog.Values.Count != symbols.Count)
            _detail.Children.Add(new TextBlock
            {
                Text = $"⚠ This COG stores {cog.Values.Count} value(s) but the script expects {symbols.Count}.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xc0, 0x60)),
                Margin = new Thickness(4, 8),
                TextWrapping = TextWrapping.Wrap,
            });

        BuildSymbolReference(script);
    }

    /// <summary>Fallback editor: numbered rows, used when the script is unknown.</summary>
    private void BuildPositional(Cog cog)
    {
        Heading("Values (positional)");
        for (int i = 0; i < cog.Values.Count; i++)
        {
            int index = i;
            _detail.Children.Add(InspectorPanel.Row($"value {i}",
                InspectorPanel.TextField(cog.Values[i], text =>
                    Commit(new SetCogValueCommand(cog, index, text.Trim())))));
        }

        var add = SmallButton("Add value", () =>
            Commit(new SetCogValueCommand(cog, cog.Values.Count, "0")));
        add.Margin = new Thickness(4, 6);
        _detail.Children.Add(add);
    }

    /// <summary>Read-only listing of the script's messages and local symbols.</summary>
    private void BuildSymbolReference(CogScript script)
    {
        var others = script.Symbols.Where(s => !s.TakesLevelValue).ToList();
        if (others.Count == 0) return;

        Heading("Script symbols (not level-supplied)");
        foreach (var symbol in others)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 1) };
            row.Children.Add(new TextBlock
            {
                Text = symbol.Type.ToString().ToLowerInvariant(),
                Width = 70, FontSize = 10, Foreground = Brushes.DimGray,
            });
            row.Children.Add(new TextBlock
            {
                Text = symbol.Name,
                Width = 140, FontSize = 11, Foreground = Brushes.Gray,
            });
            if (symbol.Local)
                row.Children.Add(new TextBlock { Text = "local", FontSize = 10, Foreground = Brushes.DimGray });
            _detail.Children.Add(row);
        }
    }

    // ---- actions ----

    private void AddCog()
    {
        var cog = new Cog { Name = _selected?.Name ?? "new.cog" };
        var command = new CreateCogCommand(_level, cog);
        _history.Do(command);
        _selected = cog;
        Refresh();
    }

    private void DeleteCog()
    {
        if (_selected is not { } cog) { _summary.Text = "Select a COG to delete."; return; }
        _history.Do(new DeleteCogCommand(_level, cog));
        _selected = null;
        Refresh();
    }

    private void Commit(IEditCommand command)
    {
        _history.Do(command);
        Refresh();
    }

    // ---- view helpers ----

    private void Heading(string text) =>
        _detail.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            FontSize = 12,
            Margin = new Thickness(4, 10, 4, 4),
        });

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = Brushes.Gray,
        FontSize = 11,
        Margin = new Thickness(4, 4),
        TextWrapping = TextWrapping.Wrap,
    };

    private static Button SmallButton(string content, Action onClick)
    {
        var button = new Button { Content = content, FontSize = 11, Padding = new Thickness(6, 1) };
        button.Click += (_, _) => onClick();
        return button;
    }
}
