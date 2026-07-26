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

// ---- Find / query on real data ----
Console.WriteLine();
{
    // Pick a material that actually occurs, then find every surface using it.
    var sampleMaterial = level.Sectors.SelectMany(s => s.Surfaces)
        .Select(s => s.Material)
        .First(m => !string.IsNullOrEmpty(m));

    var byMaterial = Sed.Core.Query.LevelQuery.Run(level, new Sed.Core.Query.FindQuery
    {
        Kind = Sed.Core.Query.FindKind.Surface,
        Text = sampleMaterial,
    });
    int actual = level.Sectors.SelectMany(s => s.Surfaces)
        .Count(s => s.Material.Contains(sampleMaterial, StringComparison.OrdinalIgnoreCase));
    Check($"find surfaces by material '{sampleMaterial}'", byMaterial.Count == actual && actual > 0,
        $"{byMaterial.Count} of {actual}");

    // Sky surfaces via a flag mask — a real query a level author would run.
    var sky = Sed.Core.Query.LevelQuery.Run(level, new Sed.Core.Query.FindQuery
    {
        Kind = Sed.Core.Query.FindKind.Surface,
        FlagMask = SurfaceFlags.SkyCeiling | SurfaceFlags.SkyHorizon,
    });
    int actualSky = level.Sectors.SelectMany(s => s.Surfaces).Count(s => s.IsSky);
    Check("find sky surfaces by flag mask", sky.Count == actualSky, $"{sky.Count} sky surfaces");

    // Every result must carry a usable jump position and the model object.
    Check("results reference their model object and a position",
        byMaterial.All(r => r.Surface is not null && r.Sector is not null) &&
        byMaterial.All(r => double.IsFinite(r.Position.X)));

    // Things by template.
    var templates = level.Things.Where(t => !string.IsNullOrEmpty(t.Template))
        .GroupBy(t => t.Template).OrderByDescending(g => g.Count()).FirstOrDefault();
    if (templates is not null)
    {
        var byTemplate = Sed.Core.Query.LevelQuery.Run(level, new Sed.Core.Query.FindQuery
        {
            Kind = Sed.Core.Query.FindKind.Thing,
            Text = templates.Key,
        });
        Check($"find things by template '{templates.Key}'", byTemplate.Count >= templates.Count(),
            $"{byTemplate.Count} found, {templates.Count()} with that exact template");
    }
}

