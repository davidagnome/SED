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
| `Sed.Formats` | JKL/JED readers & writers | 🚧 skeleton |
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
6. **JKL parser** in `Sed.Formats` → load a real level into the model, then
   render it instead of the sample cube.
7. Texture/material loading (CMP/MAT/colormaps) and Gouraud lighting from sector
   ambient/extra-light + point lights.
8. Camera WASD/fly navigation, surface/thing picking, then editing + undo.
