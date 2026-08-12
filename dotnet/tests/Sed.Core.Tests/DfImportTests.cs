using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Df;
using Sed.Formats.Jkl;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>
/// Tests for the Dark Forces .lev import (DF_IMPORT.INC). The fixture uses the
/// real DF conventions: sector walls listed counter-clockwise from above, floor
/// altitude below the ceiling. Assertions pin the exact conversion the original
/// performs (x=x, y=z, z=-y at the scale factor), so any future "correction" to
/// the axis mapping must come with a reason.
/// </summary>
public class DfImportTests
{
    // Two boxes side by side, sharing the wall (10,0)-(10,10). Sector 0's floor
    // is at -1.0 and ceiling at 1.0; sector 1's floor at -0.5 and ceiling at 1.0.
    // Three boxes, walls wound clockwise from above (interior right, the DF
    // convention the importer's outer-cycle test requires). Sector 0 shares the
    // (10,0)-(10,10) edge with sector 1 at equal heights; sector 1 shares the
    // (20,0)-(20,10) edge with sector 2, whose floor is lower (-2.0), so that
    // wall splits into a BOT cap plus a MID adjoin.
    private const string Lev = """
        LEV 1.0
        LEVELNAME test
        TEXTURES 3
        TEXTURE: wall01
        TEXTURE: ceil01
        TEXTURE: floor01
        NUMSECTORS 3
        SECTOR
        NAME first
        AMBIENT 12
        FLOOR ALTITUDE -1.000000
        CEILING ALTITUDE 1.000000
        SECOND ALTITUDE -1.000000
        FLOOR TEXTURE 2 0.000000 0.000000
        CEILING TEXTURE 1 0.000000 0.000000
        FLAGS 0x00000000
        LAYER 1
        VERTICES 4
        X: 0.000000 Z: 0.000000
        X: 0.000000 Z: 10.000000
        X: 10.000000 Z: 10.000000
        X: 10.000000 Z: 0.000000
        WALLS 4
        WALL LEFT: 0 RIGHT: 1 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 1 RIGHT: 2 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 2 RIGHT: 3 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: 1 MIRROR: 0 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 3 RIGHT: 0 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        SECTOR
        NAME second
        AMBIENT 20
        FLOOR ALTITUDE -1.000000
        CEILING ALTITUDE 1.000000
        SECOND ALTITUDE -1.000000
        FLOOR TEXTURE 2 0.000000 0.000000
        CEILING TEXTURE 1 0.000000 0.000000
        FLAGS 0x00000000
        LAYER 1
        VERTICES 4
        X: 10.000000 Z: 0.000000
        X: 10.000000 Z: 10.000000
        X: 20.000000 Z: 10.000000
        X: 20.000000 Z: 0.000000
        WALLS 4
        WALL LEFT: 0 RIGHT: 1 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: 0 MIRROR: 2 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 1 RIGHT: 2 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 2 RIGHT: 3 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: 2 MIRROR: 0 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 3 RIGHT: 0 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        SECTOR
        NAME third
        AMBIENT 10
        FLOOR ALTITUDE -2.000000
        CEILING ALTITUDE 1.000000
        SECOND ALTITUDE -2.000000
        FLOOR TEXTURE 2 0.000000 0.000000
        CEILING TEXTURE 1 0.000000 0.000000
        FLAGS 0x00000000
        LAYER 1
        VERTICES 4
        X: 20.000000 Z: 0.000000
        X: 20.000000 Z: 10.000000
        X: 30.000000 Z: 10.000000
        X: 30.000000 Z: 0.000000
        WALLS 4
        WALL LEFT: 0 RIGHT: 1 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: 1 MIRROR: 2 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 1 RIGHT: 2 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 2 RIGHT: 3 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        WALL LEFT: 3 RIGHT: 0 MID: 0 0.000000 0.000000 TOP: -1 0.000000 0.000000 BOT: -1 0.000000 0.000000 SIGN: -1 0.000000 0.000000 ADJOIN: -1 MIRROR: -1 FLAGS: 0x00000001 0x00000000 LIGHT: 31
        """;

    private const string Objects = """
        O 1.0
        LEVENAME test
        PODS 0
        SPRS 0
        FMES 0
        SOUNDS 0
        OBJECTS 2
        CLASS: PLAYER DATA: 0 X: 5.0 Y: 0.0 Z: 5.0 PCH: 0.0 YAW: 90.0 ROL: 0.0 DIFF: 0
        SEQ
        TYPE: RIFLE
        SEQEND
        CLASS: SPIRIT DATA: 1 X: 15.0 Y: 0.5 Z: 5.0 PCH: 0.0 YAW: 0.0 ROL: 0.0 DIFF: 2
        SEQ
        SEQEND
        """;

