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
| `tests/Sed.Core.Tests` | xUnit | ✅ 243 passing |

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
44. ~~Layer visibility~~ ✅ `LayerVisibility` is shared editor state (like
     `EditHistory` and `SelectionSet`) held by `VulkanView` and handed to
     `MapView`. Hiding a layer drops its sectors, things and lights from
     `SceneAssembler`, from the 2D render, **and from every picker** — being able
     to select invisible geometry would be worse than not hiding it at all.
     Stored as "visible unless hidden" so layers added later don't default to
     invisible. The left panel lists a checkbox per layer plus "Show all"; because
     retail levels carry no LAYERS section yet still place everything on layer 0,
     the list is sized by the highest layer index actually in use as well as by
     the declared names, so those levels still have something to toggle.
     Verified on `03katarn`: hiding one of three synthetic layers takes the scene
     from 9,310 to 6,159 triangles and makes its sectors and things unpickable.
45. ~~Bridge surfaces~~ ✅ `BridgeSurfacesCommand` ports `ConnectSurfaces` from
     `LEV_UTILS.PAS`. A plain adjoin only works when two faces are already the same
     shape; bridging trims them first — each face is cleaved by every edge plane of
     the other (the plane through that edge with normal `edge × normal`, which
     points out of the polygon so the cleave keeps the inside) — then adjoins the
     shared region, leaving the offcuts as separate surfaces. Enforces the
     original's preconditions (different sectors, neither already adjoined,
     opposed normals, same plane) and, if the trimmed faces turn out not to
     overlap, **rolls its own trimming back** rather than leaving half-cleaved
     geometry behind. Bound to **Ctrl+B** with exactly two surfaces selected.
46. ~~Engine flag corrections~~ ✅ **Bug fix.** Several constants in `EngineFlags.cs`
     did not match the original. `SF_DoubleRes`/`SF_HalfRes` were 0x08/0x10 instead
     of **0x10/0x20**; `SF_Water` was 0x1000 instead of **0x20000**;
     `FF_TexClampX`/`Y` were 0x08/0x10 instead of **0x04/0x08**, and a bogus
     `TexFlip = 0x04` sat on top of the real `FF_TexClampX`. These bits are written
     verbatim into the JKL surface line, so a wrong value corrupts the level the
     moment anything writes one. Nothing referenced them yet, so no data was
     harmed — they are now transcribed from `J_LEVEL.PAS` and `GEOMETRY.PAS`,
     with the missing IJIM and resolution flags added and the values pinned by
     tests.
47. ~~Texture clamp flags~~ ✅ `FF_TexClampX`/`FF_TexClampY` now affect rendering,
     matching `PRenderGL`/`PRenderDX` which set `GL_CLAMP_TO_EDGE` /
     `D3DTADDRESS_CLAMP`. Addressing is per-surface but a sampler is per-material,
     so the clamp mode joins the batch key (material, translucency, sky, **clamp**)
     and is applied in the fragment shader with a half-texel inset. Verified on
     retail levels: each material emits exactly one submesh per distinct
     clamp/blend/sky combination (94 materials → 147 submeshes on `03katarn`).
48. ~~Template editor~~ ✅ `TemplateEditorWindow` (Tools ▸ Templates…) browses and
     edits the TEMPLATES section, which already round-tripped but had no UI. The
     detail pane separates a template's **own** parameters from those it inherits
     through the parent chain, showing which ancestor each inherited value comes
     from and offering a one-click override — that inheritance is the thing you
     actually need to see when deciding whether to set a value locally.
     `TemplateParams` types the parameter names, transcribed from the `tplNames` /
     `TplVtypes` tables in `VALUES.PAS`: only ~16 keys are classified (material,
     model3d, soundclass, the template-reference keys, frame, thingflags) and
     everything else is free text, matching the original rather than inventing a
     taxonomy. **Renaming repoints every reference** — things that instantiate the
     template and child templates that inherit from it — because leaving them
     behind would silently break the level; deleting reports how many references
     it orphaned. `Template.Order` is now explicit so deleting one cannot perturb
     Dictionary enumeration order and churn the whole section on the next save.
49. ~~Writer: empty template parent~~ ✅ **Bug fix.** The parent occupies a fixed
     second token on a template line, so a template with an empty parent wrote
     `beta  size=2` — and the parser then read `size=2` as the parent name,
     silently swallowing the first parameter. Retail levels always carry the
     `none` sentinel so nothing hit this, but the new editor can create
     parentless templates. Empty parents are now written as `none`.
