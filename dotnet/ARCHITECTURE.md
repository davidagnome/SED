# SED .NET 10 rewrite

A cross-platform (Windows / macOS-arm64 / Linux) rewrite of the SED level editor,
replacing the Delphi/VCL codebase. Renderer targets **Vulkan**, via **MoltenVK**
on Apple Silicon.

## Why a rewrite (not a port)

The original is ~114K LOC of Delphi **VCL** — Windows-only UI with no macOS
target — plus a DirectDraw/Direct3D7 renderer, COM automation, x86 inline asm,
and a native-DLL plugin host. None of that survives a language change to C#.
What carries over is *design*: the level data model, file formats, and geometry
math. See the repo-root analysis for the full option comparison (Lazarus/LCL vs
.NET).

## Project layout

| Project | Role | Status |
|---|---|---|
| `Sed.Core` | Math + domain model + **`Editing/`** (`EditHistory`; Move/Create/Delete Thing, Move/Delete/Insert Vertex, Move Surface) | ✅ unit-tested |
| `Sed.Formats` | **JKL** read + **faithful save** (`JklWriter`/`GeoResourceWriter` regenerate GEORESOURCE/SECTORS/THINGS incl. adjoin mirror-pairs), **GOB**, **MAT/CMP** (palette+light table), **3DO**, templates, **`Game/GameInstall`** | ✅ validated on retail data |
| `Sed.Core` | + `Mat4` (column-major, Vulkan-clip perspective/lookat) | ✅ 9 tests |
| `Sed.Rendering` | `Camera`, `Mesh`, **`SceneAssembler`** (level surfaces + instanced 3DO models → material batches), `SceneBuilder`, `Picker`, `PngWriter` | ✅ |
| `Sed.Rendering.Vulkan` | Silk.NET backend; **indexed-texture `SceneRenderer`** — CMP palette + 64-level light ramp shading in-shader, opaque/translucent/flat passes, depth, MVP, selection/marker overlays | ✅ verified on M4 Pro |
| `Sed.App` | Avalonia shell + `VulkanView`: fly camera; click-pick **things / vertices / surfaces**; arrow-keys move selection (thing/vertex/whole surface) with **live mesh rebuild** + Edit ▸ Undo/Redo; mode toolbar + per-mode `InspectorPanel`; **Geometry** / **Texturing** / **Tools** menus (extrude, flip, cleave, adjoin, UV transforms, consistency check); Game menu + File ▸ Open | ✅ |
| `tools/Sed.GobTool`, `Sed.JklProbe`, `Sed.MatTool`, `Sed.LevelRender` | GOB list / JKL render / MAT→PNG / **textured level → PNG** | ✅ |
| `tools/Sed.VulkanSmoke`, `Sed.TriangleProbe`, `Sed.SceneProbe`, `Sed.AppShot` | bring-up + capture probes | ✅ |
| `tools/Sed.SaveProbe`, `Sed.OpsProbe` | save round-trip / **editing-ops round-trip on retail levels** | ✅ |
| `tools/Sed.UiProbe` | **headless pointer-input probe** (box-select, Ctrl+click, pan routing) | ✅ |
| `tests/Sed.Core.Tests` | xUnit | ✅ 100 passing |

### Rendering pipeline (verified)

`VulkanContext` (instance, portability enumeration) → `VulkanDevice` (physical
device pick, graphics queue, `VK_KHR_portability_subset`, command pool) → renderer:

- `OffscreenRenderer` — bring-up triangle (no vertex buffer).
- `SceneRenderer` — indexed mesh with depth buffer, perspective MVP via push
  constant, dynamic viewport/scissor (resize without pipeline rebuild),
  two-sided flat shading. Targets are recreated on size change.

`SceneBuilder.FromLevel` triangulates `Sed.Core.Model` surfaces (fan per surface,
surface normal for shading) into a `Mesh`. The editor's `VulkanView` owns the
device + `SceneRenderer`, renders to pixels at the control's pixel size, and
blits via `VulkanViewport.ToBitmap` into an Avalonia `WriteableBitmap`. This
offscreen→bitmap path is the same mechanism a future swapchain-backed viewport
would replace if blit latency ever matters.

Shaders live in `Shaders/*.{vert,frag}`, compiled to `.spv` by an MSBuild target
(`glslangValidator` when present) and embedded as resources; the checked-in
`.spv` are used when no compiler is available.

The domain model maps 1:1 to the original `J_LEVEL.PAS` classes so the format
readers can be translated faithfully.

## macOS / MoltenVK runtime notes

There is no system Vulkan on macOS. We depend on Homebrew:

```
brew install vulkan-loader molten-vk vulkan-headers
```