// ---- Header editing + GOB output ----
Console.WriteLine();
{
    var header = level.Header;
    double originalGravity = header.Gravity;
    float originalMip2 = header.MipmapDistances[2];

    history.Do(HeaderField.Set(header, "gravity", 9.25f, x => x.Gravity, (x, v) => x.Gravity = v));
    history.Do(HeaderField.Set(header, "mipmap 2", 77f,
        x => x.MipmapDistances[2], (x, v) => x.MipmapDistances[2] = v));

    var headerPath = Path.Combine(Path.GetTempPath(), $"opsprobe_header_{args[1]}.jkl");
    JklWriter.Save(doc, headerPath);
    var headerBack = JklParser.Parse(File.ReadAllText(headerPath));

    Check("header edits survive the JKL round-trip",
        System.Math.Abs(headerBack.Header.Gravity - 9.25f) < 1e-3 &&
        System.Math.Abs(headerBack.Header.MipmapDistances[2] - 77f) < 1e-3,
        $"gravity {headerBack.Header.Gravity:0.##}, mipmap[2] {headerBack.Header.MipmapDistances[2]:0.##}");

    history.Undo();
    history.Undo();
    Check("header edits undo",
        System.Math.Abs(header.Gravity - originalGravity) < 1e-6 &&
        System.Math.Abs(header.MipmapDistances[2] - originalMip2) < 1e-6);

    // File ▸ Save as GOB: one jkl\<name>.jkl entry, readable back as a level.
    var gobPath = Path.Combine(Path.GetTempPath(), $"opsprobe_{args[1]}.gob");
    var jklText = JklWriter.Build(doc);
    var jklBytes = System.Text.Encoding.ASCII.GetBytes(jklText);
    Sed.Formats.Gob.GobWriter.Build(gobPath, new[] { ($"jkl\\{args[1]}.jkl", jklBytes) });

    using (var archive = Sed.Formats.Gob.GobArchive.Open(gobPath))
    {
        var levelEntries = archive.Entries
            .Where(e => e.NormalizedName.EndsWith(".jkl", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Check("GOB contains exactly one level entry under jkl\\", levelEntries.Count == 1,
            levelEntries.Count == 1 ? levelEntries[0].NormalizedName : $"{levelEntries.Count} entries");

        var fromGob = JklParser.Parse(archive.ReadText(levelEntries[0]));
        Check("level parses back out of the written GOB",
            fromGob.Sectors.Count == level.Sectors.Count &&
            fromGob.Sectors.Sum(s => s.Surfaces.Count) == level.Sectors.Sum(s => s.Surfaces.Count),
            $"{fromGob.Sectors.Count} sectors, {fromGob.Sectors.Sum(s => s.Surfaces.Count)} surfaces");
    }
}

// ---- Layer visibility ----
// Retail levels ship with a single layer, so spread the sectors and things over
// three synthetic layers to exercise the filtering.
Console.WriteLine();
{
    for (int i = 0; i < level.Sectors.Count; i++) level.Sectors[i].Layer = i % 3;
    for (int i = 0; i < level.Things.Count; i++) level.Things[i].Layer = i % 3;

    int TriangleCount(LayerVisibility? layers)
    {
        var assembler = new Sed.Rendering.SceneAssembler();
        assembler.AddLevel(level, layers);
        return assembler.Build().Mesh.Indices.Count / 3;
    }

    var visibility = new LayerVisibility();
    int all = TriangleCount(visibility);

    visibility.SetVisible(1, false);
    int hidden = TriangleCount(visibility);

    Check("hiding a layer removes its geometry from the scene", hidden < all && hidden > 0,
        $"{all} → {hidden} triangles");

    visibility.ShowAll();
    Check("show all restores the full scene", TriangleCount(visibility) == all);

    // Picking must skip hidden geometry — you cannot select what you cannot see.
    var hiddenSector = level.Sectors.First(s => s.Layer == 1);
    var centre = TransformVerticesCommand.Centroid(hiddenSector.Vertices);
    var ray = new Sed.Rendering.Ray(centre + new Sed.Core.Math.Vec3(0, 0, 1000),
        new Sed.Core.Math.Vec3(0, 0, -1));

    visibility.SetVisible(1, false);
    var hitWhenHidden = Sed.Rendering.Picker.Pick(level, ray, visibility);
    Check("picking skips sectors on hidden layers",
        hitWhenHidden is null || hitWhenHidden.Sector.Layer != 1,
        hitWhenHidden is null ? "no hit" : $"hit sector on layer {hitWhenHidden.Sector.Layer}");

    // Things on hidden layers are likewise unpickable.
    var hiddenThing = level.Things.FirstOrDefault(t => t.Layer == 1);
    if (hiddenThing is not null)
    {
        var tRay = new Sed.Rendering.Ray(hiddenThing.Position + new Sed.Core.Math.Vec3(0, 0, 10),
            new Sed.Core.Math.Vec3(0, 0, -1));
        var thingHit = Sed.Rendering.Picker.PickThing(level, tRay, 1.0, visibility);
        Check("picking skips things on hidden layers",
            thingHit is null || thingHit.Thing.Layer != 1);
    }

    visibility.ShowAll();
    foreach (var s in level.Sectors) s.Layer = 0;
    foreach (var t in level.Things) t.Layer = 0;
}

// ---- Texture clamp flags split render batches ----
Console.WriteLine();
{
    int clamped = level.Sectors.SelectMany(s => s.Surfaces)
        .Count(s => (s.FaceFlags & (FaceFlags.TexClampX | FaceFlags.TexClampY)) != 0);
    Console.WriteLine($"clamp flags: {clamped} surface(s) carry FF_TexClampX/Y in this level");

    var assembler = new Sed.Rendering.SceneAssembler();
    assembler.AddLevel(level);
    var scene = assembler.Build();

    static int ClampOf(Surface s) =>
        ((s.FaceFlags & FaceFlags.TexClampX) != 0 ? 1 : 0) |
        ((s.FaceFlags & FaceFlags.TexClampY) != 0 ? 2 : 0);

    // For each material, the assembler must emit one submesh per distinct
    // (clamp, translucency, sky) combination its surfaces use.
    var expected = level.Sectors.SelectMany(s => s.Surfaces)
        .Where(s => s.Corners.Count >= 3 && !string.IsNullOrEmpty(s.Material))
        .GroupBy(s => s.Material, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            g => g.Key,
            g => g.Select(s => (ClampOf(s), s.IsTranslucent,
                    (s.SurfFlags & SurfaceFlags.SkyCeiling) != 0 ? 1 :
                    (s.SurfFlags & SurfaceFlags.SkyHorizon) != 0 ? 2 : 0))
                  .Distinct().Count(),
            StringComparer.OrdinalIgnoreCase);

    var actual = scene.Submeshes
        .GroupBy(x => x.Material, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    var mismatch = expected.FirstOrDefault(kv =>
        !actual.TryGetValue(kv.Key, out int n) || n != kv.Value);

    Check("each material emits one submesh per distinct clamp/blend/sky combination",
        mismatch.Key is null,
        mismatch.Key is null
            ? $"{actual.Count} materials, {scene.Submeshes.Count} submeshes"
            : $"'{mismatch.Key}' expected {mismatch.Value}, got {(actual.TryGetValue(mismatch.Key, out int got) ? got : 0)}");

    // Only meaningful on a level that actually uses the flags — 01narshadda has none.
    Check("clamped submeshes appear iff the level uses clamp flags",
        (clamped > 0) == scene.Submeshes.Any(x => x.ClampMode != 0),
        $"{clamped} clamped surface(s); submesh clamp modes: " +
        string.Join(",", scene.Submeshes.Select(x => x.ClampMode).Distinct().OrderBy(x => x)));
}

// ---- Template editing ----
Console.WriteLine();
{
    Console.WriteLine($"templates: {level.Templates.Count} declared, " +
                      $"{level.Templates.Values.Count(t => t.Values.Count == 0)} with no own params");

    // Pick a template that things actually instantiate, so the rename has work to do.
    var used = level.Templates.Values
        .Select(t => (tpl: t, users: DeleteTemplateCommand.CountUsers(level, t.Name)))
        .Where(x => x.users > 0)
        .OrderByDescending(x => x.users)
        .First();

    var target = used.tpl;
    var originalName = target.Name;

    history.Do(new SetTemplateValueCommand(target, "probe_param", "42"));
    Check("adding a template parameter", target.Values["probe_param"] == "42");

    history.Do(new RenameTemplateCommand(level, target, originalName + "_renamed"));
    bool repointed = level.Things.All(t => !string.Equals(t.Template, originalName, StringComparison.OrdinalIgnoreCase))
                     && level.Templates.Values.All(t => !string.Equals(t.Parent, originalName, StringComparison.OrdinalIgnoreCase));
    Check($"rename repoints all {used.users} reference(s)", repointed);

    // Persist and read back.
    var tplPath = Path.Combine(Path.GetTempPath(), $"opsprobe_tpl_{args[1]}.jkl");
    JklWriter.Save(doc, tplPath);
    var back = JklParser.Parse(File.ReadAllText(tplPath));

    Check("template count survives the round-trip", back.Templates.Count == level.Templates.Count,
        $"{level.Templates.Count} → {back.Templates.Count}");
    Check("renamed template and its new param persist",
        back.Templates.TryGetValue(originalName + "_renamed", out var renamedTpl) &&
        renamedTpl.Values.TryGetValue("probe_param", out var probeValue) && probeValue == "42");
    Check("no thing still references the old template name",
        back.Things.All(t => !string.Equals(t.Template, originalName, StringComparison.OrdinalIgnoreCase)));

    // Every template's parent must resolve (or be the "none" sentinel).
    int dangling = back.Templates.Values.Count(t =>
        !string.IsNullOrEmpty(t.Parent) &&
        !t.Parent.Equals("none", StringComparison.OrdinalIgnoreCase) &&
        !back.Templates.ContainsKey(t.Parent));
    Check("no template has a dangling parent after the round-trip", dangling == 0, $"{dangling} dangling");

    history.Undo();   // rename
    history.Undo();   // add param
    Check("template edits undo", level.Templates.ContainsKey(originalName) &&
                                 !target.Values.ContainsKey("probe_param"));
}

// ---- COG scripts + placed-cog editing ----
Console.WriteLine();
{
    var scripts = new Sed.Formats.Cogs.CogScriptLibrary(
        new[] { install.LevelArchive }, install.ResourceArchives);

    int resolved = 0, unresolved = 0, layoutOk = 0, layoutBad = 0;
    foreach (var cog in level.Cogs)
    {
        var script = scripts.Get(cog.Name);
        if (script is null) { unresolved++; continue; }
        resolved++;

        // The invariant the editor relies on: a placed COG's positional values
        // line up with the script's non-local, non-message symbols.
        if (script.LevelValues.Count == cog.Values.Count) layoutOk++;
        else layoutBad++;
    }

    Console.WriteLine($"cogs: {level.Cogs.Count} placed, {resolved} script(s) resolved, {unresolved} missing");
    Check("every placed COG's script resolves from the open archives", unresolved == 0,
        $"{unresolved} unresolved");
    Check("value count matches the script's level-supplied symbol count",
        layoutBad == 0, $"{layoutOk} ok, {layoutBad} mismatched");

    // Symbols must actually be named — a parse that produced nothing would still
    // "match" a zero-value cog.
    var withValues = level.Cogs.Where(c => c.Values.Count > 0).ToList();
    if (withValues.Count > 0)
    {
        var sample = withValues[0];
        var script = scripts.Get(sample.Name)!;
        Check($"symbols are named for '{sample.Name}'",
            script.LevelValues.Count > 0 && script.LevelValues.All(s => s.Name.Length > 0),
            string.Join(", ", script.LevelValues.Take(4).Select(s => $"{s.Name}({s.Type})")));

        // Edit a value, save, reload.
        var originalValues = sample.Values.ToList();
        history.Do(new SetCogValueCommand(sample, 0, "1234"));

        var cogPath = Path.Combine(Path.GetTempPath(), $"opsprobe_cog_{args[1]}.jkl");
        JklWriter.Save(doc, cogPath);
        var back = JklParser.Parse(File.ReadAllText(cogPath));

        int index = level.Cogs.IndexOf(sample);
        Check("COG value edits survive the JKL round-trip",
            back.Cogs.Count == level.Cogs.Count && back.Cogs[index].Values[0] == "1234",
            $"{back.Cogs.Count} cogs back");

        history.Undo();
        Check("COG value edit undoes", sample.Values.SequenceEqual(originalValues));
    }
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

