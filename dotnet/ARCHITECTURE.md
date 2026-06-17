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
| `Sed.Core` | Math (double-precision `Vec2/3`, `ColorF`, `Box`) + domain model (`Level`, `Sector`, `Surface`, `Vertex`, `Thing`, `Light`, `Cog`, `LevelHeader`) | ✅ builds, unit-tested |
| `Sed.Formats` | **JKL** (`Jkl/`), **GOB** (`Gob/`), **MAT/CMP** (`Material/`), **`Game/GameInstall`** (per-game base dir → resource GOBs) | ✅ validated on retail data |
| `Sed.Core` | + `Mat4` (column-major, Vulkan-clip perspective/lookat) | ✅ 9 tests |
| `Sed.Rendering` | `Camera` (+ fly basis), `Mesh`, `SceneBuilder`, `TextureLookup`, **`Picker`** (screen→ray, ray/triangle, nearest surface), `PngWriter` | ✅ |
| `Sed.Rendering.Vulkan` | Silk.NET backend; textured `SceneRenderer` (descriptor-set textures, depth, MVP) + **depth-off selection-highlight overlay** | ✅ verified on M4 Pro |
| `Sed.App` | Avalonia shell + `VulkanView` (fly camera + click-pick); **Game menu configures per-game install dirs (`AppSettings`), auto-loads levels+textures from the base folder**; File ▸ Open for loose .jkl/.gob | ✅ |
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
8. ~~Fly camera navigation + surface picking~~ ✅ `Picker` (unit-tested) +
   `VulkanView` fly controls + `SceneRenderer.SetSelection` highlight. **Next:**
   thing picking/placement, then editing (move verts/surfaces) + undo.
   Later: real colormap-table lighting, transparency/alpha materials, 3DO models.

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

### JKL format notes (from `src/LEVEL_IO.INC`)

`SECTION: <name>` … `END`; `#` comments; most lines upper-cased (names are not).
GEORESOURCE builds global tables — `WORLD VERTICES`, `WORLD TEXTURE VERTICES`,
`WORLD ADJOINS`, `WORLD SURFACES`, then **one normal line per surface (no
header — consumed by count)**. SECTORS reference the global surface list via
`SURFACES <base> <count>` and de-dup vertices per sector. Surface line:
`mat sflags(hex) fflags(hex) geo light tex adjoin extralight nverts v,t … intensities`
— extralight is 1 float (JK/MotS) or 4 (IJIM); >nverts trailing floats means
MotS/IJIM per-vertex RGB(A), else JK grayscale.
