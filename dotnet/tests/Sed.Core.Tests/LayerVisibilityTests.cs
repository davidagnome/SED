using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class LayerVisibilityTests
{
    [Fact]
    public void EverythingIsVisibleByDefault()
    {
        var layers = new LayerVisibility();

        Assert.True(layers.IsVisible(0));
        Assert.True(layers.IsVisible(7));
        Assert.True(layers.IsVisible(999));   // a layer added later must not default to hidden
        Assert.False(layers.AnyHidden);
    }

    [Fact]
    public void HidingAndShowingRaisesChangedOnlyOnRealTransitions()
    {
        var layers = new LayerVisibility();
        int fired = 0;
        layers.Changed += () => fired++;

        layers.SetVisible(2, false);
        Assert.Equal(1, fired);
        Assert.False(layers.IsVisible(2));

        layers.SetVisible(2, false);      // already hidden
        Assert.Equal(1, fired);

        layers.SetVisible(2, true);
        Assert.Equal(2, fired);
        Assert.True(layers.IsVisible(2));

        layers.SetVisible(2, true);       // already visible
        Assert.Equal(2, fired);
    }

    [Fact]
    public void ToggleFlipsState()
    {
        var layers = new LayerVisibility();

        layers.Toggle(1);
        Assert.False(layers.IsVisible(1));

        layers.Toggle(1);
        Assert.True(layers.IsVisible(1));
    }

    [Fact]
    public void ShowAllClearsEverythingAndFiresOnce()
    {
        var layers = new LayerVisibility();
        layers.SetVisible(0, false);
        layers.SetVisible(1, false);
        layers.SetVisible(2, false);

        int fired = 0;
        layers.Changed += () => fired++;

        layers.ShowAll();
        Assert.Equal(1, fired);
        Assert.False(layers.AnyHidden);

        layers.ShowAll();                 // already all visible
        Assert.Equal(1, fired);
    }

    [Fact]
    public void IsolateHidesEveryOtherLayer()
    {
        var layers = new LayerVisibility();

        layers.Isolate(2, layerCount: 5);

        Assert.True(layers.IsVisible(2));
        foreach (int other in new[] { 0, 1, 3, 4 })
            Assert.False(layers.IsVisible(other));
    }

    [Fact]
    public void IsolateIsIdempotent()
    {
        var layers = new LayerVisibility();
        int fired = 0;

        layers.Isolate(1, 4);
        layers.Changed += () => fired++;
        layers.Isolate(1, 4);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void ModelOverloadsReadTheObjectsLayer()
    {
        var level = new Level();
        var sector = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "dflt.mat", 0);
        sector.Layer = 3;
        level.Sectors.Add(sector);

        var thing = new Thing { Layer = 3, Sector = sector };
        var light = new Light { Layer = 1 };

        var layers = new LayerVisibility();
        layers.SetVisible(3, false);

        Assert.False(layers.IsVisible(sector));
        Assert.False(layers.IsVisible(thing));
        Assert.True(layers.IsVisible(light));   // different layer, still shown
    }
}
