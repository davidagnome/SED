namespace Sed.Core.Math;

/// <summary>Double-precision 2D vector (also used for texture UVs and 2D offsets).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);

    public double Dot(Vec2 b) => X * b.X + Y * b.Y;
    public double Cross(Vec2 b) => X * b.Y - b.X * Y;
    public double LengthSquared => X * X + Y * Y;
    public double Length => System.Math.Sqrt(LengthSquared);

    public Vec2 Normalized(out double length)
    {
        length = Length;
        return length == 0 ? this : new Vec2(X / length, Y / length);
    }

    public Vec2 Normalized() => Normalized(out _);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
