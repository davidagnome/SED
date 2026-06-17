namespace Sed.Core.Math;

/// <summary>
/// Floating-point RGB color (TColorF in the original). Sith-engine lighting is
/// expressed as 0..1 RGB triplets; intensity can exceed 1 for over-bright lights.
/// </summary>
public readonly record struct ColorF(float R, float G, float B)
{
    public static readonly ColorF Black = new(0, 0, 0);
    public static readonly ColorF White = new(1, 1, 1);

    public static ColorF operator +(ColorF a, ColorF b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public static ColorF operator *(ColorF a, float s) => new(a.R * s, a.G * s, a.B * s);

    public override string ToString() => $"({R:0.##}, {G:0.##}, {B:0.##})";
}
