using Sed.Core.Editing;
using Sed.Core.Model;
using Sed.Formats.Cogs;
using Sed.Formats.Jkl;
using Xunit;

namespace Sed.Core.Tests;

public class CogScriptTests
{
    // Shaped after retail scripts: tab-separated, defaults, locals, comments,
    // and a `desc=` modifier.
    private const string Script = """
        # Jedi Knight Cog Script
        #
        # 00_door.cog

        symbols

        message	startup
        message	activate

        thing		door0
        thing		door1
        thing		player		local
        sector	inside			// The sector that opens the door
        sound		locked=i00ky73t.wav	local
        flex		moveSpeed=8.0		desc=how_fast
        int		count=3

        end

        # ========================================================================

        code

        startup:
        	return;

        end
        """;

    [Fact]
    public void ParsesSymbolsWithTypesDefaultsAndLocals()
    {
        var script = CogScript.Parse("00_door.cog", Script);

        Assert.Equal(9, script.Symbols.Count);

        var door0 = script.Symbols.First(s => s.Name == "door0");
        Assert.Equal(CogSymbolType.Thing, door0.Type);
        Assert.False(door0.Local);
        Assert.Null(door0.Default);

        var player = script.Symbols.First(s => s.Name == "player");
        Assert.True(player.Local);

        var speed = script.Symbols.First(s => s.Name == "moveSpeed");
        Assert.Equal(CogSymbolType.Flex, speed.Type);
        Assert.Equal("8.0", speed.Default);
        Assert.Equal("how_fast", speed.Description);

        var locked = script.Symbols.First(s => s.Name == "locked");
        Assert.Equal("i00ky73t.wav", locked.Default);
        Assert.True(locked.Local);
    }

    [Fact]
    public void LevelValuesExcludesLocalsAndMessages()
    {
        var script = CogScript.Parse("00_door.cog", Script);

        // door0, door1, inside, moveSpeed, count — not the messages, not the locals.
        Assert.Equal(
            new[] { "door0", "door1", "inside", "moveSpeed", "count" },
            script.LevelValues.Select(s => s.Name));
    }

    [Fact]
    public void CommentsBecomeDescriptions()
    {
        var script = CogScript.Parse("00_door.cog", Script);
        var inside = script.LevelValues.First(s => s.Name == "inside");

        Assert.Equal(CogSymbolType.Sector, inside.Type);
        Assert.Equal("The sector that opens the door", inside.Description);
    }

    [Fact]
    public void ContentOutsideTheSymbolsBlockIsIgnored()
    {
        var script = CogScript.Parse("x.cog", Script);

        // The `code` section has a "startup:" label that is not a symbol.
        Assert.DoesNotContain(script.Symbols, s => s.Name.Contains(':'));
        Assert.All(script.Symbols, s => Assert.NotEqual(CogSymbolType.Unknown, s.Type));
    }

    [Fact]
    public void AScriptWithoutASymbolsBlockParsesEmpty()
    {
        var script = CogScript.Parse("bare.cog", "code\nstartup:\nreturn;\nend\n");

        Assert.Empty(script.Symbols);
        Assert.Empty(script.LevelValues);
    }

    [Fact]
    public void SetCogValuePadsRatherThanLeavingAGap()
    {
        var cog = new Cog { Name = "00_door.cog" };
        cog.Values.Add("13");

        var history = new EditHistory();
        history.Do(new SetCogValueCommand(cog, 3, "99"));

        // Positions 1 and 2 are filled, so later symbols keep their meaning.
        Assert.Equal(new[] { "13", "0", "0", "99" }, cog.Values);

        history.Undo();
        Assert.Equal(new[] { "13" }, cog.Values);
    }

    [Fact]
    public void ChangingTheScriptClearsValuesBecauseTheLayoutChanged()
    {
        var cog = new Cog { Name = "00_door.cog" };
        cog.Values.AddRange(new[] { "1", "2", "3" });

        var history = new EditHistory();
        history.Do(new SetCogScriptCommand(cog, "00_elevator.cog"));

        Assert.Equal("00_elevator.cog", cog.Name);
        Assert.Empty(cog.Values);

        history.Undo();
        Assert.Equal("00_door.cog", cog.Name);
        Assert.Equal(new[] { "1", "2", "3" }, cog.Values);
    }

    [Fact]
    public void DeleteRestoresACogAtItsOriginalIndex()
    {
        var level = new Level();
        foreach (var name in new[] { "a.cog", "b.cog", "c.cog" })
            level.Cogs.Add(new Cog { Name = name });
        for (int i = 0; i < level.Cogs.Count; i++) level.Cogs[i].Num = i;

        var middle = level.Cogs[1];
        var history = new EditHistory();
        history.Do(new DeleteCogCommand(level, middle));

        Assert.Equal(new[] { "a.cog", "c.cog" }, level.Cogs.Select(c => c.Name));
        Assert.Equal(1, level.Cogs[1].Num);

        // Scripts reference each other by COG number, so undo must put it back
        // at index 1 rather than appending it.
        history.Undo();
        Assert.Equal(new[] { "a.cog", "b.cog", "c.cog" }, level.Cogs.Select(c => c.Name));
        Assert.Equal(1, middle.Num);
        Assert.Equal(2, level.Cogs[2].Num);
    }

    [Fact]
    public void CreateCogIsReversibleAndRenumbers()
    {
        var level = new Level();
        level.Cogs.Add(new Cog { Name = "a.cog", Num = 0 });

        var fresh = new Cog { Name = "b.cog" };
        var history = new EditHistory();
        history.Do(new CreateCogCommand(level, fresh));

        Assert.Equal(2, level.Cogs.Count);
        Assert.Equal(1, fresh.Num);

        history.Undo();
        Assert.Single(level.Cogs);

        history.Redo();
        Assert.Equal(2, level.Cogs.Count);
    }

    [Fact]
    public void EditedCogsRoundTripThroughTheWriter()
    {
        const string jkl = """
            SECTION: COGS

            World cogs 2
            0:	00_door.cog	13	14	8.000000
            1:	pow_health.cog
            end

            SECTION: THINGS

            World things 0
            end
            """;

        var doc = JklParser.ParseDocument(jkl);
        var level = doc.Level;
        Assert.Equal(2, level.Cogs.Count);
        Assert.Equal(new[] { "13", "14", "8.000000" }, level.Cogs[0].Values);

        new SetCogValueCommand(level.Cogs[0], 1, "27").Apply();
        new CreateCogCommand(level, new Cog { Name = "extra.cog" }).Apply();

        var reloaded = JklParser.Parse(JklWriter.Build(doc));

        Assert.Equal(3, reloaded.Cogs.Count);
        Assert.Equal(new[] { "13", "27", "8.000000" }, reloaded.Cogs[0].Values);
        Assert.Equal("extra.cog", reloaded.Cogs[2].Name);
        Assert.Empty(reloaded.Cogs[1].Values);
    }
}
