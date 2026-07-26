using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// The editor's multi-selection — ordered, duplicate-free sets of vertices,
/// things, surfaces and sectors. A single instance is shared by the shell and
/// both views (the same way <see cref="EditHistory"/> is), so a pick in the 3D
/// viewport and a pick in the 2D map are the same selection.
///
/// Insertion order is preserved and the most recently added item of each kind is
/// the <c>Primary</c> — that is what the property inspector edits. The model
/// types are plain classes, so membership is reference identity.
/// </summary>
public sealed class SelectionSet
{
    private readonly Bucket<Vertex> _vertices = new();
    private readonly Bucket<Thing> _things = new();
    private readonly Bucket<Surface> _surfaces = new();
    private readonly Bucket<Sector> _sectors = new();
    private readonly Bucket<Light> _lights = new();

    private int _deferDepth;
    private bool _dirty;

    /// <summary>Raised after any change (once per <see cref="Defer"/> scope).</summary>
    public event Action? Changed;

    public IReadOnlyList<Vertex> Vertices => _vertices.Items;
    public IReadOnlyList<Thing> Things => _things.Items;
    public IReadOnlyList<Surface> Surfaces => _surfaces.Items;
    public IReadOnlyList<Sector> Sectors => _sectors.Items;
    public IReadOnlyList<Light> Lights => _lights.Items;

    /// <summary>Most recently added item of each kind — the inspector's target.</summary>
    public Vertex? PrimaryVertex => _vertices.Primary;
    public Thing? PrimaryThing => _things.Primary;
    public Surface? PrimarySurface => _surfaces.Primary;
    public Sector? PrimarySector => _sectors.Primary;
    public Light? PrimaryLight => _lights.Primary;

    public int Count => _vertices.Count + _things.Count + _surfaces.Count + _sectors.Count + _lights.Count;
    public bool IsEmpty => Count == 0;

    /// <summary>True when more than one item is selected, across all kinds.</summary>
    public bool IsMultiple => Count > 1;

    // ---- mutation ----

    public bool Add(Vertex v) => Mutate(_vertices.Add(v));
    public bool Add(Thing t) => Mutate(_things.Add(t));
    public bool Add(Surface s) => Mutate(_surfaces.Add(s));
    public bool Add(Sector s) => Mutate(_sectors.Add(s));
    public bool Add(Light l) => Mutate(_lights.Add(l));

    public bool Remove(Vertex v) => Mutate(_vertices.Remove(v));
    public bool Remove(Thing t) => Mutate(_things.Remove(t));
    public bool Remove(Surface s) => Mutate(_surfaces.Remove(s));
    public bool Remove(Sector s) => Mutate(_sectors.Remove(s));
    public bool Remove(Light l) => Mutate(_lights.Remove(l));

    public bool Contains(Vertex v) => _vertices.Contains(v);
    public bool Contains(Thing t) => _things.Contains(t);
    public bool Contains(Surface s) => _surfaces.Contains(s);
    public bool Contains(Sector s) => _sectors.Contains(s);
    public bool Contains(Light l) => _lights.Contains(l);

    /// <summary>Adds the item if absent, removes it if present (Ctrl+click).</summary>
    public void Toggle(Vertex v) { if (!Remove(v)) Add(v); }
    public void Toggle(Thing t) { if (!Remove(t)) Add(t); }
    public void Toggle(Surface s) { if (!Remove(s)) Add(s); }
    public void Toggle(Sector s) { if (!Remove(s)) Add(s); }
    public void Toggle(Light l) { if (!Remove(l)) Add(l); }

    /// <summary>Empties every bucket.</summary>
    public void Clear()
    {
        bool any = Count > 0;
        _vertices.Clear();
        _things.Clear();
        _surfaces.Clear();
        _sectors.Clear();
        _lights.Clear();
        Mutate(any);
    }

