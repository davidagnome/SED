using Sed.Core;
using Sed.Core.Editing;
using Sed.Core.Model;
using Sed.Formats.Game;
using Sed.Formats.Jkl;

// Exercises the geometry + texture operations that the editor's Geometry and
// Texturing menus invoke, against a REAL level (the unit tests use synthetic
// quads). Each op runs through EditHistory, is undone and redone to prove
// reversibility, then the level is saved and reparsed to confirm the result
// survives a JKL round-trip.
//   Sed.OpsProbe <baseDir> <level>
if (args.Length < 2) { Console.Error.WriteLine("usage: Sed.OpsProbe <baseDir> <level>"); return 1; }

using var install = GameInstall.TryOpen(ProjectType.JediKnight, args[0]) ?? throw new DirectoryNotFoundException(args[0]);
var entry = install.Levels.First(e => e.NormalizedName.Contains(args[1].ToLowerInvariant()));
var doc = JklParser.ParseDocument(install.ReadLevel(entry));
var level = doc.Level;

int sec0 = level.Sectors.Count;
int surf0 = level.Sectors.Sum(s => s.Surfaces.Count);
int adj0 = level.Sectors.Sum(s => s.Surfaces.Count(f => f.Adjoin is not null));
Console.WriteLine($"{entry.Name}: {sec0} sectors, {surf0} surfaces, {adj0} adjoins");
Console.WriteLine();

var history = new EditHistory();
var failures = new List<string>();

void Check(string label, bool ok, string detail = "")
{
    Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {label}{(detail.Length > 0 ? " — " + detail : "")}");
    if (!ok) failures.Add(label);
}

// Pick a few well-formed quad surfaces with no existing adjoin to operate on.
var candidates = level.Sectors
    .SelectMany(s => s.Surfaces)
    .Where(f => f.Corners.Count == 4 && f.Adjoin is null)
    .Take(8)
    .ToList();

if (candidates.Count < 4) { Console.Error.WriteLine("not enough plain quad surfaces to probe"); return 1; }

// ---- Extrude ----
{
    var target = candidates[0];
    int before = level.Sectors.Count;
    history.Do(new ExtrudeSurfaceCommand(target, 0.1));
    bool grew = level.Sectors.Count == before + 1 && target.Adjoin is not null;
    history.Undo();
    bool undone = level.Sectors.Count == before && target.Adjoin is null;
    history.Redo();
    bool redone = level.Sectors.Count == before + 1;
    Check("Extrude creates a sector, undo/redo round-trips", grew && undone && redone,
        $"{before} → {level.Sectors.Count} sectors");
}

// ---- Flip ----
{
    var target = candidates[1];
    target.RecalcNormal();
    var before = target.Normal;
    history.Do(new FlipSurfaceCommand(target));
    bool flipped = target.Normal.Dot(before) < -0.9;
    history.Undo();
    target.RecalcNormal();
    bool restored = target.Normal.Dot(before) > 0.9;
    history.Redo();
    Check("Flip reverses the normal and undo restores it", flipped && restored);
}

// ---- Cleave, using the same mid-plane the menu command picks ----
{
    var target = candidates[2];
    var sector = target.Sector;
    int before = sector.Surfaces.Count;
    var (normal, point) = GeometryOps.MidCleavePlane(target);
    history.Do(new CleaveSurfaceCommand(target, normal, point));
    bool split = sector.Surfaces.Count == before + 1;
    bool halvesValid = target.Corners.Count >= 3 && sector.Surfaces[^1].Corners.Count >= 3;
    history.Undo();
    bool undone = sector.Surfaces.Count == before && target.Corners.Count == 4;
    history.Redo();
    Check("Cleave splits a real surface into two valid halves", split && halvesValid && undone,
        $"{before} → {sector.Surfaces.Count} surfaces in sector {sector.Num}");
}

// ---- Adjoin make/remove ----
{
    var a = candidates[3];
    var b = candidates[4];
    history.Do(new MakeAdjoinCommand(a, b));
    bool paired = ReferenceEquals(a.Adjoin, b) && ReferenceEquals(b.Adjoin, a);
    history.Do(new RemoveAdjoinCommand(a));
    bool cleared = a.Adjoin is null && b.Adjoin is null;
    history.Undo();
    bool restored = ReferenceEquals(a.Adjoin, b);
    Check("Adjoin pairs mirrors, remove clears both, undo restores", paired && cleared && restored);
}

