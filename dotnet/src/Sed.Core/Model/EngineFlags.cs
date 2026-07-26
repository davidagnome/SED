namespace Sed.Core.Model;

/// <summary>
/// Surface flags, transcribed from <c>SF_*</c> in `J_LEVEL.PAS`. The bit values
/// are engine ABI — they are written straight into the JKL surface line — so they
/// must match the original exactly.
/// </summary>
public static class SurfaceFlags
{
    public const long Floor          = 0x01;
    public const long CogLinked      = 0x02;
    public const long Collision      = 0x04;

    // The resolution flags drive the SlideWall COG function at runtime; they are
    // not a static texture scale (the original's GetSurfResScale is dead code and
    // its uscale/vscale mapping in LEVEL_IO.INC is commented out).
    public const long DoubleRes      = 0x10;
    public const long HalfRes        = 0x20;
    public const long EighthRes      = 0x40;
    public const long QuarterRes     = 0x4000000;
    public const long QuadrupleRes   = 0x8000000;

    public const long SkyHorizon     = 0x200;
    public const long SkyCeiling     = 0x400;
    public const long Water          = 0x20000;

    // Infernal Machine only.
    public const long IjimAetherium  = 0x80;
    public const long IjimKillFloor  = 0x1000;
    public const long IjimClimbable  = 0x2000;
    public const long IjimTrack      = 0x4000;
    public const long IjimLedge      = 0x1000000;
    public const long IjimWaterLedge = 0x2000000;
    public const long IjimWhipAim    = 0x10000000;
}

/// <summary>Face flags, transcribed from <c>FF_*</c> in `GEOMETRY.PAS`.</summary>
public static class FaceFlags
{
    public const long DoubleSided    = 0x01;
    public const long Translucent    = 0x02;
    public const long TexClampX      = 0x04;
    public const long TexClampY      = 0x08;
    public const long TexNoFiltering = 0x10;
    public const long ZWriteDisabled = 0x20;

    // Infernal Machine only.
    public const long IjimLedge3do   = 0x40;
    public const long IjimFogEnabled = 0x100;
    public const long IjimWhipAim3do = 0x200;
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
