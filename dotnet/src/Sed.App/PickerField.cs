using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Sed.Core.Model;
using Sed.Core.Query;

namespace Sed.App;

/// <summary>
/// Builds "text box + browse button" editors for fields that name something —
/// an asset in the archives, or an object in the level. The text box stays
/// editable so a value the catalog doesn't know about can still be typed.
/// </summary>
public static class PickerField
{
    /// <summary>
    /// A field whose candidates are produced lazily. The list is only built when
    /// the button is pressed — enumerating ~2,000 materials for every visible
    /// row would make the inspectors crawl.
    /// </summary>
    public static Control Build(Window? owner, string title, string value,
        Func<IReadOnlyList<PickerItem>> candidates, Action<string> onCommit)
    {
        var box = InspectorPanel.TextField(value, onCommit);

        var browse = new Button
        {
            Content = "…",
            FontSize = 11,
            Padding = new Thickness(6, 1),
            Margin = new Thickness(4, 0, 0, 0),
        };

        browse.Click += async (_, _) =>
        {
            if (owner is null) return;

            var items = candidates();
            var picked = await new PickerDialog(title, items, box.Text)
                .ShowDialog<string?>(owner);

            if (picked is null) return;      // cancelled
            box.Text = picked;
            onCommit(picked);
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(browse);
        return row;
    }

    /// <summary>Assets of one extension from the open archives.</summary>
    public static IReadOnlyList<PickerItem> Assets(AssetCatalog? catalog, string extension) =>
        catalog is null
            ? Array.Empty<PickerItem>()
            : catalog.ByExtension(extension).Select(n => new PickerItem(n)).ToList();

    /// <summary>The level's templates, by name.</summary>
    public static IReadOnlyList<PickerItem> Templates(Level level) =>
        level.Templates.Values
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new PickerItem(t.Name, t.Parent.Length > 0 ? $"{t.Name}   ({t.Parent})" : t.Name))
            .ToList();

    /// <summary>
    /// Level objects addressed by index — the form COG symbols and thing/sector
    /// fields store. Labels come from <see cref="LevelQuery"/> so a picker shows
    /// the same descriptions as Find.
    /// </summary>
    public static IReadOnlyList<PickerItem> LevelObjects(Level level, FindKind kind) =>
        LevelQuery.Run(level, new FindQuery { Kind = kind })
            .Select(r => new PickerItem(r.Index.ToString(), r.Label))
            .ToList();

    /// <summary>Colormaps declared by the level (for a sector's colormap field).</summary>
    public static IReadOnlyList<PickerItem> Colormaps(Level level) =>
        level.ColorMaps.Select((c, i) => new PickerItem(i.ToString(), $"{i}: {c}")).ToList();
}
