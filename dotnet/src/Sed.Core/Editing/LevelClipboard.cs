using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// A detached snapshot of part of a level — the editor's clipboard (mirrors
/// `U_COPYPASTE.PAS`). Capturing deep-copies the selection, so later edits to the
/// original do not disturb what was copied, and each paste clones the snapshot
/// again so repeated pastes produce independent geometry.
///
/// JK shares world vertices between sectors, so cloning always creates fresh
/// <see cref="Vertex"/> objects rather than aliasing the source. Adjoins are
/// remapped when both sides of the pair are inside the fragment and cleared
/// otherwise — a pasted room must not stay portal-linked to the room it came from.
/// </summary>
public sealed class LevelFragment
{
    private readonly List<Sector> _sectors = new();
    private readonly List<Thing> _things = new();
    private readonly List<Light> _lights = new();

    public IReadOnlyList<Sector> Sectors => _sectors;
    public IReadOnlyList<Thing> Things => _things;
    public IReadOnlyList<Light> Lights => _lights;

    public bool IsEmpty => _sectors.Count == 0 && _things.Count == 0 && _lights.Count == 0;

    /// <summary>Total item count, for status messages.</summary>
    public int Count => _sectors.Count + _things.Count + _lights.Count;

    /// <summary>World-space extent of the captured contents.</summary>
    public Box Bounds
    {
        get
        {
            var box = Box.Empty;
            foreach (var s in _sectors)
                foreach (var v in s.Vertices) box.Encapsulate(v.Position);
            foreach (var t in _things) box.Encapsulate(t.Position);
            foreach (var l in _lights) box.Encapsulate(l.Position);
            return box;
        }
    }

    /// <summary>
    /// Snapshots the current selection. Selected sectors are taken whole; selected
    /// surfaces contribute their owning sector, since a surface cannot exist
    /// outside one. Returns an empty fragment when nothing copyable is selected.
    /// </summary>
    public static LevelFragment Capture(SelectionSet selection, Level level)
    {
        var fragment = new LevelFragment();

        // Sectors: explicitly selected, plus those owning any selected surface.
        var wanted = new List<Sector>();
        var seen = new HashSet<Sector>();
        foreach (var s in selection.Sectors)
            if (seen.Add(s)) wanted.Add(s);
        foreach (var surf in selection.Surfaces)
            if (seen.Add(surf.Sector)) wanted.Add(surf.Sector);

        var surfaceMap = new Dictionary<Surface, Surface>();
        var sectorMap = new Dictionary<Sector, Sector>();

        foreach (var src in wanted)
        {
            var clone = CloneSector(src, level, Vec3.Zero, surfaceMap);
            sectorMap[src] = clone;
            fragment._sectors.Add(clone);
        }
        RemapAdjoins(surfaceMap);

        foreach (var t in selection.Things)
            fragment._things.Add(CloneThing(t, level, Vec3.Zero, sectorMap));

        foreach (var l in selection.Lights)
            fragment._lights.Add(CloneLight(l, Vec3.Zero));

        return fragment;
    }

    /// <summary>
    /// Clones this fragment's contents into <paramref name="target"/>, displaced by
    /// <paramref name="offset"/>. The objects are created but <em>not</em> added to
    /// the level — <see cref="PasteFragmentCommand"/> does that so paste is undoable.
    /// </summary>
    internal (List<Sector> sectors, List<Thing> things, List<Light> lights) Instantiate(Level target, Vec3 offset)
    {
        var surfaceMap = new Dictionary<Surface, Surface>();
        var sectorMap = new Dictionary<Sector, Sector>();

        var sectors = new List<Sector>(_sectors.Count);
        foreach (var src in _sectors)
        {
            var clone = CloneSector(src, target, offset, surfaceMap);
            sectorMap[src] = clone;
            sectors.Add(clone);
        }
        RemapAdjoins(surfaceMap);

        var things = new List<Thing>(_things.Count);
        foreach (var t in _things)
            things.Add(CloneThing(t, target, offset, sectorMap));

        var lights = new List<Light>(_lights.Count);
        foreach (var l in _lights)
            lights.Add(CloneLight(l, offset));

        return (sectors, things, lights);
    }

    private static Light CloneLight(Light src, Vec3 offset) => new()
    {
        Position = src.Position + offset,
        Range = src.Range,
        Intensity = src.Intensity,
        Color = src.Color,
        Flags = src.Flags,
        Layer = src.Layer,
    };

