using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Core.Query;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>Per-field query criteria (the original's Q_SECTORS/Q_SURFS/Q_THINGS builders).</summary>
public class FieldQueryTests
{
    private static Level MakeLevel()
    {
        var level = new Level();
        level.AddLayer("level1");

        var a = SectorFactory.CreateBox(level, new Vec3(0, 0, 0), 1.0, "wall.mat", 0);
        var b = SectorFactory.CreateBox(level, new Vec3(3, 0, 0), 1.0, "panel.mat", 0);
        a.Flags = 0x200;
        a.Layer = 0;
        b.Sound = "wind";
        level.Sectors.Add(a);
        level.Sectors.Add(b);

        // One adjoined pair between the boxes.
        var fa = a.Surfaces[0];
        var fb = b.Surfaces[0];
        fa.Adjoin = fb;
        fb.Adjoin = fa;

        var thing = new Thing { Name = "crate", Template = "woodcrate", Level = level, Layer = 0 };
        thing.Position = new Vec3(1, 2, 3);
        level.Things.Add(thing);
        level.Things.Add(new Thing { Name = "walkplayer", Template = "player", Level = level });
        level.RenumberSectors();
        level.RenumberThings();
        return level;
    }

    private static List<FindResult> Run(Level level, FindKind kind, params FieldCriterion[] criteria) =>
        LevelQuery.Run(level, new FindQuery { Kind = kind, Fields = criteria });

    [Fact]
    public void SectorFlagMaskWithSetOperator()
    {
        var level = MakeLevel();
        var hits = Run(level, FindKind.Sector,
            new FieldCriterion { Field = FindField.Flags, Op = CompareOp.Contains, Long = 0x200 });

        var hit = Assert.Single(hits);
        Assert.Equal(0, hit.Index);
    }

    [Fact]
    public void SurfaceMaterialContainsIsCaseInsensitive()
    {
        var level = MakeLevel();
        var hits = Run(level, FindKind.Surface,
            new FieldCriterion { Field = FindField.Material, Op = CompareOp.Contains, Text = "WALL" });

        Assert.Equal(6, hits.Count); // the box's six faces share the material
        Assert.All(hits, h => Assert.Equal("wall.mat", h.Surface!.Material));
    }

    [Fact]
    public void AdjoinedSectorEqualsFindsThePortalPair()
    {
        var level = MakeLevel();
        var hits = Run(level, FindKind.Surface,
            new FieldCriterion { Field = FindField.AdjoinSector, Op = CompareOp.Equal, Long = 1 });

        var hit = Assert.Single(hits);
        Assert.Equal(0, hit.Surface!.Sector.Num);
        Assert.Equal(1, hit.Surface.Adjoin!.Sector.Num);
    }

    [Fact]
    public void ThingNameEqualsAndTemplate()
    {
        var level = MakeLevel();
        var hits = Run(level, FindKind.Thing,
            new FieldCriterion { Field = FindField.Name, Op = CompareOp.Equal, Text = "crate" },
            new FieldCriterion { Field = FindField.Template, Op = CompareOp.Equal, Text = "woodcrate" });

        var hit = Assert.Single(hits);
        Assert.Equal("crate", hit.Thing!.Name);
    }

    [Fact]
    public void NumericComparisonOperators()
    {
        var level = MakeLevel();
        // Only the crate sits at X = 1; the player is at the origin.
        var above = Run(level, FindKind.Thing,
            new FieldCriterion { Field = FindField.X, Op = CompareOp.Above, Number = 0 });
        var hit = Assert.Single(above);
        Assert.Equal("crate", hit.Thing!.Name);

        var below = Run(level, FindKind.Thing,
            new FieldCriterion { Field = FindField.X, Op = CompareOp.Below, Number = 0 });
        Assert.Empty(below);
    }

    [Fact]
    public void LayerNameCriterion()
    {
        var level = MakeLevel();
        var hits = Run(level, FindKind.Sector,
            new FieldCriterion { Field = FindField.Layer, Op = CompareOp.Equal, Text = "level1" });

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void InactiveCriteriaAreIgnored()
    {
        var level = MakeLevel();
        var hits = Run(level, FindKind.Sector,
            new FieldCriterion { Field = FindField.Flags, Op = CompareOp.None, Long = 0xFFFFFFFF });

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void ColorCriteriaParseAndMatch()
    {
        var level = new Level();
        var box = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "m.mat", 0);
        box.Tint = new ColorF(1f, 0f, 0f);
        level.Sectors.Add(box);
        level.RenumberSectors();

        var hits = Run(level, FindKind.Sector,
            new FieldCriterion { Field = FindField.Tint, Op = CompareOp.Equal, Color = new ColorF(1f, 0f, 0f) });
        Assert.Single(hits);

        var none = Run(level, FindKind.Sector,
            new FieldCriterion { Field = FindField.Tint, Op = CompareOp.Equal, Color = new ColorF(0f, 0f, 1f) });
        Assert.Empty(none);
    }

    [Fact]
    public void ColorTextParsesComponents()
    {
        Assert.True(FieldCriteria.TryParseColor("1 0 0", out var c1));
        Assert.Equal(1f, c1.R);
        Assert.Equal(0f, c1.G);

        Assert.True(FieldCriteria.TryParseColor("255/0/0", out var c2));
        Assert.Equal(1f, c2.R, 4);

        Assert.False(FieldCriteria.TryParseColor("banana", out _));
    }

    [Fact]
    public void SurfaceCountCriterion()
    {
        var level = MakeLevel();
        var hits = Run(level, FindKind.Sector,
            new FieldCriterion { Field = FindField.NSurfs, Op = CompareOp.Equal, Long = 6 });

        Assert.Equal(2, hits.Count);
    }
}
