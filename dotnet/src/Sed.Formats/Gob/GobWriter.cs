using System.Text;

namespace Sed.Formats.Gob;

public static class GobWriter
{
    private const int NameLength = 128;

    public static void Build(string outputPath, IEnumerable<(string name, byte[] data)> entries)
    {
        var list = entries.ToList();

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs, Encoding.ASCII);

        bw.Write(Encoding.ASCII.GetBytes("GOB "));
        bw.Write((uint)0x14);
        bw.Write((uint)0);

        var offsets = new uint[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            offsets[i] = (uint)fs.Position;
            bw.Write(list[i].data);
        }

        uint dirOffset = (uint)fs.Position;
        bw.Write((uint)list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            bw.Write(offsets[i]);
            bw.Write((uint)list[i].data.Length);
            bw.Write(EncodeName(list[i].name));
        }

        fs.Seek(8, SeekOrigin.Begin);
        bw.Write(dirOffset);
    }

    public static void BuildFromFiles(string outputPath, IEnumerable<(string entryName, string filePath)> entries)
    {
        var list = entries.ToList();
        var dataEntries = new List<(string name, byte[] data)>(list.Count);
        foreach (var (entryName, filePath) in list)
            dataEntries.Add((entryName, File.ReadAllBytes(filePath)));
        Build(outputPath, dataEntries);
    }

    private static byte[] EncodeName(string name)
    {
        name = name.Replace('/', '\\').ToLowerInvariant();
        if (name.Length > NameLength - 1)
            name = name[..(NameLength - 1)];

        var bytes = new byte[NameLength];
        Encoding.ASCII.GetBytes(name, 0, name.Length, bytes, 0);
        return bytes;
    }
}
