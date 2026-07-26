using Sed.Core.Editing;
using Sed.Core.Lighting;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class LightCalculatorTests
{
    /// <summary>A single quad in the XY plane at z=0, facing +Z, 2×2 at the origin.</summary>
    private static (Level level, Sector sector, Surface surf) MakeQuadLevel()
    {
        var level = new Level();
        var sector = level.NewSector();   // NewSector already appends to level.Sectors
        level.RenumberSectors();

        var v0 = sector.AddVertex(new Vec3(-1, -1, 0));
        var v1 = sector.AddVertex(new Vec3(1, -1, 0));
        var v2 = sector.AddVertex(new Vec3(1, 1, 0));
        var v3 = sector.AddVertex(new Vec3(-1, 1, 0));

        var surf = sector.NewSurface();
        surf.Material = "dflt.mat";
        foreach (var v in new[] { v0, v1, v2, v3 })
            surf.Corners.Add(new Surface.Corner { Vertex = v, Uv = new TexVertex(0, 0), Intensity = ColorF.Black });
        surf.RecalcNormal();

        return (level, sector, surf);
    }

    [Fact]
    public void FalloffMatchesTheEngineFormula()
    {
        var (level, _, surf) = MakeQuadLevel();

        // Light directly above the corner at (-1,-1,0), 1 unit away, range 4.
        level.Lights.Add(new Light
        {
            Position = new Vec3(-1, -1, 1),
            Range = 4,
            Intensity = 1.0,
            Color = ColorF.White,
            Flags = LightFlags.NoBlock,
        });

        LightCalculator.Calculate(level, options: new LightingOptions { UpdateSectorAmbients = false });

        // intensity · ((range − dist) / range)²  =  1 · ((4−1)/4)²  =  0.5625
        double expected = System.Math.Pow((4.0 - 1.0) / 4.0, 2);
        Assert.Equal(expected, surf.Corners[0].Intensity.R, 4);
    }

    [Fact]
    public void VerticesBeyondRangeGetNothing()
    {
        var (level, _, surf) = MakeQuadLevel();

        // Range 1.5 reaches the near corner (dist 1) but not the far one (dist ~3.0).
        level.Lights.Add(new Light
        {
            Position = new Vec3(-1, -1, 1),
            Range = 1.5,
            Intensity = 1.0,
            Color = ColorF.White,
            Flags = LightFlags.NoBlock,
        });

        LightCalculator.Calculate(level, options: new LightingOptions { UpdateSectorAmbients = false });

        Assert.True(surf.Corners[0].Intensity.R > 0);
        Assert.Equal(0f, surf.Corners[2].Intensity.R, 6);   // opposite corner, out of range
    }

    [Fact]
    public void LightBehindTheSurfaceContributesNothing()
    {
        var (level, _, surf) = MakeQuadLevel();
        surf.RecalcNormal();

        // Place the light on the opposite side of the plane from the normal.
        var behind = surf.Normal * -1.0;
        level.Lights.Add(new Light
        {
            Position = behind,
            Range = 10,
            Intensity = 1.0,
            Color = ColorF.White,
            Flags = LightFlags.NoBlock,
        });

        LightCalculator.Calculate(level, options: new LightingOptions { UpdateSectorAmbients = false });

        Assert.All(surf.Corners, c => Assert.Equal(0f, c.Intensity.R, 6));
    }

    [Fact]
    public void ContributionsFromMultipleLightsAccumulate()
    {
        var (level, _, surf) = MakeQuadLevel();

        for (int i = 0; i < 2; i++)
            level.Lights.Add(new Light
            {
                Position = new Vec3(-1, -1, 1),
                Range = 4,
                Intensity = 1.0,
                Color = ColorF.White,
                Flags = LightFlags.NoBlock,
            });

        LightCalculator.Calculate(level, options: new LightingOptions { UpdateSectorAmbients = false });

        double single = System.Math.Pow((4.0 - 1.0) / 4.0, 2);
        Assert.Equal(single * 2, surf.Corners[0].Intensity.R, 4);
    }

    [Fact]
    public void JediKnightAccumulatesGreyscale_MotsKeepsColour()
    {
        // JK: a red light still produces a grey vertex intensity.
        var (jk, _, jkSurf) = MakeQuadLevel();
        jk.Kind = ProjectType.JediKnight;
        jk.Lights.Add(new Light
        {
            Position = new Vec3(-1, -1, 1), Range = 4, Intensity = 1.0,
            Color = new ColorF(1, 0, 0), Flags = LightFlags.NoBlock,
        });
        LightCalculator.Calculate(jk, options: new LightingOptions { UpdateSectorAmbients = false });

        var lit = jkSurf.Corners[0].Intensity;
        Assert.Equal(lit.R, lit.G, 6);
        Assert.Equal(lit.G, lit.B, 6);
        Assert.True(lit.R > 0);

        // MotS: the same light tints the vertex red.
        var (mots, _, motsSurf) = MakeQuadLevel();
        mots.Kind = ProjectType.MysteriesOfTheSith;
        mots.Lights.Add(new Light
        {
            Position = new Vec3(-1, -1, 1), Range = 4, Intensity = 1.0,
            Color = new ColorF(1, 0, 0), Flags = LightFlags.NoBlock,
        });
        LightCalculator.Calculate(mots, options: new LightingOptions { UpdateSectorAmbients = false });

        var tinted = motsSurf.Corners[0].Intensity;
        Assert.True(tinted.R > 0);
        Assert.Equal(0f, tinted.G, 6);
        Assert.Equal(0f, tinted.B, 6);
    }

    [Fact]
    public void AnOccludingSurfaceCastsAShadow()
    {
        var (level, sector, surf) = MakeQuadLevel();

        // A blocker quad halfway between the light and the floor, covering it.
        var blocker = sector.NewSurface();
        blocker.Material = "dflt.mat";
        foreach (var p in new[]
                 {
                     new Vec3(-2, -2, 0.5), new Vec3(2, -2, 0.5),
                     new Vec3(2, 2, 0.5), new Vec3(-2, 2, 0.5),
                 })
            blocker.Corners.Add(new Surface.Corner { Vertex = sector.AddVertex(p), Intensity = ColorF.Black });
        blocker.RecalcNormal();

        level.Lights.Add(new Light
        {
            Position = new Vec3(0, 0, 2), Range = 10, Intensity = 1.0, Color = ColorF.White,
        });

        // The light is not in this sector, so the shadow test runs.
        var options = new LightingOptions { CastShadows = true, UpdateSectorAmbients = false };
        var stats = LightCalculator.Calculate(level, new[] { sector }, options);

        Assert.True(stats.Shadowed > 0, "expected the blocker to shadow the floor");
        Assert.All(surf.Corners, c => Assert.Equal(0f, c.Intensity.R, 6));

        // Without shadow casting the same geometry lights up.
        LightCalculator.Calculate(level, new[] { sector },
            new LightingOptions { CastShadows = false, UpdateSectorAmbients = false });
        Assert.True(surf.Corners[0].Intensity.R > 0);
    }

    [Fact]
    public void NoBlockLightIgnoresOccluders()
    {
        var (level, sector, surf) = MakeQuadLevel();

        var blocker = sector.NewSurface();
        foreach (var p in new[]
                 {
                     new Vec3(-2, -2, 0.5), new Vec3(2, -2, 0.5),
                     new Vec3(2, 2, 0.5), new Vec3(-2, 2, 0.5),
                 })
            blocker.Corners.Add(new Surface.Corner { Vertex = sector.AddVertex(p), Intensity = ColorF.Black });
        blocker.RecalcNormal();

        level.Lights.Add(new Light
        {
            Position = new Vec3(0, 0, 2), Range = 10, Intensity = 1.0, Color = ColorF.White,
            Flags = LightFlags.NoBlock,
        });

        LightCalculator.Calculate(level, new[] { sector },
            new LightingOptions { CastShadows = true, UpdateSectorAmbients = false });

        Assert.True(surf.Corners[0].Intensity.R > 0);
    }

    [Fact]
    public void AdjoinsPassLightUnlessFlaggedToBlockIt()
    {
        // Two surfaces adjoined to each other act as a portal: light passes.
        var (level, sector, surf) = MakeQuadLevel();

        var portal = sector.NewSurface();
        foreach (var p in new[]
                 {
                     new Vec3(-2, -2, 0.5), new Vec3(2, -2, 0.5),
                     new Vec3(2, 2, 0.5), new Vec3(-2, 2, 0.5),
                 })
            portal.Corners.Add(new Surface.Corner { Vertex = sector.AddVertex(p), Intensity = ColorF.Black });
        portal.RecalcNormal();

        var mirror = sector.NewSurface();
        foreach (var c in portal.Corners)
            mirror.Corners.Add(new Surface.Corner { Vertex = c.Vertex, Intensity = ColorF.Black });
        mirror.RecalcNormal();

        portal.Adjoin = mirror;
        mirror.Adjoin = portal;

        level.Lights.Add(new Light { Position = new Vec3(0, 0, 2), Range = 10, Intensity = 1.0, Color = ColorF.White });
        var options = new LightingOptions { CastShadows = true, UpdateSectorAmbients = false };

        LightCalculator.Calculate(level, new[] { sector }, options);
        Assert.True(surf.Corners[0].Intensity.R > 0);   // portal let the light through

        // Flagging the adjoin to block light restores the shadow.
        portal.AdjoinFlags = AdjoinFlags.BlockLight;
        mirror.AdjoinFlags = AdjoinFlags.BlockLight;
        LightCalculator.Calculate(level, new[] { sector }, options);
        Assert.Equal(0f, surf.Corners[0].Intensity.R, 6);
    }

    [Fact]
    public void SectorAmbientTakesTheBrighterOfVertexAndExtraLight()
    {
        var (level, sector, surf) = MakeQuadLevel();
        sector.Ambient = ColorF.Black;

        // No lights: vertex light is 0, so surface extra-light wins.
        surf.ExtraLightIntensity = 0.4f;
        LightCalculator.Calculate(level, new[] { sector });
        Assert.Equal(0.4f, sector.Ambient.R, 4);

        // A bright light pushes average vertex light above the extra-light.
        level.Lights.Add(new Light
        {
            Position = new Vec3(0, 0, 0.1), Range = 10, Intensity = 1.0,
            Color = ColorF.White, Flags = LightFlags.NoBlock,
        });
        LightCalculator.Calculate(level, new[] { sector });
        Assert.True(sector.Ambient.R > 0.4f);
    }

    [Fact]
    public void NoAmbientLightFlagLeavesSectorAmbientAlone()
    {
        var (level, sector, surf) = MakeQuadLevel();
        sector.Flags = SectorFlags.NoAmbientLight;
        sector.Ambient = new ColorF(0.25f, 0.25f, 0.25f);
        surf.ExtraLightIntensity = 0.9f;

        LightCalculator.Calculate(level, new[] { sector });

        Assert.Equal(0.25f, sector.Ambient.R, 6);
    }

    [Fact]
    public void BakeIsOneUndoStep_AndRestoresEveryIntensityAndAmbient()
    {
        var (level, sector, surf) = MakeQuadLevel();

        // Hand-authored lighting that must survive an undo.
        for (int i = 0; i < surf.Corners.Count; i++)
            surf.Corners[i].Intensity = new ColorF(0.1f * i, 0.2f, 0.3f);
        sector.Ambient = new ColorF(0.7f, 0.6f, 0.5f);

        var before = surf.Corners.Select(c => c.Intensity).ToList();
        var ambientBefore = sector.Ambient;

        level.Lights.Add(new Light
        {
            Position = new Vec3(0, 0, 1), Range = 8, Intensity = 1.0,
            Color = ColorF.White, Flags = LightFlags.NoBlock,
        });

        var history = new EditHistory();
        var command = new CalculateLightingCommand(level);
        history.Do(command);

        Assert.NotNull(command.Stats);
        Assert.NotEqual(before[0], surf.Corners[0].Intensity);

        history.Undo();
        for (int i = 0; i < surf.Corners.Count; i++)
            Assert.Equal(before[i], surf.Corners[i].Intensity);
        Assert.Equal(ambientBefore, sector.Ambient);
        Assert.False(history.CanUndo);           // one step, not one per vertex

        var afterRedoExpected = command.Stats;
        history.Redo();
        Assert.NotEqual(before[0], surf.Corners[0].Intensity);
        Assert.Same(afterRedoExpected, command.Stats);   // redo replays, does not recompute
    }

    [Fact]
    public void BakingIsIdempotent()
    {
        var (level, _, surf) = MakeQuadLevel();
        level.Lights.Add(new Light
        {
            Position = new Vec3(0, 0, 1), Range = 8, Intensity = 1.0,
            Color = ColorF.White, Flags = LightFlags.NoBlock,
        });

        LightCalculator.Calculate(level, options: new LightingOptions { UpdateSectorAmbients = false });
        var first = surf.Corners.Select(c => c.Intensity).ToList();

        LightCalculator.Calculate(level, options: new LightingOptions { UpdateSectorAmbients = false });

        // The reset pass means a second bake must not double the result.
        for (int i = 0; i < surf.Corners.Count; i++)
            Assert.Equal(first[i].R, surf.Corners[i].Intensity.R, 6);
    }

    [Fact]
    public void FindSectorLocatesAContainingBox()
    {
        var level = new Level();
        var box = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "dflt.mat", 0);
        level.Sectors.Add(box);
        level.RenumberSectors();

        Assert.Same(box, LightCalculator.FindSector(level, Vec3.Zero));
        Assert.Null(LightCalculator.FindSector(level, new Vec3(50, 50, 50)));
    }
}
