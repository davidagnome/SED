using Sed.Core.Math;

namespace Sed.Core.Model;

/// <summary>
/// A polygon surface of a sector (TJKSurface : TPolygon). Holds its vertex
/// references with per-vertex UVs and intensities, material, and adjoin link.
/// </summary>
public sealed class Surface
{
    /// <summary>Per-corner reference into the owning sector's vertex list, plus UV and light.</summary>
    public sealed class Corner
    {
        public Vertex Vertex = null!;
        public TexVertex Uv;
        public ColorF Intensity = ColorF.White;
    }

    // Surface flags (SF_*)
    public const long SfSkyHorizon = 0x200;
    public const long SfSkyCeiling = 0x400;
    // Face flags (FF_*)
    public const long FfTranslucent = 0x02;

    public int Num;
    public long SurfFlags;
    public long FaceFlags;
    public string Material = string.Empty;
    public int MaterialIndex = -1;

    /// <summary>True if this surface is a sky (horizon or ceiling) surface.</summary>
    public bool IsSky => (SurfFlags & (SfSkyHorizon | SfSkyCeiling)) != 0;

    /// <summary>True if this surface uses alpha blending.</summary>
    public bool IsTranslucent => (FaceFlags & FfTranslucent) != 0;

    public Surface? Adjoin;
    public long AdjoinFlags;

    public float UScale = 1f;
    public float VScale = 1f;

    public Sector Sector { get; }
    public List<Corner> Corners { get; } = new();

    /// <summary>Cached surface normal, recomputed by <see cref="RecalcNormal"/>.</summary>
    public Vec3 Normal { get; private set; }

    public Surface(Sector owner) => Sector = owner;

    /// <summary>Recomputes the polygon normal from the first three non-colinear corners (RecalcAll).</summary>
    public void RecalcNormal()
    {
        if (Corners.Count < 3)
        {
            Normal = Vec3.Zero;
            return;
        }

        var a = Corners[0].Vertex.Position;
        var b = Corners[1].Vertex.Position;
        var c = Corners[2].Vertex.Position;
        Normal = (b - a).Cross(c - a).Normalized();
    }
}
