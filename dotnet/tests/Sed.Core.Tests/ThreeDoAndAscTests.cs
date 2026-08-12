using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Asc;
using Sed.Formats.ThreeDo;
using Xunit;

namespace Sed.Core.Tests;

public class ThreeDoWriterTests
{
    private static ThreeDoModel BoxModel()
    {
        var model = new ThreeDoModel { Version = 2.1 };
        model.Materials.Add("box.mat");
        var mesh = new Mesh3do { Name = "box" };
        mesh.Vertices.Add(new Vec3(0, 0, 0));
        mesh.Vertices.Add(new Vec3(1, 0, 0));
        mesh.Vertices.Add(new Vec3(1, 1, 0));
        mesh.Vertices.Add(new Vec3(0, 1, 0));
        mesh.Uvs.Add(new Vec2(0, 0));
        mesh.Uvs.Add(new Vec2(1, 0));
        mesh.Uvs.Add(new Vec2(1, 1));
        mesh.Uvs.Add(new Vec2(0, 1));
        var face = new Face3do { Material = 0, FaceFlags = 0x01, Geo = 4, Light = 3, Tex = 3, ExtraLight = 0.5f };
        face.VertexIndices.AddRange(new[] { 0, 1, 2, 3 });
        face.UvIndices.AddRange(new[] { 0, 1, 2, 3 });
        mesh.Faces.Add(face);
        model.Meshes.Add(mesh);
        model.Nodes.Add(new HierarchyNode { Mesh = 0, Parent = -1 });
        return model;
    }

    [Fact]
    public void WrittenModelRoundTripsThroughTheParser()
    {
        var text = ThreeDoWriter.Build(BoxModel());

        var parsed = ThreeDoParser.Parse(text);

        Assert.Equal(2.1, parsed.Version, 5);
        Assert.Equal(new[] { "box.mat" }, parsed.Materials);
        var mesh = Assert.Single(parsed.Meshes);
        Assert.Equal("box", mesh.Name, ignoreCase: true);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(4, mesh.Uvs.Count);
        var face = Assert.Single(mesh.Faces);
        Assert.Equal(0, face.Material);
        Assert.Equal(new[] { 0, 1, 2, 3 }, face.VertexIndices);
        Assert.Equal(new[] { 0, 1, 2, 3 }, face.UvIndices);
        var node = Assert.Single(parsed.Nodes);
        Assert.Equal(0, node.Mesh);
    }

    [Fact]
    public void SharedTextureVerticesAreDeduplicated()
    {
        var model = BoxModel();
        // Two faces sharing UV (0,0) → one TEXTURE VERTICES entry.
        var face2 = new Face3do { Material = 0 };
        face2.VertexIndices.AddRange(new[] { 0, 3, 2 });
        face2.UvIndices.AddRange(new[] { 0, 3, 2 });
        model.Meshes[0].Faces.Add(face2);

        var text = ThreeDoWriter.Build(model);
        Assert.Contains("TEXTURE VERTICES 4", text);

        var parsed = ThreeDoParser.Parse(text);
        Assert.Equal(4, parsed.Meshes[0].Uvs.Count);
        Assert.Equal(2, parsed.Meshes[0].Faces.Count);
    }

    [Fact]
    public void FaceEngineFieldsAreWrittenAsHexAndNumbers()
    {
        var text = ThreeDoWriter.Build(BoxModel());
        Assert.Contains("0: 0 0x1 4 3 3 0.5000 4", text);
    }

    [Fact]
    public void SectorExportProducesAParseableModel()
    {
        var level = new Level();
        var box = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "wall.mat", 0);
        level.Sectors.Add(box);
        level.RenumberSectors();

        var model = ThreeDoExport.BuildModel(level, new[] { box }, _ => "layer0");

        Assert.Single(model.Meshes);
        Assert.Contains("wall.mat", model.Materials);
        var mesh = model.Meshes[0];

        // 6 faces (no adjoins in a fresh box) — floor+ceiling+4 walls.
        Assert.Equal(6, mesh.Faces.Count);

