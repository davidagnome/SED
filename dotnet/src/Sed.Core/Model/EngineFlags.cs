namespace Sed.Core.Model;

/// <summary>Surface flags (SF_* in J_LEVEL.PAS).</summary>
public static class SurfaceFlags
{
    public const long Floor          = 0x01;
    public const long Collision      = 0x04;
    public const long DoubleRes      = 0x08;
    public const long HalfRes        = 0x10;
    public const long SkyHorizon     = 0x200;
    public const long SkyCeiling     = 0x400;
    public const long Water          = 0x1000;
    public const long FloorWalkable  = 0x10000;
}

/// <summary>Face flags (FF_* in J_LEVEL.PAS).</summary>
public static class FaceFlags
{
    public const long Translucent    = 0x02;
    public const long TexFlip        = 0x04;   // FF_SF_FLIP
    public const long TexClampX      = 0x08;
    public const long TexClampY      = 0x10;
    public const long DoubleSided    = 0x01;
}

/// <summary>Sector flags (SECF_* in J_LEVEL.PAS).</summary>
public static class SectorFlags
{
    public const long Underwater        = 0x00000001;
    public const long NoAmbientLight    = 0x40000000;
    public const long NoRgbAmbientLight = 0x20000000;
    public const long IjimAetherim      = 0x08000000;
    public const long IjimUseThrust     = 0x04000000;
    public const long Cog3DO            = 0x01000000;
}

/// <summary>Adjoin flags (SAF_* in J_LEVEL.PAS).</summary>
public static class AdjoinFlags
{
    public const long Visible          = 0x01;
    public const long Move             = 0x02;
    public const long AllowSoundPass   = 0x04;
    public const long NoAiMove         = 0x08;
    public const long NoPlayerMove     = 0x10;
    public const long BlockLight       = 0x80000000;
}

/// <summary>Light flags (LF_* in J_LEVEL.PAS).</summary>
public static class LightFlags
{
    public const long NoBlock = 0x01;
}