Neither the loader nor the ICD is symlinked onto a default search path, so
`NativeVulkanLocator` resolves them at runtime (Homebrew Cellar glob + fixed
fallbacks) and:
- loads `libvulkan.dylib` **by absolute path** (macOS `dlopen` ignores a
  `DYLD_LIBRARY_PATH` mutated after launch), and
- sets `VK_ICD_FILENAMES` to the MoltenVK ICD before `vkCreateInstance`.

The instance is created with `VK_KHR_portability_enumeration` +
`InstanceCreateFlags.EnumeratePortabilityBitKhr`, required for the MoltenVK
"portability" driver to be reported.

Verified: `vkCreateInstance` OK → `Apple M4 Pro [IntegratedGpu] Vulkan 1.2.334`.

## Completed milestones (chronological)

For the work that *remains* to reach feature parity with the original editor, see
**`PLAN.md`** (detailed task breakdown) and the **Roadmap to parity** below.

1. ~~Device + logical device + queue~~ ✅
2. ~~Avalonia shell hosting Vulkan output~~ ✅
3. ~~First triangle~~ ✅
4. ~~Live viewport: resize + depth + MVP; mouse orbit/zoom~~ ✅
5. ~~Level-geometry pipeline (model→mesh)~~ ✅ (procedural cube; real lighting TBD)
6. ~~JKL parser → load a real level and render it~~ ✅ **Validated against retail
   `JK1.GOB`**: all 25 levels parse (20–966 sectors, up to 27k tris). The editor's
   `File ▸ Open` reads a loose `.jkl` or a `.gob` archive and lists its levels in
   the side panel (`tools/Sed.GobTool` does the same from the CLI).
   (Adjoins/COGs/lights/layers came later — see milestone 24.)

### GOB archive format (`Gob/GobArchive`)

Little-endian: magic `"GOB "`, uint32 version (0x14), uint32 directory offset (12);
at that offset a uint32 entry count then `count` × { uint32 dataOffset, uint32
length, char[128] name }. Names use `\` separators (e.g. `jkl\01narshadda.jkl`).
7. **Materials/textures** — two steps:
   - 7a. ~~Decode MAT (8-bit indexed texture) + CMP (256-color palette)~~ ✅
     `Material/MatFile` + `Material/Colormap`; `tools/Sed.MatTool` dumps a MAT→PNG.
     Validated against retail textures (cabinet, Nar Shaddaa wall panel, flat color).
   - 7b. ~~Wire textures into the renderer~~ ✅ Per-material `Submesh` batches,
     `VkImage`+sampler+descriptor set per material (`MaterialLibrary` loads MATs
     from a resource GOB + the level's CMP), sampled by `Surface.Corner.Uv` and
     modulated by per-vertex intensity. **Validated**: `01narshadda`, `03katarn`,
     `09fuelstation` (966 sectors), `14tower` render textured (`Sed.LevelRender`).
     Preview lighting biases intensity toward bright (JK bakes most light into
     the colormap tables, so raw vertex intensities are near-zero — `SceneBuilder.Light`).
8. ~~Fly nav + surface picking~~ ✅
9. ~~Thing markers + picking + move-with-undo/redo~~ ✅
10. ~~3DO model rendering~~ ✅ `ThreeDo/` parser + `ModelLibrary`; thing→template
    →`model3d` resolution (`Level.GetThingModel`); `SceneAssembler.AddThings`
    instances models at thing pos/orientation; model-less things keep markers.
    Verified: standalone table model + Katarn chair in-room.
11. ~~Geometry editing~~ ✅
12. ~~Saving back to JKL~~ ✅ **patch writer** (`JklDocument` records source lines +
    vertex/thing line numbers during `ParseDocument`; `JklWriter` rewrites only
    moved vertex/thing lines, preserving COGs/lights/everything else verbatim →
    game-loadable). File ▸ Save As in the editor. Only *moved* vertices are
    rewritten (JK shares world vertices across sectors). Verified round-trip on
    retail `03katarn` (vertex + thing edits persist, all sections intact).
13. ~~Sky, transparency, real colormap lighting~~ ✅ **indexed rendering**: MAT
    textures uploaded as R8 palette indices; CMP palette (256×1 RGBA) + light ramp
    (256×64 R8) uploaded once; fragment shader shades `index` through
    `lightRamp[level][index]` (level = vertex intensity×63) then resolves via
    palette — matches engine lighting (lit lamps glow, walls dark). Sky surfaces
    (`SF_SkyHorizon/Ceiling`) drawn full-bright; translucent surfaces
    (`FF_Transluent`) alpha-blended in a 2nd pass with index-0 cutout.
14. ~~Editor brightness toggle~~ ✅ shader `brightness` push constant (lerps light
    toward full bright); `View ▸ Cycle Brightness` / **B** key (real→medium→full).
15. ~~Thing / vertex create-delete~~ ✅ Insert = create thing (or split selected
    surface's edge); Delete = remove selected thing/vertex; undoable. The THINGS
    section is now **regenerated** on save so add/delete persists (verified on
    retail levels). Vertex topology changes render but don't yet save to JKL.
16. ~~Faithful topology save~~ ✅ `GeoResourceWriter` regenerates GEORESOURCE
    (pooled/deduped vertices + texvertices, adjoin mirror-pairs, full surface
    records) and SECTORS from the model; `JklWriter` splices GEORESOURCE/SECTORS/
    THINGS, keeps the rest verbatim. Verified on retail levels (07yun: 50 adjoins,
    katarn: 1660) — all geometry counts + edits round-trip.
17. ~~Sector create/delete~~ ✅ `SectorFactory.CreateBox` + Create/DeleteSectorCommand;
    **N** = new box room, Edit ▸ Delete Sector. Box persists through save.
18. ~~View-projected scrolling sky~~ ✅ ceiling sky = ray-to-plane → world-XY UV;
    horizon sky = view-direction cylindrical (scrolls with rotation); computed in
    the fragment shader (camPos + sky params in push constant), full-bright.
19. ~~Rotate/scale edits~~ ✅ `RotateThingCommand`, `TransformVerticesCommand`
    (rotate/scale about a pivot). `[`/`]` rotate (thing yaw or active sector),
    `,`/`.` scale the active sector.
20. ~~Material editing~~ ✅ `SetMaterialCommand`; **M** cycles the selected
    surface's material through the level's material list. Persists on save.
21. ~~Exact horizon screen-projection~~ ✅ fragment shader screen-space horizon
    (gl_FragCoord + camera yaw/pitch/roll → 1 texture per 360°); ceiling sky via
    ray-to-plane. Push constant grew to 152 B (MoltenVK max 4096).
22. ~~Lighting tools~~ ✅ `SetSectorAmbientCommand`, `SetVertexLightCommand`;
    `;`/`'` darken/brighten the active sector's ambient (or the selected vertex's
    light). Sector ambient is a light floor in `SceneAssembler` (edits are visible)
    and persists on save.
