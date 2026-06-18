using Sed.Core.Model;

namespace Sed.Formats.Jkl;

/// <summary>
/// A parsed JKL together with its original source lines and the line numbers of
/// the editable entries (world vertices, things). This lets <see cref="JklWriter"/>
/// rewrite only the lines that changed and preserve everything else verbatim,
/// keeping COGs/lights/layers/etc. intact for a game-loadable save.
/// </summary>
public sealed class JklDocument
{
    public string[] SourceLines { get; }
    public Level Level { get; internal set; } = new();

    /// <summary>Global world-vertex index → 0-based source line.</summary>
    public Dictionary<int, int> VertexLine { get; } = new();

    /// <summary>Global world-vertex index → originally-parsed position (to detect edits).</summary>
    public Dictionary<int, Sed.Core.Math.Vec3> VertexOriginal { get; } = new();

    /// <summary>Thing number (model order) → 0-based source line.</summary>
    public Dictionary<int, int> ThingLine { get; } = new();

    /// <summary>0-based source line of the "World things N" count line (-1 if no THINGS section).</summary>
    public int ThingsCountLine { get; set; } = -1;

    /// <summary>Original material names (index order) for resolving surface material indices on save.</summary>
    public List<string> Materials { get; } = new();

    public JklDocument(string[] sourceLines) => SourceLines = sourceLines;
}
