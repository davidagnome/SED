using Sed.Core.Math;

namespace Sed.Core.Model;

public struct Fog
{
    public bool Enabled;
    public ColorF Color;
    public double Start;
    public double End;
}

public struct CeilingSky
{
    public float Height;
    public Vec2 Offset;
}

public struct HorizonSky
{
    public float Distance;
    public float PixelsPerRev;
    public Vec2 Offset;
}

/// <summary>Level header / world parameters (TJKLHeader).</summary>
public sealed class LevelHeader
{
    public int Version;
    public float Gravity;
    public HorizonSky HorizonSky;
    public CeilingSky CeilingSky;
    public float[] MipmapDistances = new float[4];
    public float[] LodDistances = new float[4];
    public float PerspectiveDistance;
    public float GouraudDistance;
    public Fog Fog;
}