23. ~~Material inspector panel~~ ✅ right-side panel lists the level's materials
     with decoded thumbnails (`MaterialLibrary.Get` → downsampled bitmap); click a
     material to assign it to the selected surface (`VulkanView.SetSelectedSurfaceMaterial`).
24. ~~Full JKL section read/write parity~~ ✅ **LIGHTS** (`Editor lights` — JK mono
     + MotS/IJIM RGB), **COGS** (script + ordered values), **TEMPLATES** (faithful
     regeneration), **HEADER** (gravity, sky params, mipmap/LOD, perspective/gouraud,
     fog), and **LAYERS** (sector/thing assignments + auto-creation) now all
     **parse + write** through `JklParser`/`JklWriter`. Every section round-trips.
25. ~~GOB writer~~ ✅ `GobWriter.Build` writes GOB v2 archives (data → directory);
     verified read-back via `GobArchive`. Enables "Save JKL+GOB".
26. ~~Geometry operations~~ ✅ `ExtrudeSurfaceCommand` (new sector + side surfaces +
     adjoin), `CleaveSurfaceCommand` (plane-split surface), `FlipSurfaceCommand`
     (reverse winding + normal), `MakeAdjoinCommand`/`RemoveAdjoinCommand` (mirror
     pairs). All as reversible `IEditCommand`s with unit tests. `GeometryOps` provides
     plane classification + segment intersection.
27. ~~Texture mapping tools~~ ✅ `ShiftTextureCommand`, `ScaleTextureCommand`,
     `RotateTextureCommand`, `AutoTextureCommand` — per-surface UV transforms;
     reversible; persist via GEORESOURCE regeneration.
28. ~~2D map editing~~ ✅ `MapView` now **hit-tests** (vertices, things, surfaces),
     **drag-to-move** (reuses `MoveVertexCommand`/`MoveThingCommand`), **snap-to-grid**
     (**G** toggle), and **syncs selection** with the 3D `VulkanView` bidirectionally.
29. ~~Consistency checker~~ ✅ `Sed.Core.Validation.ConsistencyChecker` validates
     surface/vertex counts, normals, planarity, adjoin mirror pairs, max vertex
     count per game kind, and thing-in-sector.
