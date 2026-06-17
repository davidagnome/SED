using Sed.Core.Math;
using Sed.Rendering;
using Xunit;

namespace Sed.Core.Tests;

public class PickerTests
{
    [Fact]
    public void RayTriangle_HitsFromAbove()
    {
        var ray = new Ray(new Vec3(0.25, 0.25, 1), new Vec3(0, 0, -1));
        bool hit = Picker.RayTriangle(ray, new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), out double t);
        Assert.True(hit);
        Assert.Equal(1.0, t, 6);
    }

    [Fact]
    public void RayTriangle_MissesOutsideTriangle()
    {
        var ray = new Ray(new Vec3(5, 5, 1), new Vec3(0, 0, -1));
        Assert.False(Picker.RayTriangle(ray, new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), out _));
    }

    [Fact]
    public void RayTriangle_MissesWhenPointingAway()
    {
        var ray = new Ray(new Vec3(0.25, 0.25, 1), new Vec3(0, 0, 1)); // away from triangle
        Assert.False(Picker.RayTriangle(ray, new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), out _));
    }

    [Fact]
    public void ScreenCenterRay_PointsAlongForward()
    {
        var cam = Camera.LookingAt(new Vec3(0, 0, 5), Vec3.Zero);
        var ray = Picker.ScreenPointToRay(cam, 320, 240, 640, 480);
        Assert.Equal(cam.Forward.X, ray.Direction.X, 6);
        Assert.Equal(cam.Forward.Y, ray.Direction.Y, 6);
        Assert.Equal(cam.Forward.Z, ray.Direction.Z, 6);
    }

    [Fact]
    public void Pick_HitsNearestSurface_OnSampleCube()
    {
        var level = SampleScene.CreateCube(); // spans -1..1
        var cam = Camera.LookingAt(new Vec3(0, 0, 5), Vec3.Zero); // looking down -Z at the top face
        var ray = Picker.ScreenPointToRay(cam, 320, 240, 640, 480);

        var hit = Picker.Pick(level, ray);
        Assert.NotNull(hit);
        Assert.Equal(1.0, hit!.Point.Z, 4);   // top face at z = +1
        Assert.Equal(4.0, hit.Distance, 4);    // 5 - 1
    }
}
