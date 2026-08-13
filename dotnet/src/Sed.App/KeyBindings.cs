using Avalonia.Input;

namespace Sed.App;

/// <summary>
/// The configurable command-key table. Every menu command has a canonical
/// action name with one or more default gestures; the user's overrides live in
/// <see cref="AppSettings.KeyBindings"/> (action → semicolon-separated gesture
/// strings, the round-trip form of <see cref="KeyGesture.ToString"/>).
/// Both the menu items and the view's own chord handling resolve through here,
/// so a remapped command fires exactly once no matter where focus sits.
/// </summary>
public sealed class CommandKeys
{
    public const string Undo = "Undo";
    public const string Redo = "Redo";
    public const string SaveAs = "Save As";
    public const string Copy = "Copy";
    public const string Paste = "Paste";
    public const string Duplicate = "Duplicate";
    public const string SelectAllInSector = "Select All in Sector";
    public const string SelectNone = "Select None";
    public const string Extrude = "Extrude Surface";
    public const string ExtrudeInward = "Extrude Inward";
    public const string FlipSurface = "Flip Surface";
    public const string CleaveSurface = "Cleave Surface";
    public const string MakeAdjoin = "Make Adjoin";
    public const string RemoveAdjoin = "Remove Adjoin";
    public const string BridgeSurfaces = "Bridge Two Surfaces";
    public const string ConnectSectors = "Connect Two Sectors";
    public const string ShiftTextureLeft = "Shift Texture Left";
    public const string ShiftTextureRight = "Shift Texture Right";
    public const string ShiftTextureUp = "Shift Texture Up";
    public const string ShiftTextureDown = "Shift Texture Down";
    public const string ScaleTextureUp = "Scale Texture Up";
    public const string ScaleTextureDown = "Scale Texture Down";
    public const string RotateTextureCw = "Rotate Texture CW";
    public const string RotateTextureCcw = "Rotate Texture CCW";
    public const string AutoFitTexture = "Auto-fit Texture";
    public const string AlignTextureNeighbour = "Align Texture to Neighbour";
    public const string CalculateLighting = "Calculate Lighting";
    public const string CalculateLightingNoShadows = "Calculate Lighting (no shadows)";
    public const string Find = "Find";
    public const string CheckConsistency = "Check Consistency";

    private readonly AppSettings _settings;

    public CommandKeys(AppSettings settings) => _settings = settings;

    /// <summary>Display name + default gestures for every configurable command.</summary>
    public static IReadOnlyList<(string Name, string Label, KeyGesture[] Defaults)> All { get; } = new[]
    {
        (Undo, "Undo", new[] { G("Ctrl+Z") }),
        (Redo, "Redo", new[] { G("Ctrl+Y"), G("Ctrl+Shift+Z") }),
        (SaveAs, "Save As…", new[] { G("Ctrl+S") }),
        (Copy, "Copy", new[] { G("Ctrl+C") }),
        (Paste, "Paste", new[] { G("Ctrl+V") }),
        (Duplicate, "Duplicate", new[] { G("Ctrl+D") }),
        (SelectAllInSector, "Select All in Sector", new[] { G("Ctrl+A") }),
        (SelectNone, "Select None", new[] { G("Escape") }),
        (Extrude, "Extrude Surface", new[] { G("Ctrl+E") }),
        (ExtrudeInward, "Extrude Inward", new[] { G("Ctrl+Shift+E") }),
        (FlipSurface, "Flip Surface", new[] { G("Ctrl+F") }),
        (CleaveSurface, "Cleave Surface", new[] { G("Ctrl+K") }),
        (MakeAdjoin, "Make Adjoin", new[] { G("Ctrl+J") }),
        (RemoveAdjoin, "Remove Adjoin", new[] { G("Ctrl+Shift+J") }),
        (BridgeSurfaces, "Bridge Two Surfaces", new[] { G("Ctrl+B") }),
        (ConnectSectors, "Connect Two Sectors", new[] { G("Ctrl+Shift+B") }),
        (ShiftTextureLeft, "Shift Texture Left", new[] { G("Ctrl+Left") }),
        (ShiftTextureRight, "Shift Texture Right", new[] { G("Ctrl+Right") }),
        (ShiftTextureUp, "Shift Texture Up", new[] { G("Ctrl+Up") }),
        (ShiftTextureDown, "Shift Texture Down", new[] { G("Ctrl+Down") }),
        (ScaleTextureUp, "Scale Texture Up", new[] { G("Ctrl+OemPlus") }),
        (ScaleTextureDown, "Scale Texture Down", new[] { G("Ctrl+OemMinus") }),
        (RotateTextureCw, "Rotate Texture CW", new[] { G("Ctrl+R") }),
        (RotateTextureCcw, "Rotate Texture CCW", new[] { G("Ctrl+Shift+R") }),
        (AutoFitTexture, "Auto-fit Texture", new[] { G("Ctrl+T") }),
        (AlignTextureNeighbour, "Align Texture to Neighbour", new[] { G("Ctrl+Shift+T") }),
        (CalculateLighting, "Calculate Lighting", new[] { G("F9") }),
        (CalculateLightingNoShadows, "Calculate Lighting (no shadows)", new[] { G("Shift+F9") }),
        (Find, "Find…", new[] { G("Ctrl+Shift+F") }),
        (CheckConsistency, "Check Consistency…", new[] { G("F8") }),
    };

    private static KeyGesture G(string s) => KeyGesture.Parse(s);

    /// <summary>The configured gestures for an action (falling back to the defaults).</summary>
    public KeyGesture[] Gestures(string action)
    {
        var defaults = All.FirstOrDefault(a => a.Name == action).Defaults ?? Array.Empty<KeyGesture>();
        if (_settings.KeyBindings.TryGetValue(action, out var text) && text.Length > 0)
        {
            var parsed = text.Split(';')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Select(t => ParseSafe(t))
                .Where(g => g is not null)
                .Cast<KeyGesture>()
                .ToArray();
            if (parsed.Length > 0) return parsed;
        }
        return defaults;
    }

    /// <summary>Whether <paramref name="key"/>/<paramref name="modifiers"/> is one of an action's gestures.</summary>
    public bool Pressed(string action, Key key, KeyModifiers modifiers) =>
        Gestures(action).Any(g => g.Key == key && g.KeyModifiers == modifiers);

    /// <summary>First gesture, for the menu item display; null when the action has none.</summary>
    public KeyGesture? Primary(string action)
    {
        var g = Gestures(action);
        return g.Length == 0 ? null : g[0];
    }

    private static KeyGesture? ParseSafe(string text)
    {
        try { return KeyGesture.Parse(text); }
        catch { return null; }
    }
}