30. ~~Mode system + contextual inspectors~~ ✅ `EditMode` enum (Sector/Surface/
     Vertex/Edge/Thing/Light) drives mode-selective picking in both 2D and 3D views,
     a mode toolbar (toggle buttons + S/F/V/T/L shortcuts), and an `InspectorPanel`
     that rebuilds per-mode. Per-entity inspectors: Sector (flags, ambient, extra
     light, tint, colormap, sound, layer), Surface (surf/face flags, material,
     geo/light/tex modes, extra light, U/V scale, adjoin flags), Vertex (X/Y/Z),
     Thing (name, template, sector, position, orientation, layer, template values),
     Light (flags, range, intensity, color, position, layer). All field edits flow
     through `IEditCommand`s.
31. ~~Geometry/texturing/tools menus~~ ✅ The geometry and texture commands built in
     milestones 26–27 were implemented and unit-tested but had **no UI surface** —
     nothing in `Sed.App` referenced them. They are now reachable: a **Geometry**
     menu (extrude in/out, flip, cleave, make/remove adjoin), a **Texturing** menu
     (shift/scale/rotate/auto-fit), and **Tools ▸ Check Consistency** (`ConsistencyWindow`,
     click a row to jump to the offending sector/surface). Because Avalonia's
     `MenuItem.InputGesture` is display-only, each item also registers a real
     window-level `KeyBinding`; `VulkanView` handles the same chords first when it
     has focus and marks them handled, so they fire exactly once.
     Verified end-to-end by `tools/Sed.OpsProbe` on retail `03katarn`, `07yun`,
     `09fuelstation` and `14tower`: every op applies, undoes and redoes, and the
     edited level saves and reparses with matching sector/surface/adjoin counts and
     all 17 source sections intact.
32. ~~Robust surface normals~~ ✅ `Surface.RecalcNormal` took the cross product of
     corners 0/1/2 only, which collapses to a **zero vector** whenever those three
     are colinear — common in retail geometry. That silently corrupted shading, the
     extrude direction, `AutoTextureCommand`'s projection axis and `MidCleavePlane`'s
     basis, and made the consistency checker report false "invalid normal" warnings
     (161 of 5,219 surfaces on `03katarn`). Replaced with **Newell's method** over
     every edge — same winding convention, immune to colinear leading corners, and
     it averages numerical noise so near-planar surfaces stop being flagged.
     Retail levels now report **0 issues** across 15,676 surfaces; genuinely
     zero-area surfaces still yield a zero normal and are still flagged.
33. ~~Multi-selection~~ ✅ `SelectionSet` (`Sed.Core/Editing`) — insertion-ordered,
     reference-identity sets of vertices/things/surfaces/sectors with add/remove/
     toggle/clear, a `Changed` event, a `Defer()` scope that coalesces bulk updates
     into one notification, and `Prune(level)` to drop objects an undo or delete
     removed. The **most recently added** item of each kind is the *primary*, which
     is what the inspector and the surface-mode operations edit.
     One instance is owned by `VulkanView` and handed to `MapView` (exactly as
     `EditHistory` already was), so the 3D and 2D panes share one selection.
     **Ctrl/Cmd+click** toggles, plain click replaces, **Esc** clears, **Ctrl+A**
     selects the active sector's surfaces. Move (arrows and 2D drag), rotate,
     scale, delete and set-material now apply to the entire selection as a single
     undo step via `CompositeCommand`; `AffectedVertices()` unions the vertices
     implied by selected surfaces and sectors, deduplicated so a vertex shared by
     two selected faces moves once rather than twice.
34. ~~Box-select~~ ✅ `MapView` rubber-bands on a plain drag over empty space
     (Ctrl+drag extends); panning moved to **middle-drag / Shift+drag / Alt+drag**,
     matching the original's 2D panes. Points (vertices, things) count when they
     fall inside the rectangle; surfaces and sectors must be **fully** enclosed, so
     sweeping a band across a level does not drag in half-visible geometry. The
     bulk add runs inside `Selection.Defer()`, so selecting hundreds of objects
     raises one `Changed` event.
35. ~~Copy / paste~~ ✅ `LevelFragment` (clipboard) + `PasteFragmentCommand`.
     **Ctrl+C** snapshots the selection — selected sectors whole, selected surfaces
     contributing their owning sector, plus things with their template values.
     **Ctrl+V** pastes offset by the fragment's own width so a duplicated room
     lands *beside* its source, then selects the result for immediate dragging;
     **Ctrl+D** duplicates in one step. Two correctness properties the tests pin
     down: JK pools world vertices, so cloning always creates **fresh `Vertex`
     objects** rather than aliasing the source (otherwise editing the copy would
     corrupt the original), and adjoins are **remapped when both sides are inside
     the fragment and cleared otherwise**, so a pasted room is sealed instead of
     opening through a portal into the room it came from. Capture is a snapshot,
     so later edits to the source do not leak into the clipboard, and each paste
     re-clones so repeated pastes stay independent.