50. ~~Placed-COG editor~~ ✅ A placed COG in the JKL is a script name plus bare
     positional values — unreadable on their own. `Sed.Formats.Cogs.CogScript`
     parses a `.cog` file's `symbols` block and `CogScriptLibrary` resolves scripts
     from the archives, so `CogEditorWindow` (Tools ▸ COGs…) can label each value
     with the symbol it feeds, its type, and its comment.
     The mapping rule — values correspond in order to the symbols that are neither
     **`local`** nor **`message`** — was derived from the data, not assumed:
     across `01narshadda`, `03katarn`, `07yun` and `09fuelstation` all **227
     placed COGs resolve and every one's value count matches** its script's
     level-supplied symbol count, with zero mismatches. Script lookup must search
     the **level** archive as well as the resource archives; level-specific scripts
     live in the episode GOB. When a script cannot be found the values stay
     editable positionally rather than the window refusing to open, and a COG whose
     value count disagrees with its script is flagged, since every later symbol
     would then be reading the wrong value. `DeleteCogCommand` restores a COG at
     its original index because scripts reference each other by COG number.
51. ~~Case-insensitive material batching~~ ✅ **Bug fix.** `SceneAssembler` keyed
     render batches on the material name case-sensitively, but levels declare the
     same material under different casings — `01narshadda` has seven such pairs
     (`01wgril1.mat` / `01WGRIL1.mat`). Since `MaterialLibrary` resolves names
     case-insensitively, each pair uploaded the same texture twice and cost an
     extra draw call. Normalising the batch key drops `01narshadda` from **108 to
     104 submeshes** with an identical render.
52. ~~Asset pickers~~ ✅ Fields that name something now offer a browse button
     instead of only a raw text box. `AssetCatalog` enumerates the open archives by
     extension (lazily and cached — a retail install has ~2,000 MATs, ~640 WAVs,
     ~500 each of COG/KEY and 477 3DOs); `PickerDialog` is a filtered list;
     `PickerField` combines the two with a still-editable text box, so a value the
     catalog does not know can be typed anyway.
     The type information already existed and was simply unused: `TemplateParams`
     drives the template editor's material/model3d/soundclass/template fields, and
     `CogScript`'s symbol types drive the COG editor's. **`thing`, `sector` and
     `surface` symbols pick from the level itself**, labelled by `LevelQuery` so a
     picker reads exactly like a Find result — those symbols hold bare indices and
     were previously impossible to set correctly without counting. The surface
     inspector's material and the thing inspector's template also pick.
     Verified against retail archives: every material the level uses (94 on
     `03katarn`) and every placed COG script (25) appears in the catalog.
53. ~~Cleave sector~~ ✅ `CleaveSectorCommand` ports `CleaveSector` — splits a whole
     sector by a plane into two adjoined sectors: classify vertices, split every
     straddling surface, chain the on-plane edges into the cross-section, move the
     behind-side surfaces and vertices into a new sector, and cap the opening with
     a mirrored adjoined pair. Because it changes surface ownership and vertex
     membership across two sectors, it snapshots the affected topology before and
     after and swaps between them, which keeps object identity stable across
     undo/redo — adjoin partners in *other* sectors point at these surfaces.
     The subtle part is **welding the cut vertices**: neighbouring faces cross the
     plane at the same corner, and the cross-section is chained by vertex identity,
     so minting a fresh vertex per face leaves four disconnected stubs instead of a
     closed loop and the cleave silently reports failure. Verified on retail rooms
     (a 24-surface room splits into 14 + 20 with every adjoin in the level intact).
54. ~~Connect sectors~~ ✅ `ConnectSectorsCommand` ports `ConnectSectors`: two
     overlapping sectors describe the same volume twice, which the engine cannot
     render, so each is cleaved by the other's face planes, the duplicate overlap
     is deleted and the shared boundaries become portals. Built entirely from
     existing reversible commands, so undo is just replaying them backwards.
     **Ctrl+Shift+B** with two sectors selected.
     One deviation from a literal port: the original adjoins first and deletes
     second, which leaves one boundary open because the faces it wants are still
     adjoined to the doomed sector and get skipped. Deleting first frees them, and
     both boundaries portal — two overlapping boxes become three slabs with a
     portal pair on each internal boundary.
55. ~~Delete sector clears inbound adjoins~~ ✅ **Bug fix.** `DeleteSectorCommand`
     removed a sector from the level but left surfaces in *other* sectors still
     adjoined to it — portals opening onto a sector that no longer exists. This
     predates Connect Sectors and affected the plain **Edit ▸ Delete Sector**
     command; the original clears them via `RemoveSecRefs`. Inbound adjoins are now
     cleared on delete and restored on undo.
56. ~~Align texture to neighbour~~ ✅ `AlignTextureToNeighbourCommand` matches a
     surface's UVs to a neighbour it shares an edge with. The shared edge is the
     common axis: the reference's mapping is decomposed into a gradient *along*
     the edge and one *perpendicular* to it, and the target is given both. That is
     what continuity means for two faces meeting at an angle — they are not
     coplanar, so there is no single flat projection to share. **Ctrl+Shift+T**.
