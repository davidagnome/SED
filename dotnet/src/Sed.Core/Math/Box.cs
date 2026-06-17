namespace Sed.Core.Math;

/// <summary>Axis-aligned bounding box (TBox / TThingBox / collideBox in the original).</summary>
public struct Box
{
    public Vec3 Min;
    public Vec3 Max;

    public Box(Vec3 min, Vec3 max)
    {
        Min = min;
        Max = max;
    }

    public static Box Empty => new(
        new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public Vec3 Center => (Min + Max) * 0.5;
    public Vec3 Size => Max - Min;

    public void Encapsulate(Vec3 p)
    {
        Min = new Vec3(System.Math.Min(Min.X, p.X), System.Math.Min(Min.Y, p.Y), System.Math.Min(Min.Z, p.Z));
        Max = new Vec3(System.Math.Max(Max.X, p.X), System.Math.Max(Max.Y, p.Y), System.Math.Max(Max.Z, p.Z));
    }
}
