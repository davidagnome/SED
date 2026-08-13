using System.Globalization;

namespace Sed.Formats.Game;

/// <summary>One sequence record of an episode.jk file (TJKRec).</summary>
public sealed class EpisodeSequence
{
    public int Line;
    public int Cd;
    public int LevelNum;
    public string Type = "LEVEL";
    public string File = string.Empty;
    public int LightPow;
    public int DarkPow;
    public int GotoA = -1;
    public int GotoB = -1;
}

/// <summary>
/// The episode definition file (episode.jk, per U_MEDIT.PAS): the episode name,
/// game type, and the ordered sequence of levels the game walks through.
/// </summary>
public sealed class EpisodeFile
{
    public string Name = string.Empty;
    public int GameType = 1; // 1 = single player, 2 = deathmatch, 8 = special/CTF
    public List<EpisodeSequence> Sequences { get; } = new();

    public static EpisodeFile Parse(string text)
    {
        var episode = new EpisodeFile();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        int i = 0;
        string? Next()
        {
            while (i < lines.Length)
            {
                var s = StripComment(lines[i++]).Trim();
                if (s.Length > 0) return s;
            }
            return null;
        }

        var nameLine = Next();
        if (nameLine is not null)
        {
            var name = nameLine.Trim('"', ' ', '\t');
            episode.Name = name;
        }

        var typeLine = Next();
        if (typeLine is not null && FirstWord(typeLine).Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            episode.GameType = ParseInt(WordAt(typeLine, 1));

        var seqLine = Next();
        int n = 0;
        if (seqLine is not null && FirstWord(seqLine).Equals("SEQ", StringComparison.OrdinalIgnoreCase))
            n = ParseInt(WordAt(seqLine, 1));

        for (int k = 0; k < n; k++)
        {
            var s = Next();
            if (s is null) break;
            var seq = ParseSequence(s);
            episode.Sequences.Add(seq);
        }
        return episode;
    }

    /// <summary>Parses one sequence line: "<line>: <cd> <level> <type> <file> <lpow> <dpow> <gotoA> <gotoB>".</summary>
    public static EpisodeSequence ParseSequence(string line)
    {
        var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var seq = new EpisodeSequence();
        if (t.Length >= 1 && int.TryParse(t[0].TrimEnd(':'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNum))
            seq.Line = lineNum;
        if (t.Length >= 2) seq.Cd = ParseInt(t[1]);
        if (t.Length >= 3) seq.LevelNum = ParseInt(t[2]);
        if (t.Length >= 4) seq.Type = t[3];
        if (t.Length >= 5) seq.File = t[4];
        if (t.Length >= 6) seq.LightPow = ParseInt(t[5]);
        if (t.Length >= 7) seq.DarkPow = ParseInt(t[6]);
        if (t.Length >= 8) seq.GotoA = ParseInt(t[7]);
        if (t.Length >= 9) seq.GotoB = ParseInt(t[8]);
        return seq;
    }

    public string Build()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"\"{Name}\"");
        sb.AppendLine();
        sb.AppendLine($"TYPE {GameType}");
        sb.AppendLine();
        sb.AppendLine($"SEQ {Sequences.Count}");
        sb.AppendLine();
        sb.AppendLine("# <line> <cd>  <level>  <type>   <file>         <lightpow>  <darkpow>   <gotoA>  <gotoB>");
        sb.AppendLine();
        foreach (var s in Sequences)
            sb.AppendLine($"{s.Line}:\t{s.Cd}\t{s.LevelNum}\t{s.Type}\t{s.File}\t{s.LightPow}\t{s.DarkPow}\t{s.GotoA}\t{s.GotoB}");
        sb.AppendLine();
        sb.AppendLine("end");
        return sb.ToString();
    }

    private static string StripComment(string s)
    {
        int p = s.IndexOf('#');
        return p >= 0 ? s[..p] : s;
    }

    private static string FirstWord(string s) => WordAt(s, 0);

    private static string WordAt(string s, int index)
    {
        var t = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return index < t.Length ? t[index] : string.Empty;
    }

    private static int ParseInt(string s) =>
        int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
}

/// <summary>
/// The COG string table (cogstrings.uni, per TCOGStrings): an ordered list of
/// <c>"&lt;key&gt;" 0 "&lt;string&gt;"</c> entries, used for level names and the
/// mission text the game shows before a level.
/// </summary>
public sealed class CogStrings
{
    public List<(string Key, string Value)> Entries { get; } = new();

    public string GetString(string key) =>
        Entries.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    public void SetString(string key, string value)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                Entries[i] = (Entries[i].Key, value);
                return;
            }
        }
        Entries.Add((key, value));
    }

    public void RemoveString(string key) => Entries.RemoveAll(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static CogStrings Parse(string? text)
    {
        var strings = new CogStrings();
        if (string.IsNullOrWhiteSpace(text)) return strings;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var s = StripComment(raw).Trim();
            if (s.Length == 0) continue;
            if (s.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
            if (FirstWord(s).Equals("MSGS", StringComparison.OrdinalIgnoreCase)) continue;
            // "key" <unused> "string"
            var key = ReadQuoted(s);
            if (key is null) continue;
            var rest = s[(s.IndexOf('"', 1) + 1)..];
            var value = ReadQuoted(rest) ?? string.Empty;
            strings.Entries.Add((key, value));
        }
        return strings;
    }

    public string Build()
    {
        if (Entries.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"MSGS {Entries.Count}");
        sb.AppendLine();
        sb.AppendLine("#  \"<key>\"     <unused number>   \"<string>\"");
        sb.AppendLine();
        foreach (var (key, value) in Entries)
            sb.AppendLine($"\"{key}\" 0 \"{value}\"");
        sb.AppendLine();
        sb.AppendLine("END");
        return sb.ToString();
    }

    private static string? ReadQuoted(string s)
    {
        int open = s.IndexOf('"');
        if (open == -1) return null;
        int close = s.IndexOf('"', open + 1);
        if (close == -1) return null;
        return s[(open + 1)..close];
    }

    private static string StripComment(string s)
    {
        int p = s.IndexOf('#');
        return p >= 0 ? s[..p] : s;
    }

    private static string FirstWord(string s) =>
        s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
}
