using Sed.Core.Math;

namespace Sed.Formats.Jkl;

/// <summary>
/// Transient GEORESOURCE tables built while parsing, then referenced by the
/// SECTORS section (mirrors the GVXList/GTXVXList/GSurfList locals in the
/// original loader). Discarded once the model is built.
/// </summary>
internal sealed class GeoData
{
    public List<string> Materials { get; } = new();
    public List<Vec3> Vertices { get; } = new();
    public List<Vec2> TexVertices { get; } = new();
    public List<TempSurface> Surfaces { get; } = new();

    public string GetMaterial(int index) =>
        (uint)index < (uint)Materials.Count ? Materials[index] : string.Empty;
}

/// <summary>A surface as read from GEORESOURCE, before it is bound into a sector.</summary>
internal sealed class TempSurface
{
    public int Material;
    public long SurfFlags;
    public long FaceFlags;
    public int Geo, Light, Tex, Adjoin;
    public ColorF ExtraLight;
    public List<int> Vxs { get; } = new();
    public List<int> Tvxs { get; } = new();
    public List<ColorF> Intensities { get; } = new();
}
