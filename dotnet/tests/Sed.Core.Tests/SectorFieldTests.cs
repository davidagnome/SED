using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class SectorFieldTests
{
    [Fact]
    public void SetSectorFlags_Apply_Revert_RoundTrips()
    {
        var level = new Level();
        var sector = new Sector(level) { Flags = 0x10 };
        var cmd = new SetSectorFlagsCommand(sector, 0x40000000);

        cmd.Apply();
        Assert.Equal(0x40000000, sector.Flags);

        cmd.Revert();
        Assert.Equal(0x10, sector.Flags);
    }

    [Fact]
    public void SetSectorTint_Apply_Revert_RoundTrips()
    {
        var level = new Level();
        var sector = new Sector(level) { Tint = new ColorF(0.1f, 0.2f, 0.3f) };
        var cmd = new SetSectorTintCommand(sector, new ColorF(0.5f, 0.5f, 0.5f));

        cmd.Apply();
        Assert.Equal(new ColorF(0.5f, 0.5f, 0.5f), sector.Tint);

        cmd.Revert();
        Assert.Equal(new ColorF(0.1f, 0.2f, 0.3f), sector.Tint);
    }

    [Fact]
    public void SetSectorSound_Apply_Revert_RoundTrips()
    {
        var level = new Level();
        var sector = new Sector(level) { Sound = "old.wav", SoundVolume = 0.5 };
        var cmd = new SetSectorSoundCommand(sector, "new.wav", 1.0);

        cmd.Apply();
        Assert.Equal("new.wav", sector.Sound);
        Assert.Equal(1.0, sector.SoundVolume);

        cmd.Revert();
        Assert.Equal("old.wav", sector.Sound);
        Assert.Equal(0.5, sector.SoundVolume);
    }

    [Fact]
    public void SetSectorLayer_Apply_Revert_RoundTrips()
    {
        var level = new Level();
        var sector = new Sector(level) { Layer = 1 };
        var cmd = new SetSectorLayerCommand(sector, 5);

        cmd.Apply();
        Assert.Equal(5, sector.Layer);

        cmd.Revert();
        Assert.Equal(1, sector.Layer);
    }

    [Fact]
    public void SetSectorColormap_Apply_Revert_RoundTrips()
    {
        var level = new Level();
        var sector = new Sector(level) { ColorMap = "default.cmp" };
        var cmd = new SetSectorColormapCommand(sector, "under.cmp");

        cmd.Apply();
        Assert.Equal("under.cmp", sector.ColorMap);

        cmd.Revert();
        Assert.Equal("default.cmp", sector.ColorMap);
    }
}
