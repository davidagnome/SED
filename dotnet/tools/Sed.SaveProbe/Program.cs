using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Game;
using Sed.Formats.Jkl;

// Round-trips a real level: parse → edit a vertex + thing → save → reparse → verify
// the edits applied and every other section (size/line count) is preserved.
//   Sed.SaveProbe <baseDir> <level>
if (args.Length < 2) { Console.Error.WriteLine("usage: Sed.SaveProbe <baseDir> <level>"); return 1; }

using var install = GameInstall.TryOpen(ProjectType.JediKnight, args[0]) ?? throw new DirectoryNotFoundException(args[0]);
var entry = install.Levels.First(e => e.NormalizedName.Contains(args[1].ToLowerInvariant()));
var text = install.ReadLevel(entry);

var doc = JklParser.ParseDocument(text);
Console.WriteLine($"{entry.Name}: {doc.SourceLines.Length} lines, {doc.Level.Sectors.Count} sectors, " +
                  $"{doc.Level.Things.Count} things; vertexLines={doc.VertexLine.Count}, thingLines={doc.ThingLine.Count}");

// Edit: move sector 0's first vertex up by 1.0, and move thing 0 by +2 in X.
var v = doc.Level.Sectors[0].Vertices[0];
var origV = v.Position;
v.Position += new Vec3(0, 0, 1.0);
var th = doc.Level.Things[0];
var origT = th.Position;
th.Position += new Vec3(2, 0, 0);

var outPath = $"/tmp/edited_{args[1]}.jkl";
JklWriter.Save(doc, outPath);
var savedLines = File.ReadAllLines(outPath).Length;
Console.WriteLine($"saved → {outPath} ({savedLines} lines; original {doc.SourceLines.Length})");

// Reparse and verify.
var reloaded = JklParser.Parse(File.ReadAllText(outPath));
var rv = reloaded.Sectors[0].Vertices[0];
var rt = reloaded.Things[0];
bool sectorsOk = reloaded.Sectors.Count == doc.Level.Sectors.Count;
bool thingsOk = reloaded.Things.Count == doc.Level.Things.Count;
bool vertexMoved = System.Math.Abs(rv.Position.Z - (origV.Z + 1.0)) < 1e-3;
bool thingMoved = System.Math.Abs(rt.Position.X - (origT.X + 2.0)) < 1e-3;
bool cogsKept = text.Contains("SECTION: COGS", StringComparison.OrdinalIgnoreCase)
                == File.ReadAllText(outPath).Contains("SECTION: COGS", StringComparison.OrdinalIgnoreCase);

Console.WriteLine($"  sectors preserved: {sectorsOk}, things preserved: {thingsOk}");
Console.WriteLine($"  vertex moved: {vertexMoved} ({origV} -> {rv.Position})");
Console.WriteLine($"  thing moved:  {thingMoved} ({origT} -> {rt.Position})");
Console.WriteLine($"  COGS section preserved: {cogsKept}");
return sectorsOk && thingsOk && vertexMoved && thingMoved && cogsKept ? 0 : 2;
