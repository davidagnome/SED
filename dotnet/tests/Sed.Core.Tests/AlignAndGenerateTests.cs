using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Cogs;
using Xunit;

namespace Sed.Core.Tests;

public class AlignTextureTests
{
    /// <summary>
    /// Two quads meeting at the edge x=0: the reference lies flat in the XY plane
    /// (y from 0 to 1, x from -1 to 0) and the target folds up into XZ, so they
    /// share the edge from (0,0,0) to (0,1,0) but are not coplanar.
    /// </summary>
    private static (Surface reference, Surface target) MakeFoldedPair()
    {
        var level = new Level();
        var sector = level.NewSector();

        Surface Quad(params Vec3[] points)
        {
            var surf = sector.NewSurface();
            surf.Material = "dflt.mat";
            foreach (var p in points)
                surf.Corners.Add(new Surface.Corner
                {
                    Vertex = sector.AddVertex(p),
                    Uv = new TexVertex(0, 0),
                    Intensity = ColorF.White,
                });
            surf.RecalcNormal();
            return surf;
        }

        // Reference: 1×1 quad with a straightforward 0..64 texel mapping.
        var reference = Quad(
            new Vec3(-1, 0, 0), new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(-1, 1, 0));
        reference.Corners[0].Uv = new TexVertex(0, 0);
        reference.Corners[1].Uv = new TexVertex(64, 0);
        reference.Corners[2].Uv = new TexVertex(64, 64);
        reference.Corners[3].Uv = new TexVertex(0, 64);

        // Target: folds up from the shared edge, UVs deliberately wrong.
        var target = Quad(
            new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 1, 1), new Vec3(0, 0, 1));

        return (reference, target);
    }

    [Fact]
    public void ValidationRequiresASharedEdge()
    {
        var (reference, target) = MakeFoldedPair();
        Assert.Null(AlignTextureToNeighbourCommand.Validate(target, reference));
        Assert.Contains("different surfaces", AlignTextureToNeighbourCommand.Validate(target, target));

        // A quad somewhere else entirely shares nothing.
        var level = new Level();
        var sector = level.NewSector();
        var lonely = sector.NewSurface();
        foreach (var p in new[]
                 {
                     new Vec3(50, 0, 0), new Vec3(51, 0, 0), new Vec3(51, 1, 0), new Vec3(50, 1, 0),
                 })
            lonely.Corners.Add(new Surface.Corner { Vertex = sector.AddVertex(p), Intensity = ColorF.White });
        lonely.RecalcNormal();

        Assert.Contains("do not share an edge", AlignTextureToNeighbourCommand.Validate(target, lonely));
    }

    [Fact]
    public void UvsMatchExactlyAlongTheSharedEdge()
    {
        var (reference, target) = MakeFoldedPair();
        new AlignTextureToNeighbourCommand(target, reference).Apply();

        // Corners at (0,0,0) and (0,1,0) are on the seam and must carry the
        // reference's UVs there — (64,0) and (64,64).
        var atOrigin = target.Corners.First(c => (c.Vertex.Position - new Vec3(0, 0, 0)).LengthSquared < 1e-12);
        var atFar = target.Corners.First(c => (c.Vertex.Position - new Vec3(0, 1, 0)).LengthSquared < 1e-12);

        Assert.Equal(64, atOrigin.Uv.U, 4);
        Assert.Equal(0, atOrigin.Uv.V, 4);
        Assert.Equal(64, atFar.Uv.U, 4);
        Assert.Equal(64, atFar.Uv.V, 4);
    }

    [Fact]
    public void TextureScaleCarriesOntoTheFoldedFace()
    {
        var (reference, target) = MakeFoldedPair();
        new AlignTextureToNeighbourCommand(target, reference).Apply();

        // The reference maps one world unit to 64 texels. A corner one unit away
        // from the seam must therefore be 64 texels further along.
        var away = target.Corners.First(c => (c.Vertex.Position - new Vec3(0, 0, 1)).LengthSquared < 1e-12);
        var seam = target.Corners.First(c => (c.Vertex.Position - new Vec3(0, 0, 0)).LengthSquared < 1e-12);

        double distance = System.Math.Sqrt(
            System.Math.Pow(away.Uv.U - seam.Uv.U, 2) + System.Math.Pow(away.Uv.V - seam.Uv.V, 2));
        Assert.Equal(64, distance, 3);
    }

    [Fact]
    public void AligningIsReversible()
    {
        var (reference, target) = MakeFoldedPair();
        var before = target.Corners.Select(c => c.Uv).ToList();

        var history = new EditHistory();
        history.Do(new AlignTextureToNeighbourCommand(target, reference));
        Assert.NotEqual(before[0].U, target.Corners[0].Uv.U);

        history.Undo();
        for (int i = 0; i < target.Corners.Count; i++)
        {
            Assert.Equal(before[i].U, target.Corners[i].Uv.U, 6);
            Assert.Equal(before[i].V, target.Corners[i].Uv.V, 6);
        }
    }
}

