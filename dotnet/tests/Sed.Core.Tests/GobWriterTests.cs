using System.Text;
using Sed.Formats.Gob;
using Xunit;

namespace Sed.Core.Tests;

public class GobWriterTests
{
    [Fact]
    public void WrittenGob_CanBeReadBack()
    {
        var entries = new (string name, byte[] data)[]
        {
            ("cog\\hello.cog", new byte[] { 0xDE, 0xAD }),
            ("jkl\\level.jkl", new byte[] { 0x01, 0x02, 0x03 }),
            ("mat\\texture.mat", Encoding.ASCII.GetBytes("texture data"))
        };

        var expected = entries
            .Select(e => (Name: e.name.Replace('\\', '/').ToLowerInvariant(), e.data))
            .ToList();

        string tempFile = Path.GetTempFileName();
        try
        {
            GobWriter.Build(tempFile, entries);

            using var gob = GobArchive.Open(tempFile);

            Assert.Equal(entries.Length, gob.Entries.Count);

            foreach (var (name, data) in expected)
            {
                var entry = Assert.Single(gob.Entries, e => e.NormalizedName == name);
                Assert.Equal(data, gob.ReadBytes(entry));
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