36. ~~Headless UI probe~~ ✅ `tools/Sed.UiProbe` drives `MapView`'s real pointer
     path with simulated mouse input (`Avalonia.Headless`), covering box-select,
     Ctrl+drag extension, click-to-clear, full-containment semantics and
     pan-vs-select modifier routing — interaction logic that lives in a view and
     cannot be reached from `Sed.Core` unit tests.
37. ~~Lighting calculation~~ ✅ `Sed.Core.Lighting.LightCalculator` ports
     `CalcLighting`/`CalcSectorAmbients` from `LEV_UTILS.PAS`. Per light and
     surface: the light must be in front of the plane; per corner it must be
     within range; the contribution is **`intensity · ((range − dist) / range)²`**
     — quadratic over the *remaining* range, not inverse-square. JK accumulates
     greyscale, MotS/IJIM accumulate RGB. A shadow ray from light to vertex is
     blocked by any surface it crosses, except adjoins without
     `SAF_BlockLight` — light passes through portals by default — and
     `LF_NoBlock` lights ignore occluders entirely. Afterwards each sector's
     ambient becomes the brighter of its mean vertex light and mean surface
     extra-light, skipping `SECF_NOAMBIENTLIGHT` sectors.
     `CalculateLightingCommand` makes a whole bake **one undo step**, restoring
     every corner intensity and sector ambient exactly, so a bake can be tried
     and rejected without losing hand-authored lighting. Results land in
     `Surface.Corner.Intensity` and so persist through GEORESOURCE regeneration
     with no writer change. **Tools ▸ Calculate Lighting** (**F9**; Shift+F9
     skips shadows) scopes to the selection when there is one, else the level.
     Shadow queries go through a **uniform spatial grid** over surface bounds —
     without it each ray scanned all ~10k surfaces. Same results, 7× faster:
     a full bake of `09fuelstation` (968 sectors, 47,884 vertices, 40 lights)
     takes **486 ms**.
38. ~~Box sector winding fix~~ ✅ `SectorFactory.CreateBox` wound three of its six
     faces backwards, so new box rooms were half-inverted. Retail data settles the
     convention beyond doubt: **all 20,250 sector surfaces** across `07yun`,
     `01narshadda`, `03katarn` and `09fuelstation` face **inward**, with zero mixed
     sectors. The flipped faces meant the lighting pass skipped surfaces it thought
     the light was behind, extrude pushed the wrong way, and auto-texture chose its
     projection axis from an inverted normal. All six faces now point inward, pinned
     by a test.
39. ~~Lights as selectable entities~~ ✅ `SelectionSet` gained a `Light` bucket, so
     lights behave like every other object: `Picker.PickLight` hit-tests them in the
     3D view and `MapView` draws them as diamonds and box-selects them, both only in
     **Light** mode (a level can carry hundreds, and they would bury the geometry
     otherwise). `CreateLightCommand` (**Insert**, cloning the selected light's
     settings), `DeleteLightCommand` (which restores a light to its **original list
     index** on undo, because COGs reference lights by number) and `MoveLightCommand`
     (a delta, so several lights move as one `CompositeCommand`). `LevelFragment`
     now copies lights, and the Light inspector is finally reachable — it existed
     but `UpdateInspectorTarget` had nothing to give it.
40. ~~Writer: append absent sections~~ ✅ **Bug fix.** `JklWriter.AddSection` only
     rewrote sections it could already find in the source, so anything the source
     lacked was silently dropped. Every retail level ships *without* LIGHTS and
     LAYERS — those are editor-authored — which meant lights placed in the editor
     **vanished on save**, exactly the workflow milestones 37–39 exist to support.
     Missing sections with content are now appended at the end of the file;
     sections with no content are still skipped, so opening and saving an untouched
     level does not sprout empty ones.
41. ~~Find / jump-to~~ ✅ `Sed.Core.Query.LevelQuery` matches sectors, surfaces,
     things and lights by index, identifying strings (material, name, template,
     colormap, sound) and an optional flag mask; `FindWindow` (**Ctrl+Shift+F**)
     lists the hits, selects and frames the camera on the one you click
     (`VulkanView.JumpTo` keeps the current view direction so the jump does not
     disorient), and "Select all matches" pushes every hit into the shared
     selection — so "select every sky surface" or "every crate" becomes a
     single multi-edit. Verified on retail data: 80 surfaces of one material and
     103 sky surfaces on `03katarn`, 162 and 142 on `09fuelstation`, each matching
     an independent count.
42. ~~Level header editor~~ ✅ `HeaderEditorWindow` (Tools ▸ Level Header…) exposes
     the ~20 header fields — gravity, ceiling/horizon sky, the mipmap and LOD
     distance arrays, perspective/gouraud cutoffs, and fog. Rather than twenty
     near-identical command classes, one generic `SetHeaderFieldCommand<T>`
     parameterised by a getter/setter pair covers every field, including array
     elements and the immutable `Vec2` offsets (replaced whole, since a component
     cannot be assigned). The window rebuilds from the model after each commit, so
     an undo — from anywhere — is reflected rather than leaving stale text.