    /// <summary>Replaces the whole selection with one item (a plain click).</summary>
    public void SelectOnly(Vertex? v) { using var _ = Defer(); Clear(); if (v is not null) Add(v); }
    public void SelectOnly(Thing? t) { using var _ = Defer(); Clear(); if (t is not null) Add(t); }
    public void SelectOnly(Surface? s) { using var _ = Defer(); Clear(); if (s is not null) Add(s); }
    public void SelectOnly(Sector? s) { using var _ = Defer(); Clear(); if (s is not null) Add(s); }
    public void SelectOnly(Light? l) { using var _ = Defer(); Clear(); if (l is not null) Add(l); }

    /// <summary>
    /// Every vertex the selection implies: the directly selected ones, plus the
    /// corners of selected surfaces and the vertices of selected sectors.
    /// This is what transform commands operate on.
    /// </summary>
    public List<Vertex> AffectedVertices()
    {
        var seen = new HashSet<Vertex>();
        var result = new List<Vertex>();

        void Take(Vertex v) { if (seen.Add(v)) result.Add(v); }

        foreach (var v in _vertices.Items) Take(v);
        foreach (var s in _surfaces.Items)
            foreach (var c in s.Corners) Take(c.Vertex);
        foreach (var sec in _sectors.Items)
            foreach (var v in sec.Vertices) Take(v);

        return result;
    }

    /// <summary>
    /// Drops anything no longer reachable from <paramref name="level"/> — call
    /// after a delete or an undo that may have removed selected objects.
    /// </summary>
    public void Prune(Level level)
    {
        using var _ = Defer();
        var liveSectors = new HashSet<Sector>(level.Sectors);
        var liveThings = new HashSet<Thing>(level.Things);
        var liveLights = new HashSet<Light>(level.Lights);

        foreach (var l in _lights.Items.ToArray())
            if (!liveLights.Contains(l)) Remove(l);
        foreach (var t in _things.Items.ToArray())
            if (!liveThings.Contains(t)) Remove(t);
        foreach (var s in _sectors.Items.ToArray())
            if (!liveSectors.Contains(s)) Remove(s);
        foreach (var s in _surfaces.Items.ToArray())
            if (!liveSectors.Contains(s.Sector) || !s.Sector.Surfaces.Contains(s)) Remove(s);
        foreach (var v in _vertices.Items.ToArray())
            if (v.Sector is null || !liveSectors.Contains(v.Sector) || !v.Sector.Vertices.Contains(v)) Remove(v);
    }

    /// <summary>
    /// Suppresses <see cref="Changed"/> until the returned scope is disposed, so a
    /// bulk update (box-select) raises one event instead of thousands.
    /// </summary>
    public IDisposable Defer()
    {
        _deferDepth++;
        return new DeferScope(this);
    }

    private bool Mutate(bool changed)
    {
        if (!changed) return false;
        if (_deferDepth > 0) _dirty = true;
        else Changed?.Invoke();
        return true;
    }

    private void EndDefer()
    {
        if (--_deferDepth > 0) return;
        if (!_dirty) return;
        _dirty = false;
        Changed?.Invoke();
    }

    private sealed class DeferScope(SelectionSet owner) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner.EndDefer();
        }
    }

    /// <summary>An insertion-ordered set with reference-identity membership.</summary>
    private sealed class Bucket<T> where T : class
    {
        private readonly List<T> _order = new();
        private readonly HashSet<T> _set = new();

        public IReadOnlyList<T> Items => _order;
        public int Count => _order.Count;
        public T? Primary => _order.Count > 0 ? _order[^1] : null;

        public bool Contains(T item) => _set.Contains(item);

        public bool Add(T item)
        {
            if (!_set.Add(item)) return false;
            _order.Add(item);
            return true;
        }

        public bool Remove(T item)
        {
            if (!_set.Remove(item)) return false;
            _order.Remove(item);
            return true;
        }

        public void Clear()
        {
            _order.Clear();
            _set.Clear();
        }
    }
}