        // Vertices deduplicated by position: 8 corners, not per-surface copies.
        Assert.Equal(8, mesh.Vertices.Count);
        Assert.All(mesh.Faces, f => Assert.True(f.VertexIndices.Count is 3 or 4));

        var text = ThreeDoWriter.Build(model);
        var parsed = ThreeDoParser.Parse(text);
        Assert.Equal(6, parsed.Meshes[0].Faces.Count);
        Assert.Equal(8, parsed.Meshes[0].Vertices.Count);
    }

    [Fact]
    public void AdjoinedSurfacesAreSkippedInTheExport()
    {
        var level = new Level();
        var a = SectorFactory.CreateBox(level, new Vec3(-1, 0, 0), 1.0, "a.mat", 0);
        var b = SectorFactory.CreateBox(level, new Vec3(1, 0, 0), 1.0, "b.mat", 0);
        level.Sectors.Add(a);
        level.Sectors.Add(b);
        level.RenumberSectors();

        // Adjoin one face of each box.
        var fa = a.Surfaces[0];
        var fb = b.Surfaces[0];
        fa.Adjoin = fb;
        fb.Adjoin = fa;

        var model = ThreeDoExport.BuildModel(level, new[] { a, b }, _ => "layer0");

        var mesh = Assert.Single(model.Meshes);
        Assert.Equal(10, mesh.Faces.Count); // 12 − 2 adjoined
    }
}

public class AscImportTests
{
    private const string Asc = """
        # 3D Studio ASCII
        Tri-mesh, "box":
        Vertex list:
        Vertex 0: 0.0 0.0 0.0
        Vertex 1: 1.0 0.0 0.0
        Vertex 2: 1.0 1.0 0.0
        Vertex 3: 0.0 1.0 0.0
        Face list:
        Face 0: A: 0 B: 1 C: 2
        Face 1: A: 0 B: 2 C: 3
        """;

    [Fact]
    public void StandardAscLayoutImportsOneSectorPerTriMesh()
    {
        var level = AscImporter.Import(Asc);

        var sec = Assert.Single(level.Sectors);
        Assert.Equal(4, sec.Vertices.Count);
        Assert.Equal(2, sec.Surfaces.Count);

        // Triangles reference the mesh's shared vertices.
        var f0 = sec.Surfaces[0];
        Assert.Equal(3, f0.Corners.Count);
        Assert.Same(sec.Vertices[0], f0.Corners[0].Vertex);
        Assert.Same(sec.Vertices[1], f0.Corners[1].Vertex);
        Assert.Same(sec.Vertices[2], f0.Corners[2].Vertex);
        Assert.True(f0.Normal.Length > 0.9);
    }

    [Fact]
    public void CountBearingTriMeshHeaderIsHonoured()
    {
        const string counted = """
            Tri-mesh, "box": vertices: 4 faces: 2
            Vertex list:
            Vertex 0: 0.0 0.0 0.0
            Vertex 1: 1.0 0.0 0.0
            Vertex 2: 1.0 1.0 0.0
            Vertex 3: 0.0 1.0 0.0
            Face list:
            Face 0: A: 0 B: 1 C: 2
            Face 1: A: 0 B: 2 C: 3
            """;

        var level = AscImporter.Import(counted);
        var sec = Assert.Single(level.Sectors);
        Assert.Equal(4, sec.Vertices.Count);
        Assert.Equal(2, sec.Surfaces.Count);
    }

    [Fact]
    public void MultipleMeshesBecomeMultipleSectors()
    {
        var level = AscImporter.Import(Asc + "\n" + """
            Tri-mesh, "roof":
            Vertex list:
            Vertex 0: 0.0 0.0 1.0
            Vertex 1: 1.0 0.0 1.0
            Vertex 2: 1.0 1.0 1.0
            Face list:
            Face 0: A: 0 B: 1 C: 2
            """);

        Assert.Equal(2, level.Sectors.Count);
        Assert.Single(level.Things); // the original's default thing
    }
}
