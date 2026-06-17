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
| `Sed.Formats` | **JKL** (`Jkl/`), **GOB archive** (`Gob/`), **MAT texture + CMP palette** (`Material/`) | ✅ validated on retail JK1/Res2 data |
| `Sed.Core` | + `Mat4` (column-major, Vulkan-clip perspective/lookat) | ✅ 9 tests |
| `Sed.Rendering` | `IRenderer`/`Camera` (+ orbit/look-at, view-proj), `Mesh`, `SceneBuilder` (model→mesh), `SampleScene`, `PngWriter` | ✅ |
| `Sed.Rendering.Vulkan` | Silk.NET backend; MoltenVK locator, logical device, **offscreen triangle** + **3D `SceneRenderer`** (vertex/index buffers, depth, MVP push constant, dynamic viewport) | ✅ verified on M4 Pro |
| `Sed.App` | Avalonia shell + **live `VulkanView`** (resizable, mouse orbit, wheel zoom) | ✅ renders the sample cube in-window |
| `tools/Sed.VulkanSmoke` | Vulkan instance/GPU bring-up check | ✅ |
| `tools/Sed.TriangleProbe` | Offscreen triangle → PNG | ✅ |
| `tools/Sed.SceneProbe` | 3D cube (model→mesh→MVP→depth) → PNG | ✅ |
| `tools/Sed.AppShot` | Headless Skia capture of the editor window → PNG | ✅ |
| `tests/Sed.Core.Tests` | xUnit | ✅ 9 passing |

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
   - 7b. **Wire textures into the Vulkan renderer** (next): per-material mesh
     batches, `VkImage`+sampler+descriptor sets, sample by the UVs the parser
     already captures (`Surface.Corner.Uv`); modulate by per-vertex intensity
     (`Surface.Corner.Intensity`) for Gouraud lighting. Needs a material provider
     that loads MATs from the resource GOB by name + the level's CMP.
8. Camera WASD/fly navigation, surface/thing picking, then editing + undo.

### MAT / CMP formats (from `src/graph_files.pas`)

CMP: 64-byte header (`"CMP "`, 2×uint32, 52 pad) then 256 × {r,g,b} (full 0–255).
MAT: 76-byte header (`"MAT "`, version 0x32, type, celCount, textureCount,
ColorInfo[56] — `bpp` at offset 24). Type 0 = flat color (ColorHeader, colornum
indexes the palette). Type 2 = texture: celCount × TextureHeader[40], then per
cel TextureData{int w, int h, 3×int pad, int numMips} + mip pixels (largest
first), 8-bit palette indices. Texture coords in JKL are in texels (UV × material
size), so the renderer needs material dimensions to normalize — `MatFile` exposes
`Width`/`Height`.

### JKL format notes (from `src/LEVEL_IO.INC`)

`SECTION: <name>` … `END`; `#` comments; most lines upper-cased (names are not).
GEORESOURCE builds global tables — `WORLD VERTICES`, `WORLD TEXTURE VERTICES`,
`WORLD ADJOINS`, `WORLD SURFACES`, then **one normal line per surface (no
header — consumed by count)**. SECTORS reference the global surface list via
`SURFACES <base> <count>` and de-dup vertices per sector. Surface line:
`mat sflags(hex) fflags(hex) geo light tex adjoin extralight nverts v,t … intensities`
— extralight is 1 float (JK/MotS) or 4 (IJIM); >nverts trailing floats means
MotS/IJIM per-vertex RGB(A), else JK grayscale.
