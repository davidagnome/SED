using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Game;
using Sed.Formats.Jkl;

// Faithful round-trip of a real level: parse -> regenerate GEORESOURCE+SECTORS+THINGS
// -> reparse -> verify geometry counts are preserved and edits persisted.
//   Sed.SaveProbe <baseDir> <level>
if (args.Length < 2) { Console.Error.WriteLine("usage: Sed.SaveProbe <baseDir> <level>"); return 1; }

using var install = GameInstall.TryOpen(ProjectType.JediKnight, args[0]) ?? throw new DirectoryNotFoundException(args[0]);
var entry = install.Levels.First(e => e.NormalizedName.Contains(args[1].ToLowerInvariant()));
var text = install.ReadLevel(entry);

var doc = JklParser.ParseDocument(text);
var L = doc.Level;
int sec0 = L.Sectors.Count, surf0 = L.Sectors.Sum(s => s.Surfaces.Count),
    vtx0 = L.Sectors.Sum(s => s.Vertices.Count), cor0 = L.Sectors.Sum(s => s.Surfaces.Sum(f => f.Corners.Count)),
    adj0 = L.Sectors.Sum(s => s.Surfaces.Count(f => f.Adjoin is not null)), thg0 = L.Things.Count;
Console.WriteLine($"{entry.Name}: {sec0} sectors, {surf0} surfaces, {vtx0} sector-verts, {cor0} corners, {adj0} adjoins, {thg0} things");

// Edits: move a vertex + a thing, add a thing.
var v = L.Sectors[0].Vertices[0]; var origV = v.Position; v.Position += new Vec3(0, 0, 1.0);
var th = L.Things[0]; var origT = th.Position; th.Position += new Vec3(2, 0, 0);
L.Things.Add(new Thing { Template = th.Template, Name = "probe_added", Position = th.Position + new Vec3(0, 0, 1), Sector = th.Sector });
L.RenumberThings();

var outPath = $"/tmp/edited_{args[1]}.jkl";
JklWriter.Save(doc, outPath);

var R = JklParser.Parse(File.ReadAllText(outPath));
int sec1 = R.Sectors.Count, surf1 = R.Sectors.Sum(s => s.Surfaces.Count),
    vtx1 = R.Sectors.Sum(s => s.Vertices.Count), cor1 = R.Sectors.Sum(s => s.Surfaces.Sum(f => f.Corners.Count)),
    adj1 = R.Sectors.Sum(s => s.Surfaces.Count(f => f.Adjoin is not null)), thg1 = R.Things.Count;
Console.WriteLine($"reloaded:   {sec1} sectors, {surf1} surfaces, {vtx1} sector-verts, {cor1} corners, {adj1} adjoins, {thg1} things");

bool geomOk = sec1 == sec0 && surf1 == surf0 && vtx1 == vtx0 && cor1 == cor0 && adj1 == adj0;
bool vMoved = System.Math.Abs(R.Sectors[0].Vertices[0].Position.Z - (origV.Z + 1.0)) < 1e-3;
bool tMoved = System.Math.Abs(R.Things[0].Position.X - (origT.X + 2.0)) < 1e-3;
bool tAdded = thg1 == thg0 + 1 && R.Things.Any(x => x.Name == "probe_added");
bool cogsOk = text.Contains("SECTION: COGS", StringComparison.OrdinalIgnoreCase)
              == File.ReadAllText(outPath).Contains("SECTION: COGS", StringComparison.OrdinalIgnoreCase);

Console.WriteLine($"  geometry counts preserved: {geomOk}");
Console.WriteLine($"  vertex moved: {vMoved}, thing moved: {tMoved}, thing added: {tAdded}, COGS intact: {cogsOk}");
return geomOk && vMoved && tMoved && tAdded && cogsOk ? 0 : 2;
