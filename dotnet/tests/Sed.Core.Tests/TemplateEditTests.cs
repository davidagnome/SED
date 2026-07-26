using Sed.Core.Editing;
using Sed.Core.Model;
using Sed.Formats.Jkl;
using Xunit;

namespace Sed.Core.Tests;

public class TemplateEditTests
{
    private static Level MakeLevel()
    {
        var level = new Level();

        var basic = new Template { Name = "basic", Parent = "", Order = 0 };
        basic.Values["model3d"] = "crate.3do";
        basic.Values["size"] = "0.5";

        var crate = new Template { Name = "crate", Parent = "basic", Order = 1 };
        crate.Values["mass"] = "10";

        var heavy = new Template { Name = "heavycrate", Parent = "crate", Order = 2 };
        heavy.Values["mass"] = "50";

        level.Templates[basic.Name] = basic;
        level.Templates[crate.Name] = crate;
        level.Templates[heavy.Name] = heavy;

        level.Things.Add(new Thing { Name = "c1", Template = "crate" });
        level.Things.Add(new Thing { Name = "c2", Template = "crate" });
        level.Things.Add(new Thing { Name = "h1", Template = "heavycrate" });
        level.RenumberThings();

        return level;
    }

    [Fact]
    public void SetValueAddsChangesAndRemoves()
    {
        var level = MakeLevel();
        var crate = level.Templates["crate"];
        var history = new EditHistory();

        history.Do(new SetTemplateValueCommand(crate, "sprite", "spark.spr"));
        Assert.Equal("spark.spr", crate.Values["sprite"]);

        history.Do(new SetTemplateValueCommand(crate, "sprite", "flame.spr"));
        Assert.Equal("flame.spr", crate.Values["sprite"]);

        // An empty value removes the parameter.
        history.Do(new SetTemplateValueCommand(crate, "sprite", ""));
        Assert.False(crate.Values.ContainsKey("sprite"));

        history.Undo();
        Assert.Equal("flame.spr", crate.Values["sprite"]);
        history.Undo();
        Assert.Equal("spark.spr", crate.Values["sprite"]);
        history.Undo();
        Assert.False(crate.Values.ContainsKey("sprite"));
    }

    [Fact]
    public void InheritanceResolvesThroughTheParentChain()
    {
        var level = MakeLevel();

        Assert.Equal("crate.3do", level.GetTemplateValue("heavycrate", "model3d"));  // from basic
        Assert.Equal("50", level.GetTemplateValue("heavycrate", "mass"));            // own override
        Assert.Equal("10", level.GetTemplateValue("crate", "mass"));
        Assert.Equal("0.5", level.GetTemplateValue("crate", "size"));                // from basic
    }

    [Fact]
    public void ChangingTheParentChangesWhatIsInherited()
    {
        var level = MakeLevel();
        var heavy = level.Templates["heavycrate"];
        var history = new EditHistory();

        history.Do(new SetTemplateParentCommand(heavy, ""));
        Assert.Equal(string.Empty, level.GetTemplateValue("heavycrate", "model3d"));

        history.Undo();
        Assert.Equal("crate.3do", level.GetTemplateValue("heavycrate", "model3d"));
    }

    [Fact]
    public void CreateAndDeleteRoundTrip()
    {
        var level = MakeLevel();
        var history = new EditHistory();
        var fresh = new Template { Name = "barrel", Parent = "basic" };

        history.Do(new CreateTemplateCommand(level, fresh));
        Assert.True(level.Templates.ContainsKey("barrel"));
        Assert.Equal(3, fresh.Order);          // appended after the existing three

        history.Undo();
        Assert.False(level.Templates.ContainsKey("barrel"));

        history.Redo();
        Assert.True(level.Templates.ContainsKey("barrel"));

        history.Do(new DeleteTemplateCommand(level, fresh));
        Assert.False(level.Templates.ContainsKey("barrel"));

        history.Undo();
        Assert.True(level.Templates.ContainsKey("barrel"));
    }

    [Fact]
    public void CountUsersSeesThingsAndChildTemplates()
    {
        var level = MakeLevel();

        // "crate": two things instantiate it, one template inherits from it.
        Assert.Equal(3, DeleteTemplateCommand.CountUsers(level, "crate"));
        Assert.Equal(1, DeleteTemplateCommand.CountUsers(level, "heavycrate"));
        Assert.Equal(1, DeleteTemplateCommand.CountUsers(level, "basic"));
        Assert.Equal(0, DeleteTemplateCommand.CountUsers(level, "nonexistent"));
    }