    private static Sector CloneSector(Sector src, Level target, Vec3 offset,
        Dictionary<Surface, Surface> surfaceMap)
    {
        var dst = new Sector(target)
        {
            Flags = src.Flags,
            Ambient = src.Ambient,
            ExtraLight = src.ExtraLight,
            Tint = src.Tint,
            ColorMap = src.ColorMap,
            Sound = src.Sound,
            SoundVolume = src.SoundVolume,
            Thrust = src.Thrust,
            Layer = src.Layer,
        };

        var vertexMap = new Dictionary<Vertex, Vertex>();
        foreach (var v in src.Vertices)
            vertexMap[v] = dst.AddVertex(v.Position + offset);

        foreach (var s in src.Surfaces)
        {
            var ns = dst.NewSurface();
            ns.Material = s.Material;
            ns.MaterialIndex = s.MaterialIndex;
            ns.SurfFlags = s.SurfFlags;
            ns.FaceFlags = s.FaceFlags;
            ns.Geo = s.Geo;
            ns.Light = s.Light;
            ns.Tex = s.Tex;
            ns.ExtraLightIntensity = s.ExtraLightIntensity;
            ns.UScale = s.UScale;
            ns.VScale = s.VScale;
            ns.AdjoinFlags = s.AdjoinFlags;

            foreach (var c in s.Corners)
            {
                // A surface should only reference its own sector's vertices, but
                // clone defensively so a malformed level cannot alias the source.
                if (!vertexMap.TryGetValue(c.Vertex, out var nv))
                    nv = vertexMap[c.Vertex] = dst.AddVertex(c.Vertex.Position + offset);

                ns.Corners.Add(new Surface.Corner { Vertex = nv, Uv = c.Uv, Intensity = c.Intensity });
            }

            ns.RecalcNormal();
            surfaceMap[s] = ns;
        }

        dst.Renumber();
        return dst;
    }

    /// <summary>
    /// Rebuilds adjoin links among cloned surfaces. Pairs wholly inside the clone
    /// set are re-pointed at their clones; anything pointing outside is cleared,
    /// so a pasted room is sealed rather than opening into the original.
    /// </summary>
    private static void RemapAdjoins(Dictionary<Surface, Surface> surfaceMap)
    {
        foreach (var (src, dst) in surfaceMap)
        {
            if (src.Adjoin is { } partner && surfaceMap.TryGetValue(partner, out var clonedPartner))
            {
                dst.Adjoin = clonedPartner;
            }
            else
            {
                dst.Adjoin = null;
                dst.AdjoinFlags = 0;
            }
        }
    }

    private static Thing CloneThing(Thing src, Level target, Vec3 offset,
        Dictionary<Sector, Sector> sectorMap)
    {
        var dst = new Thing
        {
            Template = src.Template,
            Name = src.Name,
            Position = src.Position + offset,
            Pitch = src.Pitch,
            Yaw = src.Yaw,
            Roll = src.Roll,
            Layer = src.Layer,
            Flags = src.Flags,
            Level = target,
        };

        foreach (var (k, v) in src.Values) dst.Values[k] = v;

        // Keep the thing inside its copied room when that room came along;
        // otherwise leave it in the original sector, which is still valid.
        dst.Sector = src.Sector is { } sec && sectorMap.TryGetValue(sec, out var mapped) ? mapped : src.Sector;
        return dst;
    }
}

/// <summary>
/// Pastes a <see cref="LevelFragment"/> into a level, displaced by an offset.
/// Reversible; redo re-adds the same objects rather than making new ones, so undo
/// history stays stable.
/// </summary>
public sealed class PasteFragmentCommand : IEditCommand
{
    private readonly Level _level;
    private readonly LevelFragment _fragment;
    private readonly Vec3 _offset;

    private List<Sector>? _sectors;
    private List<Thing>? _things;
    private List<Light>? _lights;

    public PasteFragmentCommand(Level level, LevelFragment fragment, Vec3 offset)
    {
        _level = level;
        _fragment = fragment;
        _offset = offset;
    }

    public string Name => $"Paste {_fragment.Count} item(s)";

    /// <summary>Objects this paste created — the editor selects them afterwards.</summary>
    public IReadOnlyList<Sector> PastedSectors => _sectors ?? (IReadOnlyList<Sector>)Array.Empty<Sector>();
    public IReadOnlyList<Thing> PastedThings => _things ?? (IReadOnlyList<Thing>)Array.Empty<Thing>();
    public IReadOnlyList<Light> PastedLights => _lights ?? (IReadOnlyList<Light>)Array.Empty<Light>();

    public void Apply()
    {
        if (_sectors is null || _things is null || _lights is null)
            (_sectors, _things, _lights) = _fragment.Instantiate(_level, _offset);

        foreach (var s in _sectors) _level.Sectors.Add(s);
        foreach (var t in _things) _level.Things.Add(t);
        foreach (var l in _lights) _level.Lights.Add(l);

        _level.RenumberSectors();
        _level.RenumberThings();
        _level.RenumberLights();
    }

    public void Revert()
    {
        if (_sectors is null || _things is null || _lights is null) return;

        foreach (var s in _sectors) _level.Sectors.Remove(s);
        foreach (var t in _things) _level.Things.Remove(t);
        foreach (var l in _lights) _level.Lights.Remove(l);

        _level.RenumberSectors();
        _level.RenumberThings();
        _level.RenumberLights();
    }
}
