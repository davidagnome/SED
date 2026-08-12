using Sed.App;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Plugins;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>A plugin that moves every selected thing, to prove the contract works.</summary>
public sealed class NudgeThingsPlugin : ISedPlugin
{
    public string Name => "Nudge Things";
    public string? Description => "Test plugin";

    public IEnumerable<PluginCommand> GetCommands()
    {
        yield return new PluginCommand("Nudge up", ctx =>
        {
            var moves = ctx.Selection.Things
                .Select(IEditCommand (t) => new MoveThingCommand(t, new Vec3(0, 0, 1)))
                .ToList();
            if (moves.Count == 0) { ctx.Report("Nothing selected."); return; }

            ctx.History.Do(moves.Count == 1 ? moves[0] : new CompositeCommand("Nudge", moves));
            ctx.Report($"Nudged {moves.Count} thing(s).");
        });

        yield return new PluginCommand("Count sectors", ctx =>
            ctx.Report($"{ctx.Level.Sectors.Count} sectors."));
    }
}

/// <summary>A plugin whose command throws, to check the host contains it.</summary>
public sealed class FaultyPlugin : ISedPlugin
{
    public string Name => "Faulty";

    public IEnumerable<PluginCommand> GetCommands()
    {
        yield return new PluginCommand("Explode", _ => throw new InvalidOperationException("boom"));
    }
}

public class PluginHostTests
{
    private static (Level level, EditHistory history, SelectionSet selection, List<string> log, PluginContext ctx)
        MakeContext()
    {
        var level = new Level();
        var sector = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "dflt.mat", 0);
        level.Sectors.Add(sector);
        level.RenumberSectors();

        level.Things.Add(new Thing { Name = "a", Sector = sector });
        level.Things.Add(new Thing { Name = "b", Sector = sector });
        level.RenumberThings();

        var history = new EditHistory();
        var selection = new SelectionSet();
        var log = new List<string>();
        return (level, history, selection, log,
            new PluginContext(level, history, selection, log.Add));
    }

    private static PluginCommand CommandFrom<T>(string label) where T : ISedPlugin, new() =>
        new T().GetCommands().First(c => c.Label == label);

    [Fact]
    public void PluginsAreDiscoveredFromAnAssembly()
    {
        var host = new PluginHost();
        host.AddFromAssembly(typeof(NudgeThingsPlugin).Assembly);

        var plugin = Assert.Single(host.Plugins, p => p.Name == "Nudge Things");
        Assert.Equal("Test plugin", plugin.Description);
        Assert.Equal(2, plugin.Commands.Count);
    }

    [Fact]
    public void APluginEditsThroughTheUndoStack()
    {
        var (level, history, selection, _, ctx) = MakeContext();
        selection.Add(level.Things[0]);
        selection.Add(level.Things[1]);

        PluginHost.Invoke(CommandFrom<NudgeThingsPlugin>("Nudge up"), ctx);

        Assert.Equal(1.0, level.Things[0].Position.Z, 6);
        Assert.Equal(1.0, level.Things[1].Position.Z, 6);

        // The whole point of the managed contract: it is one ordinary undo step.
        Assert.True(history.CanUndo);
        history.Undo();
        Assert.Equal(0.0, level.Things[0].Position.Z, 6);
        Assert.Equal(0.0, level.Things[1].Position.Z, 6);
    }

    [Fact]
    public void PluginsSeeTheLiveLevelAndSelection()
    {
        var (level, _, selection, log, ctx) = MakeContext();

        PluginHost.Invoke(CommandFrom<NudgeThingsPlugin>("Count sectors"), ctx);
        Assert.Contains("1 sectors.", log);

        // And the empty-selection path reports rather than editing.
        log.Clear();
        PluginHost.Invoke(CommandFrom<NudgeThingsPlugin>("Nudge up"), ctx);
        Assert.Contains("Nothing selected.", log);
        Assert.Equal(0.0, level.Things[0].Position.Z, 6);
    }

    [Fact]
    public void AThrowingPluginIsContained()
    {
        var (_, _, _, _, ctx) = MakeContext();

        var result = PluginHost.Invoke(CommandFrom<FaultyPlugin>("Explode"), ctx);

        Assert.Contains("failed", result);
        Assert.Contains("boom", result);
    }

    [Fact]
    public void AMissingPluginDirectoryIsNotAnError()
    {
        var host = new PluginHost();
        host.LoadFrom(Path.Combine(Path.GetTempPath(), "sed-plugins-does-not-exist-" + Guid.NewGuid()));

        Assert.Empty(host.Plugins);
        Assert.Empty(host.Problems);
    }

    [Fact]
    public void ANonPluginAssemblyIsReportedRatherThanSilentlyIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sed-plugins-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            // Not a managed assembly at all.
            File.WriteAllText(Path.Combine(dir, "junk.dll"), "not an assembly");

            var host = new PluginHost();
            host.LoadFrom(dir);

            Assert.Empty(host.Plugins);
            Assert.Single(host.Problems);
            Assert.Contains("junk.dll", host.Problems[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ContractAssembliesAreSharedNotDuplicated()
    {
        // The host and a plugin must agree on the identity of the contract types.
        // If Sed.Core were loaded per-plugin, the Level a plugin receives would be
        // a different type from the one it declares and every call would fail.
        var contract = typeof(ISedPlugin).Assembly.GetName().Name;
        var model = typeof(Level).Assembly.GetName().Name;

        Assert.Equal("Sed.Plugins", contract);
        Assert.Equal("Sed.Core", model);
    }
}