public class MasterCogGeneratorTests
{
    [Fact]
    public void GeneratesTheMasterCogSkeleton()
    {
        var cog = MasterCogGenerator.Generate(new MasterCogOptions());

        Assert.Contains("#Level master COG", cog);
        Assert.Contains("symbols", cog);
        Assert.Contains("message   startup", cog);
        Assert.Contains("SetMasterCOG(GetSelfCOG());", cog);
        Assert.Contains("player = GetLocalPlayerThing();", cog);
        Assert.Contains("jkSyncForcePowers();", cog);
        Assert.Contains("SetInv(player, 1, 1);   // fists", cog);   // always granted
    }

    [Fact]
    public void GeneratedScriptParsesAsAValidCogScript()
    {
        var text = MasterCogGenerator.Generate(new MasterCogOptions
        {
            Goals = new[] { "Escape", "Find the key" },
            Weapons = JkWeapons.Briar | JkWeapons.Lightsaber,
        });

        // Round-trip through the parser the COG editor uses.
        var script = CogScript.Parse("master.cog", text);

        Assert.Equal(3, script.Symbols.Count);
        Assert.Contains(script.Symbols, s => s is { Name: "startup", Type: CogSymbolType.Message });
        Assert.Contains(script.Symbols, s => s is { Name: "player", Local: true });

        // Only messages and a local, so the level supplies nothing.
        Assert.Empty(script.LevelValues);
    }

    [Fact]
    public void OneGoalFlagPerGoal()
    {
        var cog = MasterCogGenerator.Generate(new MasterCogOptions
        {
            GoalBase = 30,
            Goals = new[] { "a", "b", "c" },
        });

        Assert.Contains("SetInv(player, 99, 30);", cog);
        Assert.Contains("SetGoalFlags(player, 0, 1);", cog);
        Assert.Contains("SetGoalFlags(player, 1, 1);", cog);
        Assert.Contains("SetGoalFlags(player, 2, 1);", cog);
        Assert.DoesNotContain("SetGoalFlags(player, 3, 1);", cog);
    }

    [Fact]
    public void AmmoIsGrantedOnlyForWeaponsThatUseIt()
    {
        // Energy weapons only.
        var energy = MasterCogGenerator.Generate(new MasterCogOptions { Weapons = JkWeapons.Briar });
        Assert.Contains("SetInv(player, 11, 100);   // Energy", energy);
        Assert.DoesNotContain("// Power", energy);
        Assert.DoesNotContain("// Railcharges", energy);

        // Power weapons only.
        var power = MasterCogGenerator.Generate(new MasterCogOptions { Weapons = JkWeapons.Repeater });
        Assert.Contains("SetInv(player, 12, 100);   // Power", power);
        Assert.DoesNotContain("// Energy", power);

        // Railgun brings its own charges.
        var rail = MasterCogGenerator.Generate(new MasterCogOptions { Weapons = JkWeapons.Railgun });
        Assert.Contains("SetInv(player, 15, 10);   // Railcharges", rail);

        // No weapons at all: fists only, no ammunition.
        var none = MasterCogGenerator.Generate(new MasterCogOptions());
        Assert.DoesNotContain("// Energy", none);
        Assert.DoesNotContain("// Power", none);
        Assert.DoesNotContain("// Railcharges", none);
    }

    [Fact]
    public void ForceRankGoesIntoTheTimerBlock()
    {
        var cog = MasterCogGenerator.Generate(new MasterCogOptions { ForceRank = 5 });

        Assert.Contains("SetInv(player, 20, 5);", cog);
        Assert.Contains("timer:", cog);
    }

    [Fact]
    public void GoalKeysUseTheOriginalsFiveDigitFormat()
    {
        Assert.Equal("GOAL_00000", MasterCogGenerator.GoalKey(0, 0));
        Assert.Equal("GOAL_00042", MasterCogGenerator.GoalKey(40, 2));
        Assert.Equal("GOAL_01234", MasterCogGenerator.GoalKey(1234, 0));
    }
}