// ---- Texture ops ----
{
    var target = candidates[5];
    var original = target.Corners.Select(c => c.Uv).ToList();

    history.Do(new ShiftTextureCommand(target, 8, 0));
    bool shifted = System.Math.Abs(target.Corners[0].Uv.U - (original[0].U + 8)) < 1e-6;

    history.Do(new RotateTextureCommand(target, 90));
    history.Do(new ScaleTextureCommand(target, 2, 2));
    history.Do(new AutoTextureCommand(target, 64, 64));
    bool changed = target.Corners.Zip(original).Any(p =>
        System.Math.Abs(p.First.Uv.U - p.Second.U) > 1e-6 ||
        System.Math.Abs(p.First.Uv.V - p.Second.V) > 1e-6);

    for (int i = 0; i < 4; i++) history.Undo();
    bool restored = target.Corners.Zip(original).All(p =>
        System.Math.Abs(p.First.Uv.U - p.Second.U) < 1e-6 &&
        System.Math.Abs(p.First.Uv.V - p.Second.V) < 1e-6);

    for (int i = 0; i < 4; i++) history.Redo();
    Check("Shift/Rotate/Scale/AutoFit change UVs and fully undo", shifted && changed && restored);
}

// ---- Save + reparse ----
Console.WriteLine();
int secEdited = level.Sectors.Count;
int surfEdited = level.Sectors.Sum(s => s.Surfaces.Count);
int adjEdited = level.Sectors.Sum(s => s.Surfaces.Count(f => f.Adjoin is not null));

var outPath = Path.Combine(Path.GetTempPath(), $"opsprobe_{args[1]}.jkl");
JklWriter.Save(doc, outPath);
var reloaded = JklParser.Parse(File.ReadAllText(outPath));

int secBack = reloaded.Sectors.Count;
int surfBack = reloaded.Sectors.Sum(s => s.Surfaces.Count);
int adjBack = reloaded.Sectors.Sum(s => s.Surfaces.Count(f => f.Adjoin is not null));

Console.WriteLine($"in memory after edits: {secEdited} sectors, {surfEdited} surfaces, {adjEdited} adjoins");
Console.WriteLine($"saved + reparsed:      {secBack} sectors, {surfBack} surfaces, {adjBack} adjoins");
Check("edited geometry survives the JKL round-trip",
    secBack == secEdited && surfBack == surfEdited && adjBack == adjEdited);

