namespace Sed.Core.Model;

/// <summary>
/// What a template parameter refers to. Transcribed from the `tplNames` and
/// `TplVtypes` tables in `VALUES.PAS` — the original classifies only a handful of
/// keys and treats everything else as free text, so this is deliberately small
/// rather than an invented taxonomy.
/// </summary>
public enum TemplateParamKind
{
    /// <summary>Anything not in the table: a number, string or flag word.</summary>
    Text,

    Material,
    Cog,
    SoundClass,
    Puppet,
    Sprite,
    Particle,
    Model3do,
    AiClass,

    /// <summary>Names another template (creatething, explode, weapon, …).</summary>
    TemplateRef,

    /// <summary>A position/orientation frame.</summary>
    Frame,

    /// <summary>A flag word, best edited in hex.</summary>
    Flags,
}

/// <summary>Classifies template parameters by name (`VALUES.PAS: InitValues`).</summary>
public static class TemplateParams
{
    private static readonly Dictionary<string, TemplateParamKind> Kinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["material"] = TemplateParamKind.Material,
            ["cog"] = TemplateParamKind.Cog,
            ["soundclass"] = TemplateParamKind.SoundClass,
            ["puppet"] = TemplateParamKind.Puppet,
            ["sprite"] = TemplateParamKind.Sprite,
            ["particle"] = TemplateParamKind.Particle,
            ["model3d"] = TemplateParamKind.Model3do,
            ["aiclass"] = TemplateParamKind.AiClass,

            ["creatething"] = TemplateParamKind.TemplateRef,
            ["explode"] = TemplateParamKind.TemplateRef,
            ["fleshhit"] = TemplateParamKind.TemplateRef,
            ["weapon"] = TemplateParamKind.TemplateRef,
            ["weapon2"] = TemplateParamKind.TemplateRef,
            ["debris"] = TemplateParamKind.TemplateRef,
            ["trailthing"] = TemplateParamKind.TemplateRef,

            ["frame"] = TemplateParamKind.Frame,
            ["thingflags"] = TemplateParamKind.Flags,
        };

    public static TemplateParamKind KindOf(string key) =>
        Kinds.TryGetValue(key, out var kind) ? kind : TemplateParamKind.Text;

    /// <summary>A short human label for the kind, shown beside the field.</summary>
    public static string Describe(TemplateParamKind kind) => kind switch
    {
        TemplateParamKind.Material => "material",
        TemplateParamKind.Cog => "cog",
        TemplateParamKind.SoundClass => "sound class",
        TemplateParamKind.Puppet => "puppet",
        TemplateParamKind.Sprite => "sprite",
        TemplateParamKind.Particle => "particle",
        TemplateParamKind.Model3do => "3DO model",
        TemplateParamKind.AiClass => "AI class",
        TemplateParamKind.TemplateRef => "template",
        TemplateParamKind.Frame => "frame",
        TemplateParamKind.Flags => "flags",
        _ => string.Empty,
    };

    /// <summary>Every parameter name the original classifies, for completion lists.</summary>
    public static IEnumerable<string> KnownKeys => Kinds.Keys;
}
