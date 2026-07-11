namespace Sed.Core.Editing;

/// <summary>
/// The entity type currently being selected/edited. Mirrors the original SED's
/// MM_* map modes (JED_MAIN.PAS:13-22). Drives what the picker hit-tests, what
/// the inspector panel shows, and which keyboard shortcuts apply.
/// </summary>
public enum EditMode
{
    Sector,
    Surface,
    Vertex,
    Edge,
    Thing,
    Light,
}
