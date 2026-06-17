namespace Sed.Core.Model;

/// <summary>
/// Root aggregate for an editable level (TJKLevel). Owns sectors, things,
/// lights, COGs, and templates, and assigns unique IDs used by the undo system.
/// </summary>
public sealed class Level
{
    private int _curId;

    public LevelHeader Header { get; } = new();
    public ProjectType Kind { get; set; } = ProjectType.JediKnight;

    public List<Sector> Sectors { get; } = new();
    public List<Thing> Things { get; } = new();
    public List<Light> Lights { get; } = new();
    public List<Cog> Cogs { get; } = new();
    public List<string> Layers { get; } = new();

    /// <summary>CMP colormap file names from GEORESOURCE; index 0 is the master palette.</summary>
    public List<string> ColorMaps { get; } = new();

    public string MasterCmp { get; set; } = string.Empty;
    public double PixelsPerUnit { get; set; }

    private int NewId() => ++_curId;

    public Sector NewSector()
    {
        var s = new Sector(this) { Id = NewId(), Num = Sectors.Count };
        Sectors.Add(s);
        return s;
    }

    public Thing NewThing()
    {
        var t = new Thing { Id = NewId(), Num = Things.Count, Level = this };
        Things.Add(t);
        return t;
    }

    public Light NewLight()
    {
        var l = new Light { Id = NewId() };
        Lights.Add(l);
        return l;
    }

    public void RenumberSectors()
    {
        for (int i = 0; i < Sectors.Count; i++)
            Sectors[i].Num = i;
    }

    public void RenumberThings()
    {
        for (int i = 0; i < Things.Count; i++)
            Things[i].Num = i;
    }

    /// <summary>Resolves a global surface by sector + local surface index (GetSectorSurfaceN).</summary>
    public Surface? GetSectorSurface(int sectorIndex, int surfaceIndex)
    {
        if ((uint)sectorIndex >= (uint)Sectors.Count) return null;
        var sec = Sectors[sectorIndex];
        if ((uint)surfaceIndex >= (uint)sec.Surfaces.Count) return null;
        return sec.Surfaces[surfaceIndex];
    }

    public int AddLayer(string name)
    {
        Layers.Add(name);
        return Layers.Count - 1;
    }

    public void Clear()
    {
        Sectors.Clear();
        Things.Clear();
        Lights.Clear();
        Cogs.Clear();
        Layers.Clear();
        _curId = 0;
    }
}
