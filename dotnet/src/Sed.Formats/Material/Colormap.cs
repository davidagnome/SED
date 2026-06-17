using System.Text;

namespace Sed.Formats.Material;

/// <summary>
/// A JK CMP colormap. Only the 256-entry RGB palette is decoded here (the
/// lighting/transparency tables that follow are not needed for base texturing).
///
/// Layout (per graph_files.pas): 64-byte header (sig "CMP ", uint32 ×2, 52 pad
/// bytes) then 256 × { byte r, g, b }. Palette values are full 0–255 range.
/// </summary>
public sealed class Colormap
{
    private const int HeaderSize = 64;
    private const int PaletteEntries = 256;

    /// <summary>256 entries of packed RGBA (a=255), index 0 is treated as transparent.</summary>
    public byte[] Rgba { get; } = new byte[PaletteEntries * 4];

    public static Colormap Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != (byte)'C' || data[1] != (byte)'M' || data[2] != (byte)'P')
            throw new InvalidDataException("Not a CMP colormap (bad magic)");
        if (data.Length < HeaderSize + PaletteEntries * 3)
            throw new InvalidDataException("CMP too small for a palette");

        var cmp = new Colormap();
        int src = HeaderSize;
        for (int i = 0; i < PaletteEntries; i++)
        {
            cmp.Rgba[i * 4 + 0] = data[src++];
            cmp.Rgba[i * 4 + 1] = data[src++];
            cmp.Rgba[i * 4 + 2] = data[src++];
            cmp.Rgba[i * 4 + 3] = (byte)(i == 0 ? 0 : 255);
        }
        return cmp;
    }

    public (byte R, byte G, byte B, byte A) this[int index]
    {
        get
        {
            int i = (index & 0xFF) * 4;
            return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
        }
    }
}
