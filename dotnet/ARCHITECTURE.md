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
| `Sed.App` | Avalonia shell + `VulkanView`: fly camera; click-pick **things / vertices / surfaces**; arrow-keys move selection (thing/vertex/whole surface) with **live mesh rebuild** + Edit ▸ Undo/Redo; Game menu + File ▸ Open | ✅ |
| `tools/Sed.GobTool`, `Sed.JklProbe`, `Sed.MatTool`, `Sed.LevelRender` | GOB list / JKL render / MAT→PNG / **textured level → PNG** | ✅ |
| `tools/Sed.VulkanSmoke`, `Sed.TriangleProbe`, `Sed.SceneProbe`, `Sed.AppShot` | bring-up + capture probes | ✅ |
| `tests/Sed.Core.Tests` | xUnit | ✅ 19 passing |

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

## Next steps (roughly in order)

1. ~~Device + logical device + queue~~ ✅
2. ~~Avalonia shell hosting Vulkan output~~ ✅
3. ~~First triangle~~ ✅
4. ~~Live viewport: resize + depth + MVP; mouse orbit/zoom~~ ✅
5. ~~Level-geometry pipeline (model→mesh)~~ ✅ (procedural cube; real lighting TBD)
6. ~~JKL parser → load a real level and render it~~ ✅ **Validated against retail
   `JK1.GOB`**: all 25 levels parse (20–966 sectors, up to 27k tris). The editor's
   `File ▸ Open` reads a loose `.jkl` or a `.gob` archive and lists its levels in
   the side panel (`tools/Sed.GobTool` does the same from the CLI).
   Adjoins/COGs/lights/layers not yet parsed.

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
    **Next:** material picker panel, surface split, COG/template editing, lighting tools.

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
