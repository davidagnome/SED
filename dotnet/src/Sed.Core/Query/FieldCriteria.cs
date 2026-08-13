using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Query;

/// <summary>
/// Comparison operators, transcribed from <c>TCompAction</c> in Q_UTILS.PAS:
/// "=" <c>ca_Equal</c>, "&lt;&gt;" <c>ca_NotEqual</c>, "&gt;" <c>ca_Above</c>,
/// "&lt;" <c>ca_Below</c>, "SET"/"CONTAINS" <c>ca_In</c> and
/// "NOT SET"/"DOESN'T CONTAIN" <c>ca_NotIn</c>. For strings, SET is a
/// case-insensitive substring; for flags and integers it is a bitmask test
/// (<c>f1 and f2 &lt;&gt; 0</c>).
/// </summary>
public enum CompareOp
{
    None,       // field not part of the query
    Equal,
    NotEqual,
    Above,
    Below,
    Contains,   // substring (strings) / bitmask (ints, flags)
    NotContains,
}

/// <summary>Which property a field criterion tests.</summary>
public enum FindField
{
    // Sector
    Num, NSurfs, Flags, ExtraLight, ColorMap, Tint, Sound, SoundVolume, Layer,
    // Surface
    Material, AdjoinSector, AdjoinSurface, AdjoinFlags, SurfFlags, FaceFlags, Geo, Light, Tex,
    // Thing
    Name, Template, Pitch, Yaw, Roll, X, Y, Z, Sector,
    // Light
    Range, Intensity, Color,
}

/// <summary>One per-field criterion: the field, its comparison and the expected value.</summary>
public sealed class FieldCriterion
{
    public FindField Field;
    public CompareOp Op = CompareOp.None;

    /// <summary>Value for string fields (material, name, colormap, sound, layer name).</summary>
    public string Text = string.Empty;

    /// <summary>Value for integer and flag fields (also the bitmask for Contains on flags).</summary>
    public long Long;

    /// <summary>Value for scalar fields (volume, pitch/yaw/roll, position, range, intensity).</summary>
    public double Number;

    /// <summary>Value for color fields (extra light, tint).</summary>
    public ColorF Color;
}

/// <summary>The kind of value a criterion's field holds.</summary>
public enum FieldValueKind { String, Integer, Flags, Double, Color }

public static class FieldCriteria
{
    /// <summary>The fields offered for each find kind, in dialog order.</summary>
    public static readonly IReadOnlyDictionary<FindKind, FindField[]> Fields = new Dictionary<FindKind, FindField[]>
    {
        [FindKind.Sector] = new[]
        {
            FindField.Num, FindField.NSurfs, FindField.Flags, FindField.ExtraLight,
            FindField.ColorMap, FindField.Tint, FindField.Sound, FindField.SoundVolume, FindField.Layer,
        },
        [FindKind.Surface] = new[]
        {
            FindField.Num, FindField.Material, FindField.SurfFlags, FindField.FaceFlags,
            FindField.AdjoinFlags, FindField.AdjoinSector, FindField.AdjoinSurface,
            FindField.ExtraLight, FindField.Geo, FindField.Light, FindField.Tex, FindField.Layer,
        },
        [FindKind.Thing] = new[]
        {
            FindField.Num, FindField.Name, FindField.Template, FindField.Sector,
            FindField.X, FindField.Y, FindField.Z, FindField.Pitch, FindField.Yaw, FindField.Roll, FindField.Layer,
        },
        [FindKind.Light] = new[]
        {
            FindField.Num, FindField.Flags, FindField.Range, FindField.Intensity, FindField.Color, FindField.Layer,
        },
    };

    public static FieldValueKind KindOf(FindField field) => field switch
    {
        FindField.ColorMap or FindField.Sound or FindField.Material or FindField.Name
            or FindField.Template or FindField.Layer => FieldValueKind.String,
        FindField.ExtraLight or FindField.Tint or FindField.Color => FieldValueKind.Color,
        FindField.SoundVolume or FindField.Pitch or FindField.Yaw or FindField.Roll
            or FindField.X or FindField.Y or FindField.Z or FindField.Range or FindField.Intensity
            => FieldValueKind.Double,
        FindField.Flags or FindField.AdjoinFlags or FindField.SurfFlags or FindField.FaceFlags
            => FieldValueKind.Flags,
        _ => FieldValueKind.Integer,
    };

    /// <summary>Human-readable field name for the dialog rows.</summary>
    public static string Label(FindField field) => field switch
    {
        FindField.Num => "Number",
        FindField.NSurfs => "Surface count",
        FindField.Flags => "Flags",
        FindField.ExtraLight => "Extra light",
        FindField.ColorMap => "Color map",
        FindField.Tint => "Tint",
        FindField.Sound => "Sound",
        FindField.SoundVolume => "Volume",
        FindField.Layer => "Layer",
        FindField.Material => "Material",
        FindField.AdjoinSector => "Adjoined sector",
        FindField.AdjoinSurface => "Adjoined surface",
        FindField.AdjoinFlags => "Adjoin flags",
        FindField.SurfFlags => "Surface flags",
        FindField.FaceFlags => "Face flags",
        FindField.Geo => "Geo",
        FindField.Light => "Light",
        FindField.Tex => "Tex",
        FindField.Name => "Name",
        FindField.Template => "Template",
        FindField.Pitch => "Pitch",
        FindField.Yaw => "Yaw",
        FindField.Roll => "Roll",
        FindField.X => "X",
        FindField.Y => "Y",
        FindField.Z => "Z",
        FindField.Sector => "Sector",
        FindField.Range => "Range",
        FindField.Intensity => "Intensity",
        FindField.Color => "Color",
        _ => field.ToString(),
    };

    /// <summary>The operators offered per value kind (the original's combo items).</summary>
    public static CompareOp[] Operators(FieldValueKind kind) => kind switch
    {
        FieldValueKind.String => new[]
        {
            CompareOp.None, CompareOp.Equal, CompareOp.NotEqual, CompareOp.Contains, CompareOp.NotContains,
        },
        FieldValueKind.Double => new[]
        {
            CompareOp.None, CompareOp.Equal, CompareOp.NotEqual, CompareOp.Above, CompareOp.Below,
        },
        _ => new[]
        {
            CompareOp.None, CompareOp.Equal, CompareOp.NotEqual, CompareOp.Above, CompareOp.Below,
            CompareOp.Contains, CompareOp.NotContains,
        },
    };

    /// <summary>Parses a color field's text ("r g b" or "r/g/b", 0..1 or 0..255).</summary>
    public static bool TryParseColor(string text, out ColorF color)
    {
        color = ColorF.Black;
        var parts = text.Split(new[] { ' ', '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        double[] c = new double[3];
        for (int i = 0; i < 3; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out c[i]))
                return false;
        if (c[0] > 1 || c[1] > 1 || c[2] > 1)
            for (int i = 0; i < 3; i++) c[i] /= 255.0;
        color = new ColorF((float)c[0], (float)c[1], (float)c[2]);
        return true;
    }

    /// <summary>The layer name a sector/thing/light criterion matches (the original's Layer fields are names).</summary>
    public static string LayerName(Level level, int layer) =>
        (uint)layer < (uint)level.Layers.Count ? level.Layers[layer] : $"Layer{layer}";
}
