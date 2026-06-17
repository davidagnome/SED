using System.Globalization;

namespace Sed.Formats.Jkl;

/// <summary>
/// Low-level line reader for the JKL text format, mirroring GetNextLine in the
/// original SED loader (LEVEL_IO.INC): strips '#' comments, trims, optionally
/// upper-cases, and tracks SECTION:/END boundaries.
/// </summary>
public sealed class JklReader
{
    private readonly TextReader _reader;

    public string Current { get; private set; } = string.Empty;
    public string Section { get; private set; } = string.Empty;
    public bool EndOfSection { get; private set; }
    public bool Eof { get; private set; }
    public int LineNumber { get; private set; }

    public JklReader(TextReader reader) => _reader = reader;

    /// <summary>Reads the next non-empty line. Returns false at end of file.</summary>
    public bool Next(bool upper = true)
    {
        Current = string.Empty;
        while (true)
        {
            var line = _reader.ReadLine();
            if (line is null)
            {
                Eof = true;
                EndOfSection = true;
                return false;
            }
            LineNumber++;

            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (upper) line = line.ToUpperInvariant();
            if (line.Length == 0) continue;

            Current = line;
            break;
        }

        if (string.Equals(Current, "END", StringComparison.OrdinalIgnoreCase))
        {
            Section = string.Empty;
            EndOfSection = true;
        }
        var first = FirstWord(Current);
        if (string.Equals(first, "SECTION:", StringComparison.OrdinalIgnoreCase))
        {
            Section = WordAt(Current, 1);
            EndOfSection = true;
        }
        return true;
    }

    /// <summary>Advances until a SECTION: header is found or EOF; used by the dispatch loop.</summary>
    public void SeekSection()
    {
        while (Section.Length == 0 && !Eof)
            Next();
    }

    /// <summary>Marks the current section as being consumed: clears the name and the end flag.</summary>
    public void EnterSection()
    {
        Section = string.Empty;
        EndOfSection = false;
    }

    public void SkipToNextSection()
    {
        while (!EndOfSection) Next();
    }

    // ---- token helpers ----

    public static string[] Tokens(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static string FirstWord(string line) => WordAt(line, 0);

    public static string WordAt(string line, int index)
    {
        var t = Tokens(line);
        return index < t.Length ? t[index] : string.Empty;
    }

    /// <summary>Drops a leading "N:" index token (e.g. "12:") if present.</summary>
    public static string StripIndex(string line)
    {
        int colon = line.IndexOf(':');
        return colon >= 0 ? line[(colon + 1)..].Trim() : line;
    }

    public static double ParseFloat(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;

    public static int ParseInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static long ParseHex(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0X", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
