using Sed.Core.Math;

namespace Sed.Core.Model;

/// <summary>A placed game object / entity (TThing).</summary>
public sealed class Thing
{
    public int Id;
    public int Num;
    public string Name = string.Empty;
    /// <summary>Template this thing instantiates (resolves its model3d, type, etc.).</summary>
    public string Template = string.Empty;

    public Vec3 Position;
    /// <summary>Pitch, yaw, roll in degrees (pch, yaw, rol).</summary>
    public double Pitch, Yaw, Roll;

    public int Layer;
    public Sector? Sector;
    public Box BBox;

    public Level? Level;

    /// <summary>Template-derived key/value parameters (TTPLValues).</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ThingFlags Flags { get; set; }
}
