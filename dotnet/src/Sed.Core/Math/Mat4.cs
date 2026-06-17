namespace Sed.Core.Math;

/// <summary>
/// Single-precision 4x4 matrix in column-major storage (GLSL/Vulkan layout):
/// element (row r, col c) lives at M[c*4 + r]. Projection uses Vulkan clip-space
/// conventions (Y points down, depth in [0,1]).
/// </summary>
public struct Mat4
{
    public float[] M; // length 16, column-major

    private Mat4(float[] m) => M = m;

    public static Mat4 Identity => new(new float[]
    {
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    });

    /// <summary>Returns a*b (apply b first, then a — same as GLSL a*b).</summary>
    public static Mat4 operator *(Mat4 a, Mat4 b)
    {
        var r = new float[16];
        for (int col = 0; col < 4; col++)
        for (int row = 0; row < 4; row++)
        {
            float sum = 0;
            for (int k = 0; k < 4; k++)
                sum += a.M[k * 4 + row] * b.M[col * 4 + k];
            r[col * 4 + row] = sum;
        }
        return new Mat4(r);
    }

    /// <summary>Right-handed look-at view matrix.</summary>
    public static Mat4 LookAt(Vec3 eye, Vec3 target, Vec3 up)
    {
        var f = (target - eye).Normalized();      // forward
        var s = f.Cross(up).Normalized();          // right
        var u = s.Cross(f);                         // true up

        return new Mat4(new float[]
        {
            (float)s.X, (float)u.X, (float)-f.X, 0,
            (float)s.Y, (float)u.Y, (float)-f.Y, 0,
            (float)s.Z, (float)u.Z, (float)-f.Z, 0,
            (float)-s.Dot(eye), (float)-u.Dot(eye), (float)f.Dot(eye), 1,
        });
    }

    /// <summary>
    /// Perspective projection for Vulkan: right-handed input, depth range [0,1],
    /// with the Y axis flipped to match Vulkan's clip space.
    /// </summary>
    public static Mat4 Perspective(double fovYRadians, double aspect, double near, double far)
    {
        double f = 1.0 / System.Math.Tan(fovYRadians / 2.0);
        var m = new float[16];
        m[0] = (float)(f / aspect);
        m[5] = (float)-f;                          // Y flip for Vulkan
        m[10] = (float)(far / (near - far));
        m[11] = -1f;
        m[14] = (float)((far * near) / (near - far));
        return new Mat4(m);
    }

    public static Mat4 Translate(Vec3 t)
    {
        var m = Identity;
        m.M[12] = (float)t.X;
        m.M[13] = (float)t.Y;
        m.M[14] = (float)t.Z;
        return m;
    }
}
