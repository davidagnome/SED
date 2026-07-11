using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Rendering;
using Xunit;

namespace Sed.Core.Tests;

public class ExtrudeCleaveTests
{
    private static (Level level, Sector sector, Surface surf) MakeBoxScene()
    {
        var level = new Level();
        var sector = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "dflt.mat", 0);
        level.Sectors.Add(sector);
        level.RenumberSectors();
        return (level, sector, sector.Surfaces[0]);
    }

    private static (Level level, Sector sector, Surface surf) MakeQuadScene()
    {
        var level = new Level();
        var sector = level.NewSector();
        var v0 = sector.AddVertex(new Vec3(0, 0, 0));
        var v1 = sector.AddVertex(new Vec3(2, 0, 0));
        var v2 = sector.AddVertex(new Vec3(2, 2, 0));
        var v3 = sector.AddVertex(new Vec3(0, 2, 0));
        var surf = sector.NewSurface();
        surf.Material = "dflt.mat";
        surf.Corners.Add(new Surface.Corner { Vertex = v0, Uv = new TexVertex(0, 0), Intensity = ColorF.White });
        surf.Corners.Add(new Surface.Corner { Vertex = v1, Uv = new TexVertex(64, 0), Intensity = ColorF.White });
        surf.Corners.Add(new Surface.Corner { Vertex = v2, Uv = new TexVertex(64, 64), Intensity = ColorF.White });
        surf.Corners.Add(new Surface.Corner { Vertex = v3, Uv = new TexVertex(0, 64), Intensity = ColorF.White });
        surf.RecalcNormal();
        return (level, sector, surf);
    }

    [Fact]
    public void Extrude_CreatesNewSector_WithSideSurfaces()
    {
        var (level, sector, surf) = MakeBoxScene();
        int sectorsBefore = level.Sectors.Count;

        var cmd = new ExtrudeSurfaceCommand(surf, 1.0);
        cmd.Apply();

        Assert.Equal(sectorsBefore + 1, level.Sectors.Count);

        var newSector = level.Sectors[^1];
        int n = surf.Corners.Count; // 4 for a box face
        // 1 adjoin + 1 opposite + n side surfaces
        Assert.Equal(n + 2, newSector.Surfaces.Count);

        var adjoinSurface = newSector.Surfaces[0];
        Assert.Same(adjoinSurface, surf.Adjoin);
        Assert.Same(surf, adjoinSurface.Adjoin);
        Assert.Equal(0x01 | 0x02, surf.AdjoinFlags);

        // Vertex count: front ring + back ring
        Assert.Equal(n * 2, newSector.Vertices.Count);

        cmd.Revert();
        Assert.Equal(sectorsBefore, level.Sectors.Count);
        Assert.Null(surf.Adjoin);
    }

    [Fact]
    public void Extrude_ThroughEditHistory()
    {
        var (level, sector, surf) = MakeBoxScene();
        int sectorsBefore = level.Sectors.Count;
        var history = new EditHistory();

        history.Do(new ExtrudeSurfaceCommand(surf, 1.0));
        Assert.Equal(sectorsBefore + 1, level.Sectors.Count);
        Assert.NotNull(surf.Adjoin);

        history.Undo();
        Assert.Equal(sectorsBefore, level.Sectors.Count);
        Assert.Null(surf.Adjoin);

        history.Redo();
        Assert.Equal(sectorsBefore + 1, level.Sectors.Count);
        Assert.NotNull(surf.Adjoin);
    }

    [Fact]
    public void CleaveSurface_SplitsInTwo()
    {
        var (level, sector, surf) = MakeQuadScene();
        int vertsBefore = sector.Vertices.Count;  // 4
        int surfsBefore = sector.Surfaces.Count;  // 1
        int cornersBefore = surf.Corners.Count;    // 4

        var cmd = new CleaveSurfaceCommand(surf, new Vec3(0, 1, 0), new Vec3(0, 1, 0));
        cmd.Apply();

        // Two new edge-crossing vertices inserted, one new surface created.
        Assert.Equal(vertsBefore + 2, sector.Vertices.Count);
        Assert.Equal(surfsBefore + 1, sector.Surfaces.Count);

        var newSurf = sector.Surfaces[^1];
        Assert.NotSame(surf, newSurf);

        // Each half is a quad → total corners = original + 2 * num_crossings.
        Assert.Equal(4, surf.Corners.Count);
        Assert.Equal(4, newSurf.Corners.Count);
        Assert.Equal(cornersBefore + 4, surf.Corners.Count + newSurf.Corners.Count);

        // Both surfaces should have valid normals.
        Assert.True(surf.Normal.LengthSquared > 0.5);
        Assert.True(newSurf.Normal.LengthSquared > 0.5);

        cmd.Revert();
        Assert.Equal(surfsBefore, sector.Surfaces.Count);
        Assert.Equal(vertsBefore, sector.Vertices.Count);
        Assert.Equal(cornersBefore, surf.Corners.Count);
    }

    [Fact]
    public void CleaveSurface_ParallelPlane_IsNoOp()
    {
        var (level, sector, surf) = MakeQuadScene();
        int vertsBefore = sector.Vertices.Count;
        int surfsBefore = sector.Surfaces.Count;
        int cornersBefore = surf.Corners.Count;

        // Plane parallel to the XY surface, far away — no intersection.
        var cmd = new CleaveSurfaceCommand(surf, new Vec3(0, 0, 1), new Vec3(0, 0, 5));
        cmd.Apply();

        Assert.Equal(vertsBefore, sector.Vertices.Count);
        Assert.Equal(surfsBefore, sector.Surfaces.Count);
        Assert.Equal(cornersBefore, surf.Corners.Count);

        // Revert should also be a safe no-op.
        cmd.Revert();
        Assert.Equal(vertsBefore, sector.Vertices.Count);
        Assert.Equal(cornersBefore, surf.Corners.Count);
    }

    [Fact]
    public void CleaveSurface_ThroughEditHistory()
    {
        var (level, sector, surf) = MakeQuadScene();
        int surfsBefore = sector.Surfaces.Count;
        var history = new EditHistory();

        history.Do(new CleaveSurfaceCommand(surf, new Vec3(0, 1, 0), new Vec3(0, 1, 0)));
        Assert.Equal(surfsBefore + 1, sector.Surfaces.Count);

        history.Undo();
        Assert.Equal(surfsBefore, sector.Surfaces.Count);
        Assert.Equal(4, surf.Corners.Count);

        history.Redo();
        Assert.Equal(surfsBefore + 1, sector.Surfaces.Count);
    }

    [Fact]
    public void CleaveSurface_InterpolatesEdgeVertices()
    {
        var (level, sector, surf) = MakeQuadScene();

        var cmd = new CleaveSurfaceCommand(surf, new Vec3(0, 1, 0), new Vec3(0, 1, 0));
        cmd.Apply();

        // The plane y=1 crosses edges (1,0)-(2,0)→(2,2,0) and (0,2,0)→(0,0,0).
        // Intersection of (2,0,0)-(2,2,0) at y=1 is (2,1,0).
        // Intersection of (0,2,0)-(0,0,0) at y=1 is (0,1,0).
        var inserted = sector.Vertices.Skip(4).ToList(); // original 4 + 2 new
        Assert.Equal(2, inserted.Count);
        Assert.Contains(inserted, v => System.Math.Abs(v.Position.X - 2) < 1e-6 && System.Math.Abs(v.Position.Y - 1) < 1e-6);
        Assert.Contains(inserted, v => System.Math.Abs(v.Position.X) < 1e-6 && System.Math.Abs(v.Position.Y - 1) < 1e-6);

        cmd.Revert();
    }
}
