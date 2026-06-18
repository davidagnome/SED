using Sed.Core.Math;

namespace Sed.Core.Model;

/// <summary>Builds default geometry for new sectors.</summary>
public static class SectorFactory
{
    private static readonly int[][] BoxFaces =
    {
        new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
        new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 2, 6, 5 },
    };

    /// <summary>
    /// Creates a detached box-room sector (8 vertices, 6 quad surfaces) using the
    /// given material. Not added to the level — a command does that for undo.
    /// </summary>
    public static Sector CreateBox(Level level, Vec3 center, double half, string material, int materialIndex)
    {
        var sector = new Sector(level) { Ambient = new ColorF(1, 1, 1) };
        Vec3[] p =
        {
            center + new Vec3(-half, -half, -half), center + new Vec3(half, -half, -half),
            center + new Vec3(half, half, -half),   center + new Vec3(-half, half, -half),
            center + new Vec3(-half, -half, half),  center + new Vec3(half, -half, half),
            center + new Vec3(half, half, half),    center + new Vec3(-half, half, half),
        };
        var v = new Vertex[8];
        for (int i = 0; i < 8; i++) v[i] = sector.AddVertex(p[i]);

        // One texture tile per face (UVs in texels; renderer divides by material size).
        var uv = new[] { new TexVertex(0, 0), new TexVertex(64, 0), new TexVertex(64, 64), new TexVertex(0, 64) };

        foreach (var face in BoxFaces)
        {
            var surf = sector.NewSurface();
            surf.Material = material;
            surf.MaterialIndex = materialIndex;
            surf.SurfFlags = 0x4;   // SF_Collision
            surf.Geo = 4; surf.Light = 3; surf.Tex = 3;
            for (int k = 0; k < 4; k++)
                surf.Corners.Add(new Surface.Corner { Vertex = v[face[k]], Uv = uv[k], Intensity = new ColorF(1, 1, 1) });
        }
        return sector;
    }
}
