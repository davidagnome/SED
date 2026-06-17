using Sed.Core.Model;
using Sed.Formats.Jkl;
using Xunit;

namespace Sed.Core.Tests;

public class JklParserTests
{
    private const string SampleJkl = """
        SECTION: HEADER
        VERSION 1
        WORLD GRAVITY 4.0
        END

        SECTION: GEORESOURCE
        WORLD COLORMAPS 1
        0:	dflt.cmp
        WORLD VERTICES 4
        0:	-1.0 -1.0 0.0
        1:	 1.0 -1.0 0.0
        2:	 1.0  1.0 0.0
        3:	-1.0  1.0 0.0
        WORLD TEXTURE VERTICES 1
        0:	0.0 0.0
        WORLD ADJOINS 0
        WORLD SURFACES 1
        0:	0 0 0 3 0 0 -1 0.5 4 0,0 1,0 2,0 3,0 0.5 0.5 0.5 0.5
        0:	0.0 0.0 1.0
        END

        SECTION: SECTORS
        World sectors 1
        SECTOR 0
        FLAGS	0x4
        VERTICES 4
        0:	0
        1:	1
        2:	2
        3:	3
        SURFACES 0 1
        END

        SECTION: THINGS
        WORLD THINGS 1
        0:	walkplayer	Player	0.0 0.0 0.5 0.0 0.0 0.0 0
        END
        """;

    [Fact]
    public void ParsesHeaderVersionAndKind()
    {
        var level = JklParser.Parse(SampleJkl);
        Assert.Equal(1, level.Header.Version);
        Assert.Equal(ProjectType.JediKnight, level.Kind);
        Assert.Equal(4.0f, level.Header.Gravity, 3);
    }

    [Fact]
    public void BuildsSectorWithDedupedVerticesAndSurface()
    {
        var level = JklParser.Parse(SampleJkl);
        Assert.Single(level.Sectors);

        var sector = level.Sectors[0];
        Assert.Equal(0x4, sector.Flags);
        Assert.Equal(4, sector.Vertices.Count);     // 4 distinct world vertices
        Assert.Single(sector.Surfaces);

        var surf = sector.Surfaces[0];
        Assert.Equal(4, surf.Corners.Count);
        Assert.Equal(-1.0, surf.Corners[0].Vertex.Position.X, 6);
        Assert.Equal(-1.0, surf.Corners[0].Vertex.Position.Y, 6);
    }

    [Fact]
    public void FloorSurfaceNormalFacesUp()
    {
        var level = JklParser.Parse(SampleJkl);
        var n = level.Sectors[0].Surfaces[0].Normal;
        Assert.Equal(0.0, n.X, 6);
        Assert.Equal(0.0, n.Y, 6);
        Assert.Equal(1.0, n.Z, 6); // CCW floor at z=0 → +Z
    }

    [Fact]
    public void ParsesThingWithNamePositionAndSector()
    {
        var level = JklParser.Parse(SampleJkl);
        Assert.Single(level.Things);
        var thing = level.Things[0];
        Assert.Equal("Player", thing.Name);          // case preserved
        Assert.Equal(0.5, thing.Position.Z, 6);
        Assert.Same(level.Sectors[0], thing.Sector);
    }
}
