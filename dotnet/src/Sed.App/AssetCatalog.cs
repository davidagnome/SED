using Sed.Formats.Gob;

namespace Sed.App;

/// <summary>
/// Lists the assets available in the open game archives, by extension, so fields
/// that name an asset can offer a pick list instead of a bare text box.
///
/// Enumeration is lazy and cached: a retail install has ~2,000 MAT files and ~500
/// each of COG/KEY/3DO, so the lists are built only when a picker is actually
/// opened, and only once per session.
/// </summary>
public sealed class AssetCatalog
{
    private readonly List<GobArchive> _archives = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AssetCatalog(params IEnumerable<GobArchive?>[] sources)
    {
        foreach (var group in sources)
            foreach (var archive in group)
                if (archive is not null) _archives.Add(archive);
    }

    /// <summary>Every asset with the given extension (".mat"), sorted, deduplicated.</summary>
    public IReadOnlyList<string> ByExtension(string extension)
    {
        if (_cache.TryGetValue(extension, out var cached)) return cached;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archive in _archives)
            foreach (var entry in archive.Entries)
            {
                var name = entry.NormalizedName;
                if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                // Archive paths are "mat\wall.mat"; fields hold the bare filename.
                int slash = name.LastIndexOf('/');
                seen.Add(slash >= 0 ? name[(slash + 1)..] : name);
            }

        var list = seen.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _cache[extension] = list;
        return list;
    }

    /// <summary>The file extension a template parameter kind names, or null if it isn't a file.</summary>
    public static string? ExtensionFor(Sed.Core.Model.TemplateParamKind kind) => kind switch
    {
        Sed.Core.Model.TemplateParamKind.Material => ".mat",
        Sed.Core.Model.TemplateParamKind.Model3do => ".3do",
        Sed.Core.Model.TemplateParamKind.SoundClass => ".snd",
        Sed.Core.Model.TemplateParamKind.Puppet => ".pup",
        Sed.Core.Model.TemplateParamKind.AiClass => ".ai",
        Sed.Core.Model.TemplateParamKind.Cog => ".cog",
        Sed.Core.Model.TemplateParamKind.Sprite => ".spr",
        Sed.Core.Model.TemplateParamKind.Particle => ".par",
        _ => null,
    };

    /// <summary>The file extension a COG symbol type names, or null if it isn't a file.</summary>
    public static string? ExtensionFor(Sed.Formats.Cogs.CogSymbolType type) => type switch
    {
        Sed.Formats.Cogs.CogSymbolType.Material => ".mat",
        Sed.Formats.Cogs.CogSymbolType.Model => ".3do",
        Sed.Formats.Cogs.CogSymbolType.Sound => ".wav",
        Sed.Formats.Cogs.CogSymbolType.Keyframe => ".key",
        Sed.Formats.Cogs.CogSymbolType.Ai => ".ai",
        Sed.Formats.Cogs.CogSymbolType.Cog => ".cog",
        _ => null,
    };
}
