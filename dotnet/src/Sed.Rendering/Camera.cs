using Sed.Core.Math;

namespace Sed.Rendering;

/// <summary>A free-look 3D camera for the level preview (replaces PREVIEW.PAS view state).</summary>
public sealed class Camera
{
    public Vec3 Position { get; set; } = Vec3.Zero;
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double FieldOfViewDegrees { get; set; } = 90.0;
    public double NearPlane { get; set; } = 0.01;
    public double FarPlane { get; set; } = 1000.0;

    public Vec3 Forward
    {
        get
        {
            double cy = System.Math.Cos(Yaw), sy = System.Math.Sin(Yaw);
            double cp = System.Math.Cos(Pitch), sp = System.Math.Sin(Pitch);
            return new Vec3(sy * cp, cp * cy, sp).Normalized();
        }
    }
}