// Every section the source carried must still be there. Retail levels have no
// LIGHTS/LAYERS (those are editor-authoring sections), so assert against the
// source's own section list rather than a fixed one.
static HashSet<string> Sections(string jkl) =>
    System.Text.RegularExpressions.Regex
        .Matches(jkl, @"SECTION:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        .Select(m => m.Groups[1].Value.ToUpperInvariant())
        .ToHashSet();

var sourceSections = Sections(install.ReadLevel(entry));
var outputSections = Sections(File.ReadAllText(outPath));
var dropped = sourceSections.Except(outputSections).OrderBy(s => s).ToList();

Check($"all {sourceSections.Count} source sections survive the save", dropped.Count == 0,
    dropped.Count == 0 ? string.Join(", ", sourceSections.OrderBy(s => s)) : "dropped: " + string.Join(", ", dropped));

// ---- Multi-selection (SelectionSet) on real data ----
Console.WriteLine();
{
    var sel = new SelectionSet();
    var sector = level.Sectors[0];

    int events = 0;
    sel.Changed += () => events++;

    // Ctrl+click behaviour: toggle each surface of a room into the selection.
    using (sel.Defer())
        foreach (var s in sector.Surfaces) sel.Toggle(s);
    Check("bulk select raises one Changed event", events == 1, $"{events} event(s)");
    Check("every surface of the sector is selected", sel.Surfaces.Count == sector.Surfaces.Count);

    // Shared vertices must be counted once.
    var verts = sel.AffectedVertices();
    int corners = sector.Surfaces.Sum(s => s.Corners.Count);
    Check("shared vertices are deduplicated", verts.Count == verts.Distinct().Count() && verts.Count < corners,
        $"{corners} corners → {verts.Count} unique vertices");

    // Move the whole selection as one undo step and put it back.
    var before = verts.Select(v => v.Position).ToList();
    var nudge = new Sed.Core.Math.Vec3(0.05, 0, 0);
    history.Do(new TransformVerticesCommand(verts, TransformVerticesCommand.Translate(nudge), "Move selection"));
    bool moved = verts.Zip(before).All(p => System.Math.Abs(p.First.Position.X - (p.Second.X + 0.05)) < 1e-9);
    history.Undo();
    bool restored = verts.Zip(before).All(p => System.Math.Abs(p.First.Position.X - p.Second.X) < 1e-9);
    Check("multi-vertex move applies once per vertex and fully undoes", moved && restored);

    // Deleting the sector should prune it out of the selection.
    var doomed = level.Sectors[^1];
    sel.SelectOnly(doomed);
    level.Sectors.Remove(doomed);
    sel.Prune(level);
    Check("Prune drops objects removed from the level", sel.IsEmpty);
    level.Sectors.Add(doomed);
    level.RenumberSectors();
}

// ---- Copy / paste (clipboard) on real data ----
Console.WriteLine();
{
    var sel = new SelectionSet();
    var source = level.Sectors[0];
    sel.Add(source);

    var fragment = LevelFragment.Capture(sel, level);
    int sectorsBefore = level.Sectors.Count;

    var paste = new PasteFragmentCommand(level, fragment, new Sed.Core.Math.Vec3(100, 0, 0));
    history.Do(paste);

    var copy = paste.PastedSectors[0];
    Check("paste adds a sector", level.Sectors.Count == sectorsBefore + 1);
    Check("pasted geometry matches the source",
        copy.Vertices.Count == source.Vertices.Count && copy.Surfaces.Count == source.Surfaces.Count,
        $"{copy.Vertices.Count} verts, {copy.Surfaces.Count} surfaces");

    // No vertex may be shared with the original — JK pools world vertices, so
    // aliasing here would make edits to the copy corrupt the source room.
    var sourceVerts = new HashSet<Vertex>(source.Vertices);
    Check("pasted vertices are clones, not aliases", copy.Vertices.All(v => !sourceVerts.Contains(v)));

    // Adjoins that pointed outside the fragment must be cleared, or the pasted
    // room would open into the original through a portal 100 units away.
    int leaked = copy.Surfaces.Count(s => s.Adjoin is not null && !copy.Surfaces.Contains(s.Adjoin));
    Check("no adjoin leaks from the copy back to the original", leaked == 0, $"{leaked} leaked");

    history.Undo();
    Check("paste undoes cleanly", level.Sectors.Count == sectorsBefore);
    history.Redo();
    Check("paste redoes", level.Sectors.Count == sectorsBefore + 1);

    // A duplicated room is worthless if it does not persist — save again with the
    // paste in place and confirm the extra geometry comes back.
    var pastePath = Path.Combine(Path.GetTempPath(), $"opsprobe_paste_{args[1]}.jkl");
    JklWriter.Save(doc, pastePath);
    var afterPaste = JklParser.Parse(File.ReadAllText(pastePath));

    Check("pasted sector survives the JKL round-trip",
        afterPaste.Sectors.Count == level.Sectors.Count &&
        afterPaste.Sectors.Sum(s => s.Surfaces.Count) == level.Sectors.Sum(s => s.Surfaces.Count),
        $"{afterPaste.Sectors.Count} sectors, {afterPaste.Sectors.Sum(s => s.Surfaces.Count)} surfaces");
}

// ---- Lighting bake on real geometry ----
// Retail levels ship without an "Editor lights" section, so synthesise one light
// per sector centroid to exercise the calculator against real geometry.
Console.WriteLine();
{
    int lightCount = System.Math.Min(40, level.Sectors.Count);
    for (int i = 0; i < lightCount; i++)
    {
        var sector = level.Sectors[i];
        if (sector.Vertices.Count == 0) continue;
        var centre = TransformVerticesCommand.Centroid(sector.Vertices);
        var extent = sector.Vertices.Max(v => (v.Position - centre).Length);
        level.Lights.Add(new Light
        {
            Position = centre,
            Range = System.Math.Max(0.5, extent * 2),
            Intensity = 1.0,
            Color = Sed.Core.Math.ColorF.White,
        });
    }

    var scope = level.Sectors.Take(System.Math.Min(60, level.Sectors.Count)).ToList();

    var noShadow = System.Diagnostics.Stopwatch.StartNew();
    var quick = Sed.Core.Lighting.LightCalculator.Calculate(level, scope,
        new Sed.Core.Lighting.LightingOptions { CastShadows = false });
    noShadow.Stop();

    var shadowed = System.Diagnostics.Stopwatch.StartNew();
    var full = Sed.Core.Lighting.LightCalculator.Calculate(level, scope,
        new Sed.Core.Lighting.LightingOptions { CastShadows = true });
    shadowed.Stop();

    Console.WriteLine($"lighting: {scope.Count} sectors, {full.Vertices} vertices, {level.Lights.Count} lights");
    Console.WriteLine($"    no shadows: {noShadow.ElapsedMilliseconds,6} ms");
    Console.WriteLine($"    shadowed:   {shadowed.ElapsedMilliseconds,6} ms  ({full.Shadowed} rays blocked)");

    // The number that matters in practice: baking the entire level.
    var whole = System.Diagnostics.Stopwatch.StartNew();
    var fullLevel = Sed.Core.Lighting.LightCalculator.Calculate(level, null,
        new Sed.Core.Lighting.LightingOptions { CastShadows = true });
    whole.Stop();
    Console.WriteLine($"    full level: {whole.ElapsedMilliseconds,6} ms  " +
                      $"({fullLevel.Sectors} sectors, {fullLevel.Vertices} vertices)");

    bool anyLit = scope.Any(s => s.Surfaces.Any(f => f.Corners.Any(c => c.Intensity.R > 0)));
    Check("bake produces non-zero vertex lighting", anyLit);
    Check("shadow pass blocks at least some rays", full.Shadowed > 0, $"{full.Shadowed} blocked");
    Check("sector ambients were updated", scope.Any(s => s.Ambient.R > 0));

    // Undo must restore every intensity exactly.
    var probeScope = scope.Take(5).ToList();
    var snapshot = probeScope.SelectMany(s => s.Surfaces).SelectMany(f => f.Corners)
        .Select(c => c.Intensity).ToList();
    var ambientSnapshot = probeScope.Select(s => s.Ambient).ToList();

    var cmd = new CalculateLightingCommand(level, probeScope);
    history.Do(cmd);
    history.Undo();

    var restored = probeScope.SelectMany(s => s.Surfaces).SelectMany(f => f.Corners)
        .Select(c => c.Intensity).ToList();
    Check("undo restores every vertex intensity exactly", restored.SequenceEqual(snapshot));
    Check("undo restores sector ambients", probeScope.Select(s => s.Ambient).SequenceEqual(ambientSnapshot));

    // Lights as editable entities: create, copy/paste, delete, and persistence.
    int lightsBefore = level.Lights.Count;
    var newLight = new Light
    {
        Position = level.Lights[0].Position + new Sed.Core.Math.Vec3(0.25, 0, 0),
        Range = 3, Intensity = 0.75, Color = new Sed.Core.Math.ColorF(1, 0.5f, 0.25f),
    };
    history.Do(new CreateLightCommand(level, newLight));
    Check("create light adds and numbers it", level.Lights.Count == lightsBefore + 1 && newLight.Num == lightsBefore);

    var lightSel = new SelectionSet();
    lightSel.Add(newLight);
    var lightFragment = LevelFragment.Capture(lightSel, level);
    var lightPaste = new PasteFragmentCommand(level, lightFragment, new Sed.Core.Math.Vec3(0, 0.25, 0));
    history.Do(lightPaste);
    Check("lights copy/paste as independent clones",
        lightPaste.PastedLights.Count == 1 && !ReferenceEquals(lightPaste.PastedLights[0], newLight));

    // Persist: the level had no LIGHTS section, so writing must create one.
    var litPath = Path.Combine(Path.GetTempPath(), $"opsprobe_lights_{args[1]}.jkl");
    JklWriter.Save(doc, litPath);
    var litBack = JklParser.Parse(File.ReadAllText(litPath));
    Check("lights survive the JKL round-trip",
        litBack.Lights.Count == level.Lights.Count,
        $"{level.Lights.Count} written, {litBack.Lights.Count} read back");

    history.Undo();   // paste
    history.Undo();   // create
    Check("light create/paste undo cleanly", level.Lights.Count == lightsBefore);

    level.Lights.Clear();
}

// ---- Consistency checker (Tools ▸ Check Consistency) on real data ----
Console.WriteLine();
var issues = Sed.Core.Validation.ConsistencyChecker.Check(reloaded);
int errors = issues.Count(i => i.Severity == Sed.Core.Validation.IssueSeverity.Error);
Console.WriteLine($"consistency: {issues.Count} issue(s) — {errors} error, {issues.Count - errors} warning " +
                  $"over {surfBack} surfaces");
foreach (var group in issues.GroupBy(i => i.Message).OrderByDescending(g => g.Count()))
    Console.WriteLine($"    {group.Count(),6}  {group.Key}");

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? $"All checks passed. Wrote {outPath}"
    : $"{failures.Count} check(s) FAILED: {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 2;

