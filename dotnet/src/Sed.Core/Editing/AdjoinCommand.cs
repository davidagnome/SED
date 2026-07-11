using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// Creates a mirror adjoin pair between two surfaces: <c>a.Adjoin = b</c> and
/// <c>b.Adjoin = a</c>, setting default adjoin flags (Visible | Move).
/// Fully reversible.
/// </summary>
public sealed class MakeAdjoinCommand : IEditCommand
{
    private readonly Surface _a, _b;
    private Surface? _oldAdjoinA, _oldAdjoinB;
    private long _oldFlagsA, _oldFlagsB;

    public MakeAdjoinCommand(Surface a, Surface b) { _a = a; _b = b; }
    public string Name => "Make adjoin";

    public void Apply()
    {
        _oldAdjoinA = _a.Adjoin; _oldAdjoinB = _b.Adjoin;
        _oldFlagsA = _a.AdjoinFlags; _oldFlagsB = _b.AdjoinFlags;
        _a.Adjoin = _b; _b.Adjoin = _a;
        _a.AdjoinFlags = 0x01 | 0x02; // Visible | Move
        _b.AdjoinFlags = 0x01 | 0x02;
    }

    public void Revert()
    {
        _a.Adjoin = _oldAdjoinA; _b.Adjoin = _oldAdjoinB;
        _a.AdjoinFlags = _oldFlagsA; _b.AdjoinFlags = _oldFlagsB;
    }
}

/// <summary>
/// Removes an adjoin from <paramref name="a"/>, clearing the mirror link on the
/// opposite surface as well. Fully reversible.
/// </summary>
public sealed class RemoveAdjoinCommand : IEditCommand
{
    private readonly Surface _a;
    private Surface? _oldAdjoin, _oldMirrorAdjoin;
    private long _oldFlags, _oldMirrorFlags;

    public RemoveAdjoinCommand(Surface a) { _a = a; }
    public string Name => "Remove adjoin";

    public void Apply()
    {
        _oldAdjoin = _a.Adjoin;
        _oldFlags = _a.AdjoinFlags;
        if (_a.Adjoin is { } mirror)
        {
            _oldMirrorAdjoin = mirror.Adjoin;
            _oldMirrorFlags = mirror.AdjoinFlags;
            mirror.Adjoin = null;
        }
        _a.Adjoin = null;
    }

    public void Revert()
    {
        _a.Adjoin = _oldAdjoin;
        _a.AdjoinFlags = _oldFlags;
        if (_oldAdjoin is { } mirror)
        {
            mirror.Adjoin = _oldMirrorAdjoin;
            mirror.AdjoinFlags = _oldMirrorFlags;
        }
    }
}