    private static (Level level, IReadOnlyList<string> warnings) Import(string lev = Lev, string? o = Objects)
    {
        var options = new DfImportOptions
        {
            KeepTextureNames = true,
            LogicTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RIFLE"] = "strifle",
            },
        };
        return DfLevelImporter.Import(lev, o, options);
    }

    [Fact]
    public void ConvertsAxisAndScale()
    {
        var (level, _) = Import();

        Assert.Equal(3, level.Sectors.Count);

        // Sector 0 floor: DF (0, -1, 0) → JK (0, 0, 1/35); the floor surface's
        // four corners sit at z = -(-1)/35 with the walls' XZ mapped to XY.
        var sec = level.Sectors[0];
        Assert.All(sec.Surfaces, s => Assert.True(s.Corners.Count is 3 or 4));

        var floor = sec.Surfaces.First(s => (s.SurfFlags & SurfaceFlags.Floor) != 0);
        Assert.All(floor.Corners, c => Assert.Equal(1.0 / 35.0, c.Vertex.Position.Z, 9));

        var ceiling = sec.Surfaces.First(s => (s.SurfFlags & SurfaceFlags.Floor) == 0 &&
                                              s.Corners.All(c => System.Math.Abs(c.Vertex.Position.Z - (-1.0 / 35.0)) < 1e-9));
        Assert.NotNull(ceiling);
        Assert.Equal("ceil01.mat", ceiling.Material);
    }

    [Fact]
    public void WallsBecomeFourSidedSurfaces()
    {
        var (level, _) = Import();
        var sec = level.Sectors[0];

        // Floor + ceiling + 4 walls.
        Assert.Equal(6, sec.Surfaces.Count);
        Assert.Equal(8, sec.Vertices.Count);

        // The wall between the two sectors is adjoined to sector 1's facing wall.
        var portal = sec.Surfaces.First(s => s.Adjoin is not null);
        Assert.Same(level.Sectors[1], portal.Adjoin!.Sector);
        Assert.Same(portal, portal.Adjoin.Adjoin);
        Assert.Equal(1, level.Sectors[1].Surfaces.Count(s => s.Adjoin is not null));
    }

    [Fact]
    public void UsesDefaultMaterialWhenNotKeepingTextureNames()
    {
        var (level, _) = DfLevelImporter.Import(Lev, null, new DfImportOptions { KeepTextureNames = false });
        Assert.All(level.Sectors.SelectMany(s => s.Surfaces).Where(s => s.Material.Length > 0),
            s => Assert.Equal("DFLT.MAT", s.Material));
    }

    [Fact]
    public void AmbientLightAppliedFromSectorHeader()
    {
        var (level, _) = Import();
        Assert.Equal(12.0 / 31.0, level.Sectors[0].Ambient.R, 6);
        Assert.Equal(20.0 / 31.0, level.Sectors[1].Ambient.R, 6);
        Assert.All(level.Sectors[0].Surfaces.SelectMany(s => s.Corners),
            c => Assert.Equal(12.0 / 31.0, c.Intensity.R, 6));
    }

    [Fact]
    public void LayersGetSyntheticNames()
    {
        var (level, _) = Import();
        Assert.Equal(new[] { "Layer1" }, level.Layers);
        Assert.All(level.Sectors, s => Assert.Equal(0, s.Layer));
    }

    [Fact]
    public void ObjectsConvertLogicAndCoordinates()
    {
        var (level, _) = Import();

        Assert.Equal(2, level.Things.Count);

        // PLAYER: DF (5, 0, 5) → JK (5/35, 5/35, 0); logic converted via the table.
        var player = level.Things[0];
        Assert.Equal("strifle", player.Name);
        Assert.Equal(new Vec3(5.0 / 35.0, 5.0 / 35.0, 0), player.Position);

        // SPIRIT/SAFE map to walkplayer; DIFF 2 sets TF_NOEASY.
        var spirit = level.Things[1];
        Assert.Equal("walkplayer", spirit.Name);
        Assert.Equal("0x1000", spirit.Values["thingflags"]);
    }

    [Fact]
    public void ConcaveSectorIsTriangulatedIntoConvexSectors()
    {
        // An L-shaped cycle (6 walls) must be split into two convex pieces.
        const string lShaped = """
            LEV 1.0
            TEXTURES 0
            NUMSECTORS 1
            SECTOR
            NAME L
            AMBIENT 10
            FLOOR ALTITUDE -1.000000
            CEILING ALTITUDE 1.000000
            SECOND ALTITUDE -1.000000
            FLOOR TEXTURE -1 0.0 0.0
            CEILING TEXTURE -1 0.0 0.0
            FLAGS 0x00000000
            LAYER 1
            VERTICES 6
            X: 0.000000 Z: 0.000000
            X: 0.000000 Z: 10.000000
            X: 5.000000 Z: 10.000000
            X: 5.000000 Z: 5.000000
            X: 10.000000 Z: 5.000000
            X: 10.000000 Z: 0.000000
            WALLS 6
            WALL LEFT: 0 RIGHT: 1 MID: -1 0.0 0.0 TOP: -1 0.0 0.0 BOT: -1 0.0 0.0 SIGN: -1 0.0 0.0 ADJOIN: -1 MIRROR: -1 FLAGS: 0x0 0x0 LIGHT: 31
            WALL LEFT: 1 RIGHT: 2 MID: -1 0.0 0.0 TOP: -1 0.0 0.0 BOT: -1 0.0 0.0 SIGN: -1 0.0 0.0 ADJOIN: -1 MIRROR: -1 FLAGS: 0x0 0x0 LIGHT: 31
            WALL LEFT: 2 RIGHT: 3 MID: -1 0.0 0.0 TOP: -1 0.0 0.0 BOT: -1 0.0 0.0 SIGN: -1 0.0 0.0 ADJOIN: -1 MIRROR: -1 FLAGS: 0x0 0x0 LIGHT: 31
            WALL LEFT: 3 RIGHT: 4 MID: -1 0.0 0.0 TOP: -1 0.0 0.0 BOT: -1 0.0 0.0 SIGN: -1 0.0 0.0 ADJOIN: -1 MIRROR: -1 FLAGS: 0x0 0x0 LIGHT: 31
            WALL LEFT: 4 RIGHT: 5 MID: -1 0.0 0.0 TOP: -1 0.0 0.0 BOT: -1 0.0 0.0 SIGN: -1 0.0 0.0 ADJOIN: -1 MIRROR: -1 FLAGS: 0x0 0x0 LIGHT: 31
            WALL LEFT: 5 RIGHT: 0 MID: -1 0.0 0.0 TOP: -1 0.0 0.0 BOT: -1 0.0 0.0 SIGN: -1 0.0 0.0 ADJOIN: -1 MIRROR: -1 FLAGS: 0x0 0x0 LIGHT: 31
            """;

        var (level, warnings) = DfLevelImporter.Import(lShaped, null, new DfImportOptions());
        Assert.Empty(warnings);
        Assert.True(level.Sectors.Count >= 2, $"L-shape should split into ≥2 sectors, got {level.Sectors.Count}");

        // Every produced sector is a closed box: floor + ceiling + n walls, and
        // each surface is convex with a non-degenerate normal.
        foreach (var sec in level.Sectors)
        {
            Assert.True(sec.Surfaces.Count >= 5);
            Assert.True(sec.Surfaces.Count(s => (s.SurfFlags & SurfaceFlags.Floor) != 0) == 1);
            Assert.All(sec.Surfaces, s => Assert.True(s.Normal.Length > 0.9));
        }
    }

    [Fact]
    public void ImportedLevelRoundTripsThroughTheJklWriter()
    {
        var (level, _) = Import();

        var doc = JklParser.ParseDocument("SECTION: HEADER\nVERSION 1\nEND\n");
        doc.Level.Clear();
        foreach (var s in level.Sectors) doc.Level.Sectors.Add(s);
        doc.Level.RenumberSectors();
        foreach (var t in level.Things) doc.Level.Things.Add(t);
        doc.Level.RenumberThings();

        var output = JklWriter.Build(doc);
        var reloaded = JklParser.Parse(output);

        Assert.Equal(level.Sectors.Count, reloaded.Sectors.Count);
        Assert.Equal(level.Things.Count, reloaded.Things.Count);
        Assert.Equal(level.Sectors.Sum(s => s.Surfaces.Count), reloaded.Sectors.Sum(s => s.Surfaces.Count));
        int adjoins = level.Sectors.Sum(s => s.Surfaces.Count(x => x.Adjoin is not null));
        int reloadedAdjoins = reloaded.Sectors.Sum(s => s.Surfaces.Count(x => x.Adjoin is not null));
        Assert.Equal(adjoins, reloadedAdjoins);
    }
}