57. ~~COG generator~~ ✅ `MasterCogGenerator` + `CogGeneratorWindow`
     (Tools ▸ Generate Master COG) port `U_COGGEN`: emits the level master COG
     that registers itself, initialises goals, grants starting weapons and sets
     the Force rank on a timer. Ammunition follows the original's rules rather
     than being handed out blindly — energy only for weapons that consume it,
     power only for the power weapons, rail charges only with the railgun. The
     generated script is round-tripped through `CogScript.Parse` in the tests, so
     the editor's own parser validates the generator's output. Goal *strings* live
     in `cogstrings.uni`, which this does not write — the window says so rather
     than letting them look handled.
58. ~~Map view readout~~ ✅ Cursor world coordinates and, while a box is being
     dragged, the span and diagonal it covers; grid step shown in the legend.
     Vertex dots now appear for every visible sector in Vertex mode (dimmed
     outside the active one) so a vertex can be picked without first selecting its
     sector.
59. ~~Managed plugin model~~ ✅ `Sed.Plugins` defines the contract —
     `ISedPlugin`, `PluginCommand`, and a `PluginContext` carrying the **real**
     `Level`, `EditHistory` and `SelectionSet`. The original needed ~100 COM
     accessor methods only because a Delphi DLL could not share an object graph;
     a managed plugin gets the model directly and pushes edits through the undo
     stack, so plugin changes are ordinary undoable edits.
     `PluginHost` loads each assembly into its own collectible
     `AssemblyLoadContext` for dependency isolation, but deliberately resolves
     **`Sed.Plugins` and `Sed.Core` to the host's copies** — loading those
     per-plugin would make the plugin's `Level` type a different type from the one
     it is handed, failing every call with a confusing cast error. A plugin that
     throws is contained and reported rather than taking down the editor, and a
     bad assembly in the folder is surfaced in the menu instead of silently
     skipped.

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
- ✅ Box-select; cursor coordinate + drag-measurement readout; vertex dots for
  every visible sector in Vertex mode (dimmed outside the active one).

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
- ✅ **Bridge/connect surfaces** — `BridgeSurfacesCommand` (**Ctrl+B**, with two
  surfaces selected) trims each face by the other's edge planes, then adjoins the
  shared region, so faces of different sizes can be joined.
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
- ✅ `FF_TexClampX`/`FF_TexClampY` — clamp addressing, applied in the fragment
  shader and split into separate render batches (2,463 of `03katarn`'s surfaces
  use them).
- ❌ `SF_DoubleRes`/`HalfRes` are **not** a static texture scale — they drive the
  `SlideWall` COG function at runtime. The original's `GetSurfResScale` is dead
  code and its uscale/vscale mapping in `LEVEL_IO.INC` is commented out, so
  scaling UVs by them would disagree with both the game and the original editor.
- ✅ Align-to-neighbour — matches a surface's UVs to one it shares an edge with,
  so the texture runs continuously across the seam (**Ctrl+Shift+T**).
- ❌ `FF_TexNoFiltering` is a non-item here: MAT textures are uploaded as palette
  *indices* and the sampler must be `Filter.Nearest`, because interpolating index
  values produces garbage. Every surface already behaves as "no filtering".
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
- ✅ **Template editor/creator** (`TemplateEditorWindow`, Tools ▸ Templates…):
  browse/filter, edit parameters, override inherited ones, set parent, and
  create/clone/rename/delete templates — all undoable.
- ✅ **Placed-COG editor** (`CogEditorWindow`, Tools ▸ COGs…): resolves each
  placed COG's `.cog` script from the archives and labels its positional values
  with the symbol they feed.
- ✅ COG **generator** (`U_COGGEN`) — Tools ▸ Generate Master COG.
- ⬜ **Cutscene helper** (`U_CSCENE`): it is a keyframe previewer — assign a
  `.key` file and a time per thing, then play it back on the thing's 3DO. That
  needs a KEY parser and 3DO skeletal animation, neither of which exists; it
  belongs with the 3DO tooling (P10) rather than with the COG work.
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
- ✅ **Layer visibility toggles** — a checkbox list in the left panel; hidden
  layers vanish from both views and from picking.
- ⬜ Episode editor.

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

### P12 — Extensibility (architectural redesign) ✅
The original plugin host is **Windows COM + native DLLs** (`SED_COM`,
`sed_plugins`) — not portable. Replaced with a **managed plugin model**: the
`Sed.Plugins` contract plus `PluginHost`, which loads assemblies from a
`plugins` folder via `AssemblyLoadContext`. A **Plugins** menu lists whatever is
installed.

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
