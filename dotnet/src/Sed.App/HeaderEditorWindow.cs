using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.App;

/// <summary>
/// Editor for the level header — gravity, the two sky descriptions, mipmap/LOD
/// distances, perspective/gouraud cutoffs and fog (mirrors `U_LHEADER.PAS`).
/// The HEADER section already parses and writes faithfully; this exposes it.
/// Every field commits through an <see cref="IEditCommand"/>, so header edits
/// share the editor's undo stack with everything else.
/// </summary>
public sealed class HeaderEditorWindow : Window
{
    private readonly EditHistory _history;
    private readonly StackPanel _fields;
    private LevelHeader _header;

    public HeaderEditorWindow(LevelHeader header, EditHistory history)
    {
        _header = header;
        _history = history;

        Title = "Level Header";
        Width = 340;
        Height = 620;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _fields = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4) };
        Content = new ScrollViewer { Content = _fields };

        Rebuild();
    }

    /// <summary>Points the window at another level's header (after File ▸ Open).</summary>
    public void SetHeader(LevelHeader header)
    {
        _header = header;
        Rebuild();
    }

    /// <summary>
    /// Rebuilds every field from the model. Called after each edit so an undo
    /// (or a change made elsewhere) is reflected rather than leaving stale text.
    /// </summary>
    public void Rebuild()
    {
        _fields.Children.Clear();
        var h = _header;

        Section("World");
        Num("Gravity", h.Gravity, v =>
            HeaderField.Set(h, "gravity", (float)v, x => x.Gravity, (x, val) => x.Gravity = val));

        Section("Ceiling sky");
        Num("Height", h.CeilingSky.Height, v =>
            HeaderField.Set(h, "ceiling sky height", (float)v,
                x => x.CeilingSky.Height, (x, val) => x.CeilingSky.Height = val));
        // Vec2 is an immutable record struct, so offsets are replaced whole.
        Num("Offset X", h.CeilingSky.Offset.X, v =>
            HeaderField.Set(h, "ceiling sky offset X", new Vec2(v, h.CeilingSky.Offset.Y),
                x => x.CeilingSky.Offset, (x, val) => x.CeilingSky.Offset = val));
        Num("Offset Y", h.CeilingSky.Offset.Y, v =>
            HeaderField.Set(h, "ceiling sky offset Y", new Vec2(h.CeilingSky.Offset.X, v),
                x => x.CeilingSky.Offset, (x, val) => x.CeilingSky.Offset = val));

        Section("Horizon sky");
        Num("Distance", h.HorizonSky.Distance, v =>
            HeaderField.Set(h, "horizon distance", (float)v,
                x => x.HorizonSky.Distance, (x, val) => x.HorizonSky.Distance = val));
        Num("Px per rev", h.HorizonSky.PixelsPerRev, v =>
            HeaderField.Set(h, "pixels per rev", (float)v,
                x => x.HorizonSky.PixelsPerRev, (x, val) => x.HorizonSky.PixelsPerRev = val));
        Num("Offset X", h.HorizonSky.Offset.X, v =>
            HeaderField.Set(h, "horizon offset X", new Vec2(v, h.HorizonSky.Offset.Y),
                x => x.HorizonSky.Offset, (x, val) => x.HorizonSky.Offset = val));
        Num("Offset Y", h.HorizonSky.Offset.Y, v =>
            HeaderField.Set(h, "horizon offset Y", new Vec2(h.HorizonSky.Offset.X, v),
                x => x.HorizonSky.Offset, (x, val) => x.HorizonSky.Offset = val));

        Section("Mipmap distances");
        for (int i = 0; i < h.MipmapDistances.Length; i++)
        {
            int index = i;
            Num($"Mipmap {i}", h.MipmapDistances[i], v =>
                HeaderField.Set(h, $"mipmap distance {index}", (float)v,
                    x => x.MipmapDistances[index], (x, val) => x.MipmapDistances[index] = val));
        }

        Section("LOD distances");
        for (int i = 0; i < h.LodDistances.Length; i++)
        {
            int index = i;
            Num($"LOD {i}", h.LodDistances[i], v =>
                HeaderField.Set(h, $"LOD distance {index}", (float)v,
                    x => x.LodDistances[index], (x, val) => x.LodDistances[index] = val));
        }

        Section("Rendering");
        Num("Perspective", h.PerspectiveDistance, v =>
            HeaderField.Set(h, "perspective distance", (float)v,
                x => x.PerspectiveDistance, (x, val) => x.PerspectiveDistance = val));
        Num("Gouraud", h.GouraudDistance, v =>
            HeaderField.Set(h, "gouraud distance", (float)v,
                x => x.GouraudDistance, (x, val) => x.GouraudDistance = val));

        Section("Fog");
        var enabled = InspectorPanel.CheckField("Enabled", h.Fog.Enabled, on =>
            Commit(HeaderField.Set(h, "fog enabled", on, x => x.Fog.Enabled, (x, val) => x.Fog.Enabled = val)));
        enabled.Margin = new Thickness(4, 2);
        _fields.Children.Add(enabled);

        Num("Colour R", h.Fog.Color.R, v =>
            HeaderField.Set(h, "fog red", WithR(h.Fog.Color, (float)v),
                x => x.Fog.Color, (x, val) => x.Fog.Color = val));
        Num("Colour G", h.Fog.Color.G, v =>
            HeaderField.Set(h, "fog green", WithG(h.Fog.Color, (float)v),
                x => x.Fog.Color, (x, val) => x.Fog.Color = val));
        Num("Colour B", h.Fog.Color.B, v =>
            HeaderField.Set(h, "fog blue", WithB(h.Fog.Color, (float)v),
                x => x.Fog.Color, (x, val) => x.Fog.Color = val));
        Num("Start", h.Fog.Start, v =>
            HeaderField.Set(h, "fog start", v, x => x.Fog.Start, (x, val) => x.Fog.Start = val));
        Num("End", h.Fog.End, v =>
            HeaderField.Set(h, "fog end", v, x => x.Fog.End, (x, val) => x.Fog.End = val));
    }

    private static ColorF WithR(ColorF c, float r) => new(r, c.G, c.B);
    private static ColorF WithG(ColorF c, float g) => new(c.R, g, c.B);
    private static ColorF WithB(ColorF c, float b) => new(c.R, c.G, b);

    private void Section(string title) =>
        _fields.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(4, 10, 4, 4),
        });

    /// <summary>Adds a numeric row whose commit builds and runs an edit command.</summary>
    private void Num(string label, double value, Func<double, IEditCommand> build) =>
        _fields.Children.Add(InspectorPanel.Row(label,
            InspectorPanel.NumericField(value, v => Commit(build(v)))));

    private void Commit(IEditCommand command)
    {
        _history.Do(command);
        // Re-read from the model: a field may be clamped, and undo elsewhere
        // should not leave this window showing values the level no longer has.
        Rebuild();
    }
}
