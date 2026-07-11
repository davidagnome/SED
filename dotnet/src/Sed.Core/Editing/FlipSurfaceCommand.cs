using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// Reverses a surface's vertex winding order (corner list), flipping its normal.
/// Mirrors the original Delphi <c>FlipSurface</c> in <c>LEV_UTILS.PAS:5334</c>.
/// </summary>
public sealed class FlipSurfaceCommand : IEditCommand
{
    private readonly Surface _surface;
    private List<Surface.Corner> _original = new();

    public FlipSurfaceCommand(Surface surface) { _surface = surface; }
    public string Name => "Flip surface";

    public void Apply()
    {
        _original = new List<Surface.Corner>(_surface.Corners);
        _surface.Corners.Reverse();
        _surface.RecalcNormal();
    }

    public void Revert()
    {
        _surface.Corners.Clear();
        _surface.Corners.AddRange(_original);
        _surface.RecalcNormal();
    }
}
