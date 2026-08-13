using Sed.Formats.Game;
using Xunit;

namespace Sed.Core.Tests;

public class EpisodeFileTests
{
    private const string Episode = """
        "Nar Shaddaa"
        TYPE 1
        SEQ 2
        # <line> <cd>  <level>  <type>   <file>         <lightpow>  <darkpow>   <gotoA>  <gotoB>

        10:	0	1	LEVEL	01narshadda.jkl	0	0	-1	-1
        20:	0	2	LEVEL	03katarn.jkl	0	0	10	-1
        end
        """;

    [Fact]
    public void ParsesTheEpisodeHeaderAndSequences()
    {
        var episode = EpisodeFile.Parse(Episode);

        Assert.Equal("Nar Shaddaa", episode.Name);
        Assert.Equal(1, episode.GameType);
        Assert.Equal(2, episode.Sequences.Count);

        var first = episode.Sequences[0];
        Assert.Equal(10, first.Line);
        Assert.Equal(0, first.Cd);
        Assert.Equal(1, first.LevelNum);
        Assert.Equal("LEVEL", first.Type);
        Assert.Equal("01narshadda.jkl", first.File);
        Assert.Equal(0, first.LightPow);
        Assert.Equal(-1, first.GotoA);
        Assert.Equal(-1, first.GotoB);

        var second = episode.Sequences[1];
        Assert.Equal(20, second.Line);
        Assert.Equal("03katarn.jkl", second.File);
        Assert.Equal(10, second.GotoA);
    }

    [Fact]
    public void RoundTripsThroughTheWriter()
    {
        var episode = EpisodeFile.Parse(Episode);
        var rebuilt = EpisodeFile.Parse(episode.Build());

        Assert.Equal(episode.Name, rebuilt.Name);
        Assert.Equal(episode.GameType, rebuilt.GameType);
        Assert.Equal(episode.Sequences.Count, rebuilt.Sequences.Count);
        for (int i = 0; i < episode.Sequences.Count; i++)
        {
            Assert.Equal(episode.Sequences[i].Line, rebuilt.Sequences[i].Line);
            Assert.Equal(episode.Sequences[i].File, rebuilt.Sequences[i].File);
            Assert.Equal(episode.Sequences[i].GotoA, rebuilt.Sequences[i].GotoA);
        }
    }

    [Fact]
    public void MissingFileStartsEmpty()
    {
        var episode = EpisodeFile.Parse(string.Empty);
        Assert.Empty(episode.Sequences);
        Assert.Equal(string.Empty, episode.Name);
    }

    [Fact]
    public void CogStringsParseBuildAndRoundTrip()
    {
        const string uni = """
            MSGS 3
            #  "<key>"     <unused number>   "<string>"

            "01narshadda" 0 "The Perfect Weapon"
            "01narshadda_TEXT_00" 0 "First line"
            "01narshadda_TEXT_01" 0 "Second line"
            END
            """;

        var strings = CogStrings.Parse(uni);
        Assert.Equal(3, strings.Entries.Count);
        Assert.Equal("The Perfect Weapon", strings.GetString("01narshadda"));
        Assert.Equal("First line", strings.GetString("01narshadda_TEXT_00"));
        Assert.Equal("", strings.GetString("missing"));

        var rebuilt = CogStrings.Parse(strings.Build());
        Assert.Equal(strings.Entries.Count, rebuilt.Entries.Count);
        Assert.Equal("Second line", rebuilt.GetString("01narshadda_TEXT_01"));
    }

    [Fact]
    public void CogStringsSetAndRemove()
    {
        var strings = new CogStrings();
        strings.SetString("key", "value");
        Assert.Equal("value", strings.GetString("key"));

        // Setting the same key replaces, not duplicates.
        strings.SetString("key", "new");
        Assert.Single(strings.Entries);
        Assert.Equal("new", strings.GetString("key"));

        strings.RemoveString("key");
        Assert.Empty(strings.Entries);
    }
}
