using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Jkl;
using Xunit;

namespace Sed.Core.Tests;

public class SectionRoundTripTests
{
    private const string Jkl = """
        SECTION: HEADER
        VERSION 1
        WORLD GRAVITY 4.00000000
        CEILING SKY Z 15.00000000
        HORIZON DISTANCE 100.00000000
        HORIZON PIXELS PER REV 768.000000
        HORIZON SKY OFFSET 0.00000000 0.00000000
        CEILING SKY OFFSET 0.00000000 0.00000000
        MIPMAP DISTANCES 1.000000 2.000000 3.000000 4.000000
        LOD DISTANCES 0.300000 0.600000 0.900000 1.200000
        PERSPECTIVE DISTANCE 2.000000
        GOURAUD DISTANCE 2.000000
        END

        SECTION: GEORESOURCE
        WORLD COLORMAPS 1
        0:	dflt.cmp
        WORLD VERTICES 4
        0:	-1.0 -1.0 0.0
        1:	1.0 -1.0 0.0
        2:	1.0 1.0 0.0
        3:	-1.0 1.0 0.0
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
        SURFACES 0 1
        END

        SECTION: TEMPLATES
        World templates 2
        +walkplayer      _actor    model3d=ky.3do
        +weapon          _actor    model3d=w.3do

        SECTION: COGS
        World cogs 1
        0:	special.cog	0	1	2
        END

        SECTION: THINGS
        WORLD THINGS 1
        0:	walkplayer	Player	0.0 0.0 0.5 0.0 0.0 0.0 0
        END

        SECTION: LIGHTS
        Editor lights 2
        0: 0x0 0 1.0 2.0 3.0 5.0 1.0
        1: 0x1 0 0.0 0.0 0.0 3.0 0.5
        END

        SECTION: LAYERS
        Editor layers 2
        Layer0
        1:	0
        1:	0
        Layer1
        0:
        0:
        END
        """;

    [Fact]
    public void SectionAbsentFromSource_IsAppendedRatherThanDropped()
    {
        // Retail levels carry no LIGHTS or LAYERS section — those are authored by
        // the editor. The patch writer used to rewrite only sections it could
        // already find, so a light placed in such a level vanished on save.
        var withoutLights = Jkl
            .Replace("SECTION: LIGHTS", "SECTION: UNUSEDLIGHTS");

        var doc = JklParser.ParseDocument(withoutLights);
        Assert.Empty(doc.Level.Lights);

        var light = doc.Level.NewLight();
        light.Position = new Vec3(1, 2, 3);
        light.Range = 7.5;
        light.Intensity = 0.5;

        var output = JklWriter.Build(doc);
        Assert.Contains("SECTION: LIGHTS", output);

        var reloaded = JklParser.Parse(output);
        var back = Assert.Single(reloaded.Lights);
        Assert.Equal(new Vec3(1, 2, 3), back.Position);
        Assert.Equal(7.5, back.Range, 5);
    }

    [Fact]
    public void EmptySectionsAreNotInventedOnUntouchedLevels()
    {
        // A level with no lights must not gain an empty LIGHTS section just
        // because it was opened and saved.
        var withoutLights = Jkl.Replace("SECTION: LIGHTS", "SECTION: UNUSEDLIGHTS");

        var doc = JklParser.ParseDocument(withoutLights);
        var output = JklWriter.Build(doc);

        Assert.DoesNotContain("SECTION: LIGHTS", output);
    }

    [Fact]
    public void Lights_RoundTrip()
    {
        var doc = JklParser.ParseDocument(Jkl);
        Assert.Equal(2, doc.Level.Lights.Count);

        doc.Level.Lights[0].Position = new Vec3(10, 20, 30);

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(2, reloaded.Lights.Count);
        Assert.Equal(new Vec3(10, 20, 30), reloaded.Lights[0].Position);
        Assert.Equal(5.0, reloaded.Lights[0].Range, 5);
    }

    [Fact]
    public void Lights_Added_PersistsThroughSave()
    {
        var doc = JklParser.ParseDocument(Jkl);
        var light = doc.Level.NewLight();
        light.Position = new Vec3(5, 6, 7);
        light.Intensity = 2.0;
        light.Range = 10.0;

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(3, reloaded.Lights.Count);
    }

    [Fact]
    public void Cogs_RoundTrip()
    {
        var doc = JklParser.ParseDocument(Jkl);
        Assert.Single(doc.Level.Cogs);
        Assert.Equal("special.cog", doc.Level.Cogs[0].Name);
        Assert.Equal(3, doc.Level.Cogs[0].Values.Count);

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Single(reloaded.Cogs);
        Assert.Equal("special.cog", reloaded.Cogs[0].Name);
        Assert.Equal(3, reloaded.Cogs[0].Values.Count);
        Assert.Equal("0", reloaded.Cogs[0].Values[0]);
        Assert.Equal("1", reloaded.Cogs[0].Values[1]);
        Assert.Equal("2", reloaded.Cogs[0].Values[2]);
    }

    [Fact]
    public void Header_RoundTrip()
    {
        var doc = JklParser.ParseDocument(Jkl);
        Assert.Equal(4.0f, doc.Level.Header.Gravity, 5);
        Assert.Equal(15.0f, doc.Level.Header.CeilingSky.Height, 5);
        Assert.Equal(100.0f, doc.Level.Header.HorizonSky.Distance, 5);
        Assert.Equal(768.0f, doc.Level.Header.HorizonSky.PixelsPerRev, 5);
        Assert.Equal(1.0f, doc.Level.Header.MipmapDistances[0], 5);
        Assert.Equal(4.0f, doc.Level.Header.MipmapDistances[3], 5);
        Assert.Equal(0.3f, doc.Level.Header.LodDistances[0], 5);
        Assert.Equal(2.0f, doc.Level.Header.PerspectiveDistance, 5);

        doc.Level.Header.Gravity = 6.0f;

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(6.0f, reloaded.Header.Gravity, 5);
        Assert.Equal(15.0f, reloaded.Header.CeilingSky.Height, 5);
        Assert.Equal(768.0f, reloaded.Header.HorizonSky.PixelsPerRev, 5);
        Assert.Equal(4.0f, reloaded.Header.MipmapDistances[3], 5);
    }

    [Fact]
    public void Templates_RoundTrip()
    {
        var doc = JklParser.ParseDocument(Jkl);
        Assert.Contains("+walkplayer", doc.Level.Templates.Keys);

        doc.Level.Templates["+walkplayer"].Values["model3d"] = "modified.3do";

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Contains("+walkplayer", reloaded.Templates.Keys);
        Assert.Equal("modified.3do", reloaded.Templates["+walkplayer"].Values["model3d"]);
    }

    [Fact]
    public void Layers_RoundTrip()
    {
        var doc = JklParser.ParseDocument(Jkl);
        Assert.Equal(2, doc.Level.Layers.Count);
        Assert.Equal(0, doc.Level.Sectors[0].Layer);

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(2, reloaded.Layers.Count);
        Assert.Equal(0, reloaded.Sectors[0].Layer);
    }
}
