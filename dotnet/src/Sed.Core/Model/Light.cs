using Sed.Core.Math;

namespace Sed.Core.Model;

/// <summary>An editor light source (TSedLight).</summary>
public sealed class Light
{
    public int Id;
    public long Flags;
    public int Layer;

    public ColorF Color = ColorF.White;
    public double Intensity = 1.0;
    public double Range;
    public Vec3 Position;
}

/// <summary>A COG script reference (TCOG).</summary>
public sealed class Cog
{
    public string Name = string.Empty;
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
}