    [Fact]
    public void RenameRepointsThingsAndChildTemplates()
    {
        var level = MakeLevel();
        var crate = level.Templates["crate"];
        var history = new EditHistory();

        history.Do(new RenameTemplateCommand(level, crate, "box"));

        Assert.True(level.Templates.ContainsKey("box"));
        Assert.False(level.Templates.ContainsKey("crate"));
        Assert.Equal("box", crate.Name);

        // Things that instantiated it follow the rename...
        Assert.Equal(2, level.Things.Count(t => t.Template == "box"));
        // ...and so does the child template's parent link.
        Assert.Equal("box", level.Templates["heavycrate"].Parent);
        Assert.Equal("crate.3do", level.GetTemplateValue("heavycrate", "model3d"));

        history.Undo();
        Assert.True(level.Templates.ContainsKey("crate"));
        Assert.Equal(2, level.Things.Count(t => t.Template == "crate"));
        Assert.Equal("crate", level.Templates["heavycrate"].Parent);
    }

    [Fact]
    public void RenameValidationRejectsClashesAndBadNames()
    {
        var level = MakeLevel();
        var crate = level.Templates["crate"];

        Assert.Contains("already exists", RenameTemplateCommand.Validate(level, crate, "basic"));
        Assert.Contains("needs a name", RenameTemplateCommand.Validate(level, crate, "  "));
        Assert.Contains("cannot contain spaces", RenameTemplateCommand.Validate(level, crate, "my crate"));
        Assert.Null(RenameTemplateCommand.Validate(level, crate, "box"));
        Assert.Null(RenameTemplateCommand.Validate(level, crate, "crate"));   // unchanged is fine
    }

    [Fact]
    public void ParamKindsMatchTheOriginalTable()
    {
        Assert.Equal(TemplateParamKind.Model3do, TemplateParams.KindOf("model3d"));
        Assert.Equal(TemplateParamKind.Material, TemplateParams.KindOf("material"));
        Assert.Equal(TemplateParamKind.SoundClass, TemplateParams.KindOf("soundclass"));
        Assert.Equal(TemplateParamKind.TemplateRef, TemplateParams.KindOf("explode"));
        Assert.Equal(TemplateParamKind.TemplateRef, TemplateParams.KindOf("weapon2"));
        Assert.Equal(TemplateParamKind.Flags, TemplateParams.KindOf("thingflags"));
        Assert.Equal(TemplateParamKind.Frame, TemplateParams.KindOf("frame"));

        // Case-insensitive, and anything unlisted is free text.
        Assert.Equal(TemplateParamKind.Model3do, TemplateParams.KindOf("MODEL3D"));
        Assert.Equal(TemplateParamKind.Text, TemplateParams.KindOf("mass"));
    }

    [Fact]
    public void TemplateOrderSurvivesDeletionSoTheSectionDoesNotChurn()
    {
        const string jkl = """
            SECTION: TEMPLATES

            World templates 3
            alpha none size=1
            beta alpha size=2
            gamma beta size=3
            end

            SECTION: THINGS

            World things 0
            end
            """;

        var doc = JklParser.ParseDocument(jkl);
        var level = doc.Level;
        Assert.Equal(3, level.Templates.Count);

        // Remove the middle one and add another: the survivors keep their order.
        new DeleteTemplateCommand(level, level.Templates["beta"]).Apply();
        new CreateTemplateCommand(level, new Template { Name = "delta", Parent = "alpha" }).Apply();

        var output = JklWriter.Build(doc);
        var names = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("alpha ") || l.StartsWith("gamma ") || l.StartsWith("delta "))
            .Select(l => l.Split(' ')[0])
            .ToList();

        Assert.Equal(new[] { "alpha", "gamma", "delta" }, names);
    }

    [Fact]
    public void EditedTemplatesRoundTripThroughTheWriter()
    {
        const string jkl = """
            SECTION: TEMPLATES

            World templates 2
            alpha none size=1
            beta alpha size=2
            end

            SECTION: THINGS

            World things 0
            end
            """;

        var doc = JklParser.ParseDocument(jkl);
        var level = doc.Level;

        new SetTemplateValueCommand(level.Templates["beta"], "model3d", "thing.3do").Apply();
        new SetTemplateParentCommand(level.Templates["beta"], "").Apply();

        var reloaded = JklParser.Parse(JklWriter.Build(doc));

        Assert.Equal(2, reloaded.Templates.Count);
        Assert.Equal("thing.3do", reloaded.Templates["beta"].Values["model3d"]);
        Assert.Equal("2", reloaded.Templates["beta"].Values["size"]);

        // An empty parent is written as the "none" sentinel; writing nothing would
        // shift "size=2" into the parent slot and silently drop the parameter.
        Assert.Equal("none", reloaded.Templates["beta"].Parent);
    }
}
