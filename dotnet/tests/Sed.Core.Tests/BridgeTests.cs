using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Rendering;
using Xunit;

namespace Sed.Core.Tests;

public class BridgeTests
{
    /// <summary>
    /// Two facing quads in the plane z=0, in separate sectors. `a` is wound so its
    /// normal points +Z and `b` so its points −Z, which is the back-to-back
    /// arrangement a bridge requires.
    /// </summary>
    private static (Level level, Surface a, Surface b) MakeFacingPair(
        double aHalf = 1.0, double bHalf = 1.0, Vec3 bOffset = default)
    {
        var level = new Level();
        var sa = level.NewSector();
        var sb = level.NewSector();

        Surface Quad(Sector sector, double half, Vec3 offset, bool reverse)
        {
            var pts = new[]
            {
                offset + new Vec3(-half, -half, 0),
                offset + new Vec3(half, -half, 0),
                offset + new Vec3(half, half, 0),
                offset + new Vec3(-half, half, 0),
            };
            if (reverse) System.Array.Reverse(pts);

            var surf = sector.NewSurface();
            surf.Material = "dflt.mat";
            foreach (var p in pts)
                surf.Corners.Add(new Surface.Corner
                {
                    Vertex = sector.AddVertex(p),
                    Uv = new TexVertex(0, 0),
                    Intensity = ColorF.White,
                });
            surf.RecalcNormal();
            return surf;
        }

        var a = Quad(sa, aHalf, Vec3.Zero, reverse: false);
        var b = Quad(sb, bHalf, bOffset, reverse: true);
        level.RenumberSectors();
        return (level, a, b);
    }

    [Fact]
    public void IdenticalFacingQuadsBridgeIntoAnAdjoinPair()
    {
        var (_, a, b) = MakeFacingPair();
        Assert.Null(BridgeSurfacesCommand.Validate(a, b));

        var history = new EditHistory();
        var command = new BridgeSurfacesCommand(a, b);
        history.Do(command);

        Assert.Null(command.Failure);
        Assert.Same(b, a.Adjoin);
        Assert.Same(a, b.Adjoin);

        history.Undo();
        Assert.Null(a.Adjoin);
        Assert.Null(b.Adjoin);
    }

    [Fact]
    public void ASmallerFaceTrimsTheLargerOneToTheirSharedRegion()
    {
        // A 4×4 face meeting a 2×2 one: the big face must be cut down.
        var (_, big, small) = MakeFacingPair(aHalf: 2.0, bHalf: 1.0);
        int bigSectorSurfacesBefore = big.Sector.Surfaces.Count;

        var history = new EditHistory();
        var command = new BridgeSurfacesCommand(big, small);
        history.Do(command);

        Assert.Null(command.Failure);
        Assert.Same(small, big.Adjoin);

        // The trimmed portal is the size of the smaller face.
        double extent = big.Corners.Max(c => c.Vertex.Position.X) - big.Corners.Min(c => c.Vertex.Position.X);
        Assert.Equal(2.0, extent, 4);

        // The offcuts stay in the sector as extra surfaces.
        Assert.True(big.Sector.Surfaces.Count > bigSectorSurfacesBefore);

        history.Undo();
        Assert.Null(big.Adjoin);
        Assert.Equal(bigSectorSurfacesBefore, big.Sector.Surfaces.Count);
        Assert.Equal(4, big.Corners.Count);
        double restored = big.Corners.Max(c => c.Vertex.Position.X) - big.Corners.Min(c => c.Vertex.Position.X);
        Assert.Equal(4.0, restored, 4);
    }

    [Fact]
    public void RedoReplaysTheSameResult()
    {
        var (_, big, small) = MakeFacingPair(aHalf: 2.0, bHalf: 1.0);
        var history = new EditHistory();
        history.Do(new BridgeSurfacesCommand(big, small));

        int corners = big.Corners.Count;
        history.Undo();
        history.Redo();

        Assert.Same(small, big.Adjoin);
        Assert.Equal(corners, big.Corners.Count);
    }

    [Fact]
    public void SurfacesInTheSameSectorAreRejected()
    {
        var (_, a, _) = MakeFacingPair();
        var other = a.Sector.NewSurface();
        foreach (var c in a.Corners)
            other.Corners.Add(new Surface.Corner { Vertex = c.Vertex, Intensity = ColorF.White });

        Assert.Contains("same sector", BridgeSurfacesCommand.Validate(a, other));
    }

    [Fact]
    public void AlreadyAdjoinedSurfacesAreRejected()
    {
        var (_, a, b) = MakeFacingPair();
        a.Adjoin = b;
        b.Adjoin = a;

        Assert.Contains("already adjoined", BridgeSurfacesCommand.Validate(a, b));
    }

    [Fact]
    public void SurfacesFacingTheSameWayAreRejected()
    {
        var level = new Level();
        var sa = level.NewSector();
        var sb = level.NewSector();

        Surface Quad(Sector sector)
        {
            var surf = sector.NewSurface();
            foreach (var p in new[]
                     {
                         new Vec3(-1, -1, 0), new Vec3(1, -1, 0),
                         new Vec3(1, 1, 0), new Vec3(-1, 1, 0),
                     })
                surf.Corners.Add(new Surface.Corner { Vertex = sector.AddVertex(p), Intensity = ColorF.White });
            surf.RecalcNormal();
            return surf;
        }

        // Same winding ⇒ same normal direction ⇒ not back to back.
        Assert.Contains("do not face each other", BridgeSurfacesCommand.Validate(Quad(sa), Quad(sb)));
    }

    [Fact]
    public void SurfacesInDifferentPlanesAreRejected()
    {
        // Facing each other, but 5 units apart along the normal.
        var (_, a, b) = MakeFacingPair(bOffset: new Vec3(0, 0, 5));

        Assert.Contains("not back to back", BridgeSurfacesCommand.Validate(a, b));
    }

    [Fact]
    public void NonOverlappingFacesLeaveTheGeometryUntouched()
    {
        // Coplanar and facing, but side by side with no shared area.
        var (_, a, b) = MakeFacingPair(bOffset: new Vec3(10, 0, 0));
        Assert.Null(BridgeSurfacesCommand.Validate(a, b));   // passes the cheap checks

        int aCorners = a.Corners.Count;
        int aSurfaces = a.Sector.Surfaces.Count;
        int bSurfaces = b.Sector.Surfaces.Count;

        var command = new BridgeSurfacesCommand(a, b);
        command.Apply();

        Assert.NotNull(command.Failure);
        Assert.Contains("do not overlap", command.Failure);

        // A failed bridge must roll its own trimming back rather than leaving
        // half-cleaved geometry behind.
        Assert.Null(a.Adjoin);
        Assert.Equal(aCorners, a.Corners.Count);
        Assert.Equal(aSurfaces, a.Sector.Surfaces.Count);
        Assert.Equal(bSurfaces, b.Sector.Surfaces.Count);
    }
}