43. ~~Save as GOB~~ ✅ **File ▸ Save as GOB…** writes the edited level into a GOB v2
     archive as a single `jkl\<name>.jkl` entry, which is where the engine looks
     for levels regardless of the archive's own filename. Verified by writing a
     retail level out and parsing it back through `GobArchive` (608 sectors,
     5,226 surfaces on `03katarn`).

---

## Roadmap to parity with the original SED (`src/*.PAS`)

The milestones above cover **rendering, formats, save, and core editing**. What
remains to match the Delphi/VCL editor's *functionality* is listed below, grouped
by area and ordered by leverage. Each item names the original unit(s) to mirror
and the architectural adaptation (VCL→Avalonia, DirectX→Vulkan, COM/DLL→managed).
A feature is "parity-done" when it round-trips to a game-loadable JKL and is
reachable from the editor UI.

Status legend: ⬜ not started · 🟡 partial · ✅ done.

### P1 — 2D orthographic map views (the original's primary editor surface) ✅
The Delphi editor edits in **2D top/side/front map panes with a grid** (`JED_MAIN`,
`Render.pas`/`RenderSW`), using 3D only as a preview.
- ✅ `MapView` (Avalonia `DrawingContext`): renders surface edges (StreamGeometry),
  things, and a grid; Top/Front/Side axes (**V** cycles); pan (drag) + zoom-to-cursor
  (wheel); auto-frames the level. Laid out under the 3D viewport via a `GridSplitter`.
- ✅ Grid **snap-to-grid** (**G** toggle; snap-to-vertex is future work).
- ✅ Selection + drag editing in 2D (vertices/things/surfaces) reusing the existing
  `IEditCommand`s; selection sync with the 3D view (bidirectional).
- ⬜ Box-select; measurement/coords readout; vertex dots for all sectors (currently
  only the active sector).

### P2 — Selection model: multi-select + copy/paste ✅
Mirror `u_multisel.pas`, `u_copypaste.pas`.
- ✅ **Multi-selection set** (`SelectionSet`) holding things/surfaces/sectors/
  vertices, shared by both views. **Ctrl/Cmd+click** adds and removes; plain click
  replaces; **Esc** or Edit ▸ Select None clears; **Ctrl+A** grabs the active
  sector's surfaces. Move/rotate/scale/delete/set-material all act on the whole
  selection as **one undo step** (`CompositeCommand`).
- ✅ **Box-select** in the map view — a plain drag on empty space rubber-bands;
  Ctrl+drag adds to the selection; pan moved to middle-drag / Shift+drag / Alt+drag.
  Points must fall inside; surfaces and sectors must be *fully* enclosed.
- ✅ Transform commands accept vertex sets; `SelectionSet.AffectedVertices()`
  feeds them, deduplicating vertices shared between selected surfaces.
- ✅ **Copy/paste** (`LevelFragment` + `PasteFragmentCommand`) — **Ctrl+C** /
  **Ctrl+V** / **Ctrl+D**. Deep-clones sectors (vertices, surfaces, UVs, flags) and
  things (including template values); pastes offset by the fragment's own width so
  a duplicated room lands beside its source; selects what it pasted; undoable.
- ✅ Lights are copied too (milestone 39).

### P3 — Core geometry operations ✅
Mirror `SAVEJKL.INC`-adjacent ops in `JED_MAIN`/`TBAR_TOOLS`:
- ✅ **Adjoins**: `MakeAdjoinCommand`/`RemoveAdjoinCommand` set and clear the
  mirror pair. Editor: Geometry ▸ Make Adjoin is a two-step pick (**Ctrl+J** on
  the first surface, then on the facing one; **Esc** cancels), Remove is
  **Ctrl+Shift+J**.
- ✅ **Extrude surface** — `ExtrudeSurfaceCommand` pulls a surface along its
  normal, generating the new sector, side surfaces and the adjoin.
  **Ctrl+E** (outward) / **Ctrl+Shift+E** (inward).
- ✅ **Cleave / split** — `CleaveSurfaceCommand` splits a surface by a plane.
  **Ctrl+K** cleaves down the middle: `GeometryOps.MidCleavePlane` puts the plane
  through the centroid, normal along the surface's longest in-plane axis.
  Cleaving a sector *by another sector's* plane is still to do.
- ✅ **Flip surface** — `FlipSurfaceCommand` (**Ctrl+F**); insert vertex via
  `InsertSurfaceVertexCommand` (**Insert**).
