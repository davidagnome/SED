namespace Sed.Core.Math;

/// <summary>
/// Double-precision 3D vector. The Sith-engine level format and the original
/// SED editor (J_LEVEL.PAS / math.pas) work in <c>double</c> precision for
/// geometry, so the editor model mirrors that. Floats are only introduced at
/// the rendering boundary (see <see cref="Sed.Core.Math.Vec3.ToFloat"/>).
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public Vec3 Cross(Vec3 b) => new(
        Y * b.Z - Z * b.Y,
        Z * b.X - X * b.Z,
        X * b.Y - Y * b.X);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => System.Math.Sqrt(LengthSquared);

    public double DistanceSquaredTo(Vec3 b) => (this - b).LengthSquared;
    public double DistanceTo(Vec3 b) => (this - b).Length;

    /// <summary>Returns the normalized vector and outputs the original length (mirrors VectorNormalize3).</summary>
    public Vec3 Normalized(out double length)
    {
        length = Length;
        return length == 0 ? this : this / length;
    }

    public Vec3 Normalized() => Normalized(out _);

    /// <summary>Projects this vector onto <paramref name="b"/>.</summary>
    public Vec3 ProjectOnto(Vec3 b) => b * (Dot(b) / b.Dot(b));

    /// <summary>Projects this vector onto a unit normal <paramref name="n"/>.</summary>
    public Vec3 ProjectOntoNormal(Vec3 n) => n * Dot(n);

    public (float X, float Y, float Z) ToFloat() => ((float)X, (float)Y, (float)Z);

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
