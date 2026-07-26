using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Core.Query;
using Xunit;

namespace Sed.Core.Tests;

public class LevelQueryTests
{
    private static Level MakeLevel()
    {
        var level = new Level();

        var a = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "wall.mat", 0);
        a.ColorMap = "dark.cmp";
        a.Sound = "hum.wav";
        a.Flags = SectorFlags.Underwater;

        var b = SectorFactory.CreateBox(level, new Vec3(10, 0, 0), 1.0, "floor.mat", 1);
        b.ColorMap = "bright.cmp";

        level.Sectors.Add(a);
        level.Sectors.Add(b);
        level.RenumberSectors();
        foreach (var s in level.Sectors) s.Renumber();

        // Give one surface a distinguishing flag.
        a.Surfaces[0].SurfFlags = SurfaceFlags.SkyCeiling;

        level.Things.Add(new Thing { Name = "player_start", Template = "walkplayer", Sector = a });
        level.Things.Add(new Thing { Name = "crate01", Template = "crate", Sector = b });
        level.Things.Add(new Thing { Name = "crate02", Template = "crate", Sector = b });
        level.RenumberThings();

        level.Lights.Add(new Light { Position = new Vec3(0, 0, 2), Range = 5, Intensity = 1 });
        level.Lights.Add(new Light { Position = new Vec3(10, 0, 2), Range = 8, Intensity = 2, Flags = LightFlags.NoBlock });
        level.RenumberLights();

        return level;
    }

    [Fact]
    public void EmptyTextReturnsEverythingOfThatKind()
    {
        var level = MakeLevel();

        Assert.Equal(2, LevelQuery.Run(level, new FindQuery { Kind = FindKind.Sector }).Count);
        Assert.Equal(3, LevelQuery.Run(level, new FindQuery { Kind = FindKind.Thing }).Count);
        Assert.Equal(2, LevelQuery.Run(level, new FindQuery { Kind = FindKind.Light }).Count);
        Assert.Equal(12, LevelQuery.Run(level, new FindQuery { Kind = FindKind.Surface }).Count);
    }

    [Fact]
    public void SurfacesMatchByMaterialSubstring_CaseInsensitively()
    {
        var level = MakeLevel();

        var hits = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Surface, Text = "FLOOR" });

        Assert.Equal(6, hits.Count);   // every face of the second box
        Assert.All(hits, h => Assert.Equal("floor.mat", h.Surface!.Material));
    }

    [Fact]
    public void ThingsMatchByNameOrTemplate()
    {
        var level = MakeLevel();

        var byName = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Thing, Text = "player" });
        Assert.Single(byName);
        Assert.Equal("player_start", byName[0].Thing!.Name);

        var byTemplate = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Thing, Text = "crate" });
        Assert.Equal(2, byTemplate.Count);
    }

    [Fact]
    public void NumericTextMatchesTheIndex()
    {
        var level = MakeLevel();

        var hits = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Thing, Text = "2" });

        // Thing 2 by index; "crate02" also contains "2", so both are legitimate hits.
        Assert.Contains(hits, h => h.Thing!.Num == 2);
    }

    [Fact]
    public void SectorsMatchByColormapAndSound()
    {
        var level = MakeLevel();

        Assert.Single(LevelQuery.Run(level, new FindQuery { Kind = FindKind.Sector, Text = "bright" }));
        Assert.Single(LevelQuery.Run(level, new FindQuery { Kind = FindKind.Sector, Text = "hum.wav" }));
    }

    [Fact]
    public void FlagMaskFiltersResults()
    {
        var level = MakeLevel();

        var underwater = LevelQuery.Run(level, new FindQuery
        {
            Kind = FindKind.Sector,
            FlagMask = SectorFlags.Underwater,
        });
        Assert.Single(underwater);
        Assert.Equal(0, underwater[0].Index);

        var sky = LevelQuery.Run(level, new FindQuery
        {
            Kind = FindKind.Surface,
            FlagMask = SurfaceFlags.SkyCeiling,
        });
        Assert.Single(sky);

        var noBlock = LevelQuery.Run(level, new FindQuery
        {
            Kind = FindKind.Light,
            FlagMask = LightFlags.NoBlock,
        });
        Assert.Single(noBlock);
        Assert.Equal(1, noBlock[0].Index);
    }

    [Fact]
    public void TextAndFlagMaskCombine()
    {
        var level = MakeLevel();

        // "wall" matches the first box's six faces, but only one carries the flag.
        var hits = LevelQuery.Run(level, new FindQuery
        {
            Kind = FindKind.Surface,
            Text = "wall",
            FlagMask = SurfaceFlags.SkyCeiling,
        });

        Assert.Single(hits);
    }

    [Fact]
    public void NoMatchReturnsEmpty()
    {
        var level = MakeLevel();

        Assert.Empty(LevelQuery.Run(level, new FindQuery { Kind = FindKind.Thing, Text = "nonexistent" }));
        Assert.Empty(LevelQuery.Run(level, new FindQuery { Kind = FindKind.Sector, FlagMask = 0x40000000 }));
    }

    [Fact]
    public void ResultsCarryAJumpPosition()
    {
        var level = MakeLevel();

        // Sector 1's box is centred at x = 10, so its centroid should be too.
        var sector = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Sector, Text = "bright" })[0];
        Assert.Equal(10.0, sector.Position.X, 6);

        // A thing result reports the thing's own position.
        level.Things[0].Position = new Vec3(3, 4, 5);
        var thing = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Thing, Text = "player" })[0];
        Assert.Equal(new Vec3(3, 4, 5), thing.Position);
    }

    [Fact]
    public void ResultsReferenceTheModelObject()
    {
        var level = MakeLevel();

        var surface = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Surface, Text = "floor" })[0];
        Assert.NotNull(surface.Surface);
        Assert.NotNull(surface.Sector);
        Assert.Null(surface.Thing);
        Assert.Same(level.Sectors[1], surface.Sector);

        var light = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Light })[0];
        Assert.NotNull(light.Light);
        Assert.Null(light.Surface);
    }

    [Fact]
    public void LimitCapsTheResultList()
    {
        var level = MakeLevel();

        var hits = LevelQuery.Run(level, new FindQuery { Kind = FindKind.Surface, Limit = 4 });

        Assert.Equal(4, hits.Count);
    }
}
