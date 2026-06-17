using Sed.Core.Model;
using Sed.Formats.ThreeDo;
using Xunit;

namespace Sed.Core.Tests;

public class ThreeDoTests
{
    private const string SampleModel = """
        SECTION: HEADER
        3DO 2.1
        END

        SECTION: MODELRESOURCE
        MATERIALS 1
        0: wall.mat
        END

        SECTION: GEOMETRYDEF
        RADIUS 1.0
        INSERT OFFSET 0.0 0.0 0.0
        GEOSETS 1
        GEOSET 0
        MESHES 1
        MESH 0
        NAME tri
        GEOMETRYMODE 4
        LIGHTINGMODE 3
        TEXTUREMODE 3
        VERTICES 3
        0: 0.0 0.0 0.0
        1: 1.0 0.0 0.0
        2: 0.0 1.0 0.0
        TEXTURE VERTICES 3
        0: 0.0 0.0
        1: 1.0 0.0
        2: 0.0 1.0
        VERTEX NORMALS 3
        0: 0.0 0.0 1.0
        1: 0.0 0.0 1.0
        2: 0.0 0.0 1.0
        FACES 1
        0: 0 0x0 3 0 0 0.5 3  0,0 1,1 2,2
        FACE NORMALS 1
        0: 0.0 0.0 1.0
        END

        SECTION: HIERARCHYDEF
        HIERARCHY NODES 1
        0: 0x0 0x00010 0 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 root
        END
        """;

    [Fact]
    public void Parses_MeshGeometryMaterialsAndHierarchy()
    {
        var model = ThreeDoParser.Parse(SampleModel);

        Assert.Equal(2.1, model.Version, 3);
        Assert.Single(model.Materials);
        Assert.Equal("wall.mat", model.Materials[0]);

        var mesh = Assert.Single(model.Meshes);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal(3, mesh.Uvs.Count);
        Assert.Equal(new Sed.Core.Math.Vec3(1, 0, 0), mesh.Vertices[1]);

        var face = Assert.Single(mesh.Faces);
        Assert.Equal(0, face.Material);
        Assert.Equal(3, face.VertexIndices.Count);
        Assert.Equal(new[] { 0, 1, 2 }, face.VertexIndices);
        Assert.Equal(new[] { 0, 1, 2 }, face.UvIndices);

        var node = Assert.Single(model.Nodes);
        Assert.Equal(0, node.Mesh);
        Assert.Equal(-1, node.Parent);
    }
}

public class TemplateResolutionTests
{
    [Fact]
    public void GetThingModel_FollowsParentChain()
    {
        var level = new Level();
        level.Templates["base"] = new Template { Name = "base", Values = { ["model3d"] = "ky.3do" } };
        level.Templates["player"] = new Template { Name = "player", Parent = "base" };

        var thing = new Thing { Template = "player" };
        Assert.Equal("ky.3do", level.GetThingModel(thing));
    }

    [Fact]
    public void GetThingModel_ThingParamOverridesTemplate()
    {
        var level = new Level();
        level.Templates["crate"] = new Template { Name = "crate", Values = { ["model3d"] = "crate.3do" } };
        var thing = new Thing { Template = "crate" };
        thing.Values["model3d"] = "special.3do";
        Assert.Equal("special.3do", level.GetThingModel(thing));
    }

    [Fact]
    public void GetThingModel_EmptyWhenUnknown()
    {
        var level = new Level();
        Assert.Equal("", level.GetThingModel(new Thing { Template = "nope" }));
    }
}
