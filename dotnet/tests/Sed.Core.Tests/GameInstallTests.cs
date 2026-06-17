using System.Text;
using Sed.Core.Model;
using Sed.Formats.Game;
using Xunit;

namespace Sed.Core.Tests;

public class GameInstallTests
{
    private static byte[] Gob(params (string name, string content)[] files)
    {
        const int nameLen = 128, entry = 4 + 4 + nameLen, dirOffset = 12;
        int dataStart = dirOffset + 4 + files.Length * entry;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII);
        bw.Write(Encoding.ASCII.GetBytes("GOB "));
        bw.Write((uint)0x14);
        bw.Write((uint)dirOffset);
        bw.Write((uint)files.Length);
        int cursor = dataStart;
        foreach (var (name, content) in files)
        {
            bw.Write((uint)cursor);
            bw.Write((uint)content.Length);
            var nb = new byte[nameLen];
            Encoding.ASCII.GetBytes(name).CopyTo(nb, 0);
            bw.Write(nb);
            cursor += content.Length;
        }
        foreach (var (_, content) in files) bw.Write(Encoding.ASCII.GetBytes(content));
        return ms.ToArray();
    }

    [Fact]
    public void TryOpen_LocatesArchivesUnderEpisodeAndResource()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "sed_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "Episode"));
            Directory.CreateDirectory(Path.Combine(baseDir, "Resource"));
            File.WriteAllBytes(Path.Combine(baseDir, "Episode", "Jk1.gob"),
                Gob(("jkl\\01test.jkl", "SECTION: HEADER")));
            File.WriteAllBytes(Path.Combine(baseDir, "Resource", "Res2.gob"),
                Gob(("3do\\mat\\wall.mat", "fake")));

            Assert.True(GameInstall.IsValid(ProjectType.JediKnight, baseDir));

            using var install = GameInstall.TryOpen(ProjectType.JediKnight, baseDir);
            Assert.NotNull(install);
            Assert.NotNull(install!.LevelArchive);
            Assert.Single(install.ResourceArchives);
            Assert.Single(install.Levels);
            Assert.Equal("jkl\\01test.jkl", install.Levels.First().Name);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void TryOpen_ReturnsNullForEmptyDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "sed_empty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            Assert.False(GameInstall.IsValid(ProjectType.JediKnight, baseDir));
            Assert.Null(GameInstall.TryOpen(ProjectType.JediKnight, baseDir));
        }
        finally { Directory.Delete(baseDir, recursive: true); }
    }
}
