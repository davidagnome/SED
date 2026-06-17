using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Rendering;

/// <summary>
/// Procedural test geometry built through the real <see cref="Level"/> model, so
/// the model → <see cref="SceneBuilder"/> → renderer path is exercised before the
/// JKL parser exists. Produces a unit cube as one sector with six quad surfaces.
/// </summary>
public static class SampleScene
{
    public static Level CreateCube(double half = 1.0)
    {
        var level = new Level();
        var sector = level.NewSector();

        Vec3[] p =
        {
            new(-half, -half, -half), // 0
            new( half, -half, -half), // 1
            new( half,  half, -half), // 2
            new(-half,  half, -half), // 3
            new(-half, -half,  half), // 4
            new( half, -half,  half), // 5
            new( half,  half,  half), // 6
            new(-half,  half,  half), // 7
        };
        var v = new Vertex[8];
        for (int i = 0; i < 8; i++)
            v[i] = sector.AddVertex(p[i]);

        int[][] faces =
        {
            new[] { 0, 1, 2, 3 }, // -Z
            new[] { 4, 5, 6, 7 }, // +Z
            new[] { 0, 1, 5, 4 }, // -Y
            new[] { 3, 2, 6, 7 }, // +Y
            new[] { 0, 3, 7, 4 }, // -X
            new[] { 1, 2, 6, 5 }, // +X
        };

        foreach (var face in faces)
        {
            var surf = sector.NewSurface();
            foreach (var idx in face)
                surf.Corners.Add(new Surface.Corner { Vertex = v[idx] });
        }

        return level;
    }
}