- ⬜ Bridge/connect sectors (composite of cleave + adjoin).
- All are `IEditCommand`s, so undo and faithful save come for free.

### P4 — Texture mapping tools ✅
Mirror the `&Texturing` menu (`ShiftTexture`/`ScaleTexture`/`RotateTexture`,
auto/fit/align-from-adjoin).
- ✅ Per-surface UV transforms (offset/scale/rotate) editing `Surface.Corner.Uv`;
  auto-texture (project). `ShiftTextureCommand`, `ScaleTextureCommand`,
  `RotateTextureCommand`, `AutoTextureCommand` — all reversible.
- ✅ Editor UI: a **Texturing** menu, with **Ctrl+arrows** to shift by ⅛ of the
  material, **Ctrl+±** to scale, **Ctrl+R** / **Ctrl+Shift+R** to rotate 15°, and
  **Ctrl+T** to auto-fit. Shift and auto-fit read the real material dimensions.
- ⬜ Surface flags affecting tex (`SF_DoubleRes`/`HalfRes`, `FF_TexClampX/Y`).
- ⬜ Align-from-adjoin (stitch).
- ✅ Commands persist via the existing GEORESOURCE regeneration.

### P5 — Lighting calculation ✅
Mirror `Calculate &Lighting` (`lev_utils.pas`).
- ✅ Static light propagation from point lights to per-vertex intensities
  (`Sed.Core.Lighting.LightCalculator` → `Surface.Corner.Intensity`), with the
  engine's falloff, the sector-ambient pass, and shadow ray-casting through
  geometry. **Tools ▸ Calculate Lighting** (**F9**, Shift+F9 for no shadows).
- ✅ LIGHTS parse + faithful write (milestone 24); light entities are editable
  through the Light inspector.
- ✅ Per-vertex/sector/surface light editing (`SetVertexLight`/`SetSectorAmbient`).
- ✅ **Lights are selectable entities** — pickable and box-selectable in Light
  mode in both views, with create (**Insert**), delete, move (arrows / 2D drag),
  copy-paste, and the Light inspector bound to the primary selection.

### P6 — Things, templates, COGs (gameplay data) 🟡
Mirror `Item_edit`, `U_TEMPLATES`/`U_TPLCREATE`, `U_COGFORM`/`U_COGGEN`, `U_CSCENE`.
- ✅ COGS and TEMPLATES both parse and write faithfully (milestone 24).
- ✅ **Item editor**: the Thing inspector edits template, name, sector, position,
  orientation, layer and template values.
- ⬜ **Template editor/creator**: no UI for viewing/adding templates.
- ⬜ **COGs**: no placed-cog symbol-value editor, COG generator, or cutscene helper.
- Thing create/delete/move/rotate exist ✅.

### P7 — Find / navigate / inspect ✅
Mirror `Q_Sectors`/`Q_surfs`/`Q_things`, `Jump to Object`.
- ✅ Find dialog (`FindWindow`, **Ctrl+Shift+F**) for sectors/surfaces/things/lights
  by index, material, name, template, colormap/sound or flag mask; clicking a
  result selects it and frames the camera on it; "Select all matches" pushes every
  hit into the shared selection. Matching lives in `Sed.Core.Query.LevelQuery`.
- ⬜ The original's full per-field query builder (a comparison operator per field:
  material, adjoin sector/surface, each flag word separately) — the current dialog
  covers free text plus one flag mask.
- ✅ **Sector/surface/thing/vertex/light property inspector** panel with typed
  editors → `IEditCommand`s (milestone 30). Material panel done ✅.
- ✅ Consistency checker (`CONS_CHECKER`) — `ConsistencyChecker` validates
  normals, planarity, adjoin mirrors, vertex counts and thing-in-sector, surfaced
  by **Tools ▸ Check Consistency** (**F8**); selecting a row jumps the views to it.

### P8 — Header / layers / level admin 🟡
Mirror `U_LHEADER`, layers, `U_MEDIT`.
- ✅ HEADER and LAYERS both parse and write faithfully (milestone 24); per-object
  layer assignment is editable from the inspectors.
- ✅ **Level header editor** (`HeaderEditorWindow`, Tools ▸ Level Header…) —
  gravity, both sky descriptions, mipmap/LOD distance arrays, perspective/gouraud
  cutoffs and fog, every field an undoable `IEditCommand`.
- ⬜ Layer visibility toggles; episode editor.

### P9 — File / GOB project / test-launch 🟡
Mirror `Gob Project`, `Save JKL and Test`, `FILEOPERATIONS`.
- ✅ **GOB writer** — `GobWriter.Build` writes GOB v2 archives (milestone 25).
- ✅ **File ▸ Save as GOB…** writes the edited level as a single
  `jkl\<name>.jkl` entry — the layout the engine looks for.
