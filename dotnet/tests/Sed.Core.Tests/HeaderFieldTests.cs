using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class HeaderFieldTests
{
    private static LevelHeader MakeHeader() => new()
    {
        Gravity = 4.0f,
        PerspectiveDistance = 2.0f,
        GouraudDistance = 2.0f,
        CeilingSky = new CeilingSky { Height = 15f, Offset = new Vec2(1, 2) },
        HorizonSky = new HorizonSky { Distance = 100f, PixelsPerRev = 768f, Offset = new Vec2(3, 4) },
        MipmapDistances = new[] { 1f, 2f, 3f, 4f },
        LodDistances = new[] { 0.3f, 0.6f, 0.9f, 1.2f },
        Fog = new Fog { Enabled = false, Color = ColorF.Black, Start = 0, End = 100 },
    };

    [Fact]
    public void ScalarFieldRoundTripsThroughUndo()
    {
        var h = MakeHeader();
        var history = new EditHistory();

        history.Do(HeaderField.Set(h, "gravity", 9.5f, x => x.Gravity, (x, v) => x.Gravity = v));
        Assert.Equal(9.5f, h.Gravity);

        history.Undo();
        Assert.Equal(4.0f, h.Gravity);

        history.Redo();
        Assert.Equal(9.5f, h.Gravity);
    }

    [Fact]
    public void NestedStructFieldRoundTrips()
    {
        var h = MakeHeader();
        var history = new EditHistory();

        history.Do(HeaderField.Set(h, "ceiling sky height", 42f,
            x => x.CeilingSky.Height, (x, v) => x.CeilingSky.Height = v));
        Assert.Equal(42f, h.CeilingSky.Height);

        history.Undo();
        Assert.Equal(15f, h.CeilingSky.Height);
    }

    [Fact]
    public void Vec2OffsetIsReplacedWholeAndRestores()
    {
        var h = MakeHeader();
        var history = new EditHistory();

        // Vec2 is immutable, so a component edit replaces the whole value.
        history.Do(HeaderField.Set(h, "horizon offset X", new Vec2(9, h.HorizonSky.Offset.Y),
            x => x.HorizonSky.Offset, (x, v) => x.HorizonSky.Offset = v));

        Assert.Equal(9.0, h.HorizonSky.Offset.X, 6);
        Assert.Equal(4.0, h.HorizonSky.Offset.Y, 6);   // untouched component preserved

        history.Undo();
        Assert.Equal(3.0, h.HorizonSky.Offset.X, 6);
        Assert.Equal(4.0, h.HorizonSky.Offset.Y, 6);
    }

    [Fact]
    public void ArrayElementEditsAreIndependent()
    {
        var h = MakeHeader();
        var history = new EditHistory();

        history.Do(HeaderField.Set(h, "mipmap 2", 99f,
            x => x.MipmapDistances[2], (x, v) => x.MipmapDistances[2] = v));

        Assert.Equal(99f, h.MipmapDistances[2]);
        Assert.Equal(1f, h.MipmapDistances[0]);
        Assert.Equal(4f, h.MipmapDistances[3]);

        history.Undo();
        Assert.Equal(3f, h.MipmapDistances[2]);
    }

    [Fact]
    public void BoolAndColourFieldsRoundTrip()
    {
        var h = MakeHeader();
        var history = new EditHistory();

        history.Do(HeaderField.Set(h, "fog enabled", true, x => x.Fog.Enabled, (x, v) => x.Fog.Enabled = v));
        history.Do(HeaderField.Set(h, "fog colour", new ColorF(0.2f, 0.4f, 0.6f),
            x => x.Fog.Color, (x, v) => x.Fog.Color = v));

        Assert.True(h.Fog.Enabled);
        Assert.Equal(0.4f, h.Fog.Color.G, 4);

        history.Undo();
        Assert.Equal(ColorF.Black, h.Fog.Color);
        Assert.True(h.Fog.Enabled);      // only the colour edit was undone

        history.Undo();
        Assert.False(h.Fog.Enabled);
    }

    [Fact]
    public void EachFieldEditIsItsOwnUndoStep()
    {
        var h = MakeHeader();
        var history = new EditHistory();

        history.Do(HeaderField.Set(h, "gravity", 1f, x => x.Gravity, (x, v) => x.Gravity = v));
        history.Do(HeaderField.Set(h, "gouraud distance", 5f,
            x => x.GouraudDistance, (x, v) => x.GouraudDistance = v));

        history.Undo();
        Assert.Equal(2.0f, h.GouraudDistance);
        Assert.Equal(1f, h.Gravity);        // the earlier edit stands

        history.Undo();
        Assert.Equal(4.0f, h.Gravity);
    }

    [Fact]
    public void CommandNameDescribesTheField()
    {
        var h = MakeHeader();
        var command = HeaderField.Set(h, "gravity", 1f, x => x.Gravity, (x, v) => x.Gravity = v);

        Assert.Equal("Set gravity", command.Name);
    }
}
