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
            return new Vec3(sy * cp, cy * cp, sp).Normalized();
        }
    }

    /// <summary>World up. The Sith engine is Z-up (sector floors face +Z).</summary>
    public static Vec3 Up => new(0, 0, 1);

    /// <summary>Camera-right unit vector (screen +X).</summary>
    public Vec3 Right => Forward.Cross(Up).Normalized();

    /// <summary>Camera-up unit vector (screen +Y), tilts with pitch.</summary>
    public Vec3 CameraUp => Right.Cross(Forward).Normalized();

    /// <summary>Creates a camera positioned at <paramref name="eye"/> aimed at <paramref name="target"/>.</summary>
    public static Camera LookingAt(Vec3 eye, Vec3 target, double fovDegrees = 60.0)
    {
        var dir = (target - eye).Normalized();
        return new Camera
        {
            Position = eye,
            Pitch = System.Math.Asin(System.Math.Clamp(dir.Z, -1, 1)),
            Yaw = System.Math.Atan2(dir.X, dir.Y),
            FieldOfViewDegrees = fovDegrees,
        };
    }

    /// <summary>Builds the combined projection * view matrix for the given aspect ratio.</summary>
    public Mat4 ViewProjection(double aspect)
    {
        var view = Mat4.LookAt(Position, Position + Forward, Up);
        var proj = Mat4.Perspective(FieldOfViewDegrees * System.Math.PI / 180.0, aspect, NearPlane, FarPlane);
        return proj * view;
    }
}