- ⬜ "Save and test" — launch the game with the level (on macOS: via the user's
  Wine/CrossOver or a configured command).
- ⬜ Import/export: DF import (`U_DFI`/`DF_IMPORT.INC`), `.3do`/shape export of a
  sector (`Export Sector as 3DO`), ASC/LEV import.

### P10 — 3DO model tooling ⬜
Mirror `U_3DOS`/`U_3DOFORM`/`U_3doprev` (we render 3DO ✅, don't edit).
- 3DO hierarchy viewer/editor; standalone 3DO preview window; export sector→3DO.

### P11 — Editor UX parity ⬜
Mirror `U_OPTIONS`, recent files, recovery.
- Configurable keybindings, grid/units, recent-files, autosave/backup &
  crash-recovery, multi-game project switching (game-install config done ✅).

### P12 — Extensibility (architectural redesign) ⬜
The original plugin host is **Windows COM + native DLLs** (`SED_COM`,
`sed_plugins`) — not portable. Parity = a **managed plugin model**: define a
`Sed.Plugins` contract and load plugin assemblies via `AssemblyLoadContext`
(cross-platform). Treat as opt-in, last.

### Cross-cutting notes
- **Renderer**: 2D map views use Avalonia-native `DrawingContext` (vector); the 3D
  path stays Vulkan/MoltenVK.
- **Save**: every edit flows through `IEditCommand` and is covered by the section
  regeneration. ✅ All JKL sections (GEORESOURCE/SECTORS/THINGS/LIGHTS/COGS/
  TEMPLATES/HEADER/LAYERS) now read + write. GOB v2 writing is implemented
  (`GobWriter`).
- **Verification**: keep the probe-per-feature + round-trip-count pattern; a level
  is "parity-correct" when it loads in retail Jedi Knight (user-verified).

### MAT / CMP formats (from `src/graph_files.pas`)

CMP: 64-byte header (`"CMP "`, 2×uint32, 52 pad) then 256 × {r,g,b} (full 0–255).
MAT: 76-byte header (`"MAT "`, version 0x32, type, celCount, textureCount,
ColorInfo[56] — `bpp` at offset 24). Type 0 = flat color (ColorHeader, colornum
indexes the palette). Type 2 = texture: celCount × TextureHeader[40], then per
cel TextureData{int w, int h, 3×int pad, int numMips} + mip pixels (largest
first), 8-bit palette indices. Texture coords in JKL are in texels (UV × material
size), so the renderer needs material dimensions to normalize — `MatFile` exposes
`Width`/`Height`.

### Game install resolution (`Game/GameInstall`, mirrors `U_OPTIONS.PAS`)

Per game a single **base install dir** is configured; resources are found under it
case-insensitively in `Episode/` and `Resource/` (the original's FindGobJK/FindGoo):
- **Jedi Knight**: levels `Episode/Jk1.gob`; resources `Resource/Res2.gob` (MAT/CMP),
  `Resource/Res1hi.gob`.
- **MotS**: `Jkm.goo` / `Jkmres.goo`. **IJIM**: `CD1.GOB` / `CD2.GOB`.

`AppSettings` (Sed.App) persists the dirs as JSON under the OS app-data dir
(macOS: `~/Library/Application Support/SED/settings.json`). The app auto-opens a
configured game on startup. Material lookups search the resource archives.

### Texture coordinates (important)

JK stores surface texture-vertex UVs in **texel** units, not 0..1. The renderer
normalizes per material: `uv01 = texelUV / materialPixelSize` (matches
`PRENDER.PAS`: `vd.u = u / tx.width`). `SceneRenderer` pushes `invTexSize =
(1/w, 1/h)` per submesh as a push constant and the vertex shader applies it.
Surface `uscale/vscale` are *not* applied at render time — the stored UVs already
encode scaling. No-material surfaces (adjoin portals / sky) are skipped in
`SceneBuilder.BuildScene`.

### JKL format notes (from `src/LEVEL_IO.INC`)

`SECTION: <name>` … `END`; `#` comments; most lines upper-cased (names are not).
GEORESOURCE builds global tables — `WORLD VERTICES`, `WORLD TEXTURE VERTICES`,
`WORLD ADJOINS`, `WORLD SURFACES`, then **one normal line per surface (no
header — consumed by count)**. SECTORS reference the global surface list via
`SURFACES <base> <count>` and de-dup vertices per sector. Surface line:
`mat sflags(hex) fflags(hex) geo light tex adjoin extralight nverts v,t … intensities`
— extralight is 1 float (JK/MotS) or 4 (IJIM); >nverts trailing floats means
MotS/IJIM per-vertex RGB(A), else JK grayscale.
