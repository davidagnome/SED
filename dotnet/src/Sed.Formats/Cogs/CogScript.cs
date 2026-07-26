namespace Sed.Formats.Cogs;

/// <summary>
/// COG symbol types (`CogTypes` in `VALUES.PAS`). The type decides what a value
/// refers to — a thing index, a sector index, a sound filename, and so on.
/// </summary>
public enum CogSymbolType
{
    Unknown,
    Message,
    Int,
    Flex,
    Float,
    Vector,
    Thing,
    Sector,
    Surface,
    Sound,
    Material,
    Model,
    Template,
    Keyframe,
    Ai,
    Cog,
}

/// <summary>One entry of a script's <c>symbols</c> block.</summary>
public sealed record CogSymbol(
    CogSymbolType Type,
    string Name,
    string? Default,
    bool Local,
    string? Description)
{
    /// <summary>
    /// True when the level supplies this symbol's value. Locals are script-private
    /// and messages are entry points, so neither appears in the JKL's COGS line.
    /// </summary>
    public bool TakesLevelValue => !Local && Type != CogSymbolType.Message;
}

/// <summary>
/// A parsed <c>.cog</c> script — specifically its <c>symbols</c> block, which is
/// what turns a placed COG's bare positional values in the JKL into named,
/// typed parameters.
///
/// The correspondence was verified against retail data: a placed COG's values map
/// in order onto the script's symbols that are neither <c>local</c> nor
/// <c>message</c>. Across `01narshadda`, `03katarn`, `07yun` and `09fuelstation`
/// that matched for every one of the 96 placed COGs whose script was resolvable,
/// with no mismatches.
/// </summary>
public sealed class CogScript
{
    public string Name { get; }
    public IReadOnlyList<CogSymbol> Symbols { get; }

    /// <summary>Symbols the level supplies values for, in the order they appear.</summary>
    public IReadOnlyList<CogSymbol> LevelValues { get; }

    private CogScript(string name, List<CogSymbol> symbols)
    {
        Name = name;
        Symbols = symbols;
        LevelValues = symbols.Where(s => s.TakesLevelValue).ToList();
    }

    public static CogScript Parse(string name, string text)
    {
        var symbols = new List<CogSymbol>();
        bool inSymbols = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = StripComment(raw, out var comment).Trim();

            if (!inSymbols)
            {
                if (line.Equals("symbols", StringComparison.OrdinalIgnoreCase)) inSymbols = true;
                continue;
            }

            if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) break;
            if (line.Length == 0) continue;

            if (ParseSymbol(line, comment) is { } symbol) symbols.Add(symbol);
        }

        return new CogScript(name, symbols);
    }

    /// <summary>
    /// `type name[=default] [local] [nolink] [desc=…] [linkid=…] [mask=…]`.
    /// Modifiers may also be comma-joined in one token (`nolink,desc=…`).
    /// </summary>
    private static CogSymbol? ParseSymbol(string line, string? comment)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        var type = ParseType(tokens[0]);
        if (type == CogSymbolType.Unknown) return null;   // not a symbol line
        if (tokens.Length < 2) return null;

        var name = tokens[1];
        string? def = null;
        int eq = name.IndexOf('=');
        if (eq > 0)
        {
            def = name[(eq + 1)..];
            name = name[..eq];
        }

        bool local = false;
        string? description = null;

        for (int i = 2; i < tokens.Length; i++)
            foreach (var part in tokens[i].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Equals("local", StringComparison.OrdinalIgnoreCase)) local = true;
                else if (part.StartsWith("desc=", StringComparison.OrdinalIgnoreCase)) description = part[5..];
            }

        return new CogSymbol(type, name, def, local, description ?? comment);
    }

    private static CogSymbolType ParseType(string token) => token.ToLowerInvariant() switch
    {
        "message" => CogSymbolType.Message,
        "int" => CogSymbolType.Int,
        "flex" => CogSymbolType.Flex,
        "float" => CogSymbolType.Float,
        "vector" => CogSymbolType.Vector,
        "thing" => CogSymbolType.Thing,
        "sector" => CogSymbolType.Sector,
        "surface" => CogSymbolType.Surface,
        "sound" => CogSymbolType.Sound,
        "material" => CogSymbolType.Material,
        "model" => CogSymbolType.Model,
        "template" => CogSymbolType.Template,
        "keyframe" => CogSymbolType.Keyframe,
        "ai" => CogSymbolType.Ai,
        "cog" => CogSymbolType.Cog,
        _ => CogSymbolType.Unknown,
    };

    private static string StripComment(string line, out string? comment)
    {
        comment = null;
        int cut = line.Length;

        int slashes = line.IndexOf("//", StringComparison.Ordinal);
        if (slashes >= 0) cut = slashes;

        int hash = line.IndexOf('#');
        if (hash >= 0 && hash < cut) cut = hash;

        if (cut < line.Length)
        {
            var text = line[cut..].TrimStart('/', '#', ' ', '\t', '\r').Trim();
            if (text.Length > 0) comment = text;
        }

        return line[..cut];
    }
}
