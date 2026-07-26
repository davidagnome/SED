using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// Which layers are currently shown. This is editor state rather than level data
/// — hiding a layer changes nothing that gets saved — but both views and the
/// pickers need it, so a single instance is shared the way
/// <see cref="EditHistory"/> and <see cref="SelectionSet"/> are.
///
/// Layers are stored as "everything is visible unless hidden", so a level that
/// gains layers later does not have them default to invisible.
/// </summary>
public sealed class LayerVisibility
{
    private readonly HashSet<int> _hidden = new();

    /// <summary>Raised whenever visibility changes; views rebuild on this.</summary>
    public event Action? Changed;

    public bool AnyHidden => _hidden.Count > 0;
    public IReadOnlyCollection<int> Hidden => _hidden;

    public bool IsVisible(int layer) => !_hidden.Contains(layer);

    // Convenience overloads so render and pick loops read cleanly.
    public bool IsVisible(Sector sector) => IsVisible(sector.Layer);
    public bool IsVisible(Thing thing) => IsVisible(thing.Layer);
    public bool IsVisible(Light light) => IsVisible(light.Layer);

    public void SetVisible(int layer, bool visible)
    {
        bool changed = visible ? _hidden.Remove(layer) : _hidden.Add(layer);
        if (changed) Changed?.Invoke();
    }

    public void Toggle(int layer) => SetVisible(layer, !IsVisible(layer));

    /// <summary>Shows every layer. Used when loading a level and by "Show all".</summary>
    public void ShowAll()
    {
        if (_hidden.Count == 0) return;
        _hidden.Clear();
        Changed?.Invoke();
    }

    /// <summary>Hides every layer except one — "solo" a layer while working on it.</summary>
    public void Isolate(int layer, int layerCount)
    {
        var wanted = new HashSet<int>();
        for (int i = 0; i < layerCount; i++)
            if (i != layer) wanted.Add(i);

        if (wanted.SetEquals(_hidden)) return;
        _hidden.Clear();
        foreach (var i in wanted) _hidden.Add(i);
        Changed?.Invoke();
    }
}
