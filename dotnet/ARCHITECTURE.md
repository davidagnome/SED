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
| `Sed.Rendering` | Backend-agnostic `IRenderer`, `Camera` | 🚧 interface only |
| `Sed.Rendering.Vulkan` | Silk.NET Vulkan backend; MoltenVK locator, logical device, **offscreen triangle pipeline** | ✅ device + render pass + SPIR-V pipeline + draw + readback verified on M4 Pro |
| `Sed.App` | Avalonia desktop shell (menu, side panel, Vulkan viewport, status bar) | ✅ composed window renders the MoltenVK triangle |
| `tools/Sed.VulkanSmoke` | Vulkan instance/GPU bring-up check | ✅ |
| `tools/Sed.TriangleProbe` | Offscreen render → PNG (dependency-free encoder) | ✅ |
| `tools/Sed.AppShot` | Headless Skia capture of the editor window → PNG | ✅ |
| `tests/Sed.Core.Tests` | xUnit | ✅ 6 passing |

### Rendering pipeline (verified)

`VulkanContext` (instance, portability enumeration) → `VulkanDevice` (physical
device pick, graphics queue, `VK_KHR_portability_subset`, command pool) →
`OffscreenRenderer` (RGBA8 color image, render pass, embedded SPIR-V graphics
pipeline, draw, image→buffer copy, host readback). The editor displays the
readback via `VulkanViewport.ToBitmap` into an Avalonia `WriteableBitmap`. This
offscreen-to-bitmap path is intentionally the same mechanism a live, resizable
viewport will use.

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
4. **Live viewport**: re-render on resize/camera change (replace the one-shot
   frame); add depth buffer + MVP uniform; consider a true swapchain via
   Silk.NET windowing embedded through `NativeControlHost` if blit latency matters.
5. **Level geometry** pipeline: triangulate `Sed.Core.Model` sectors/surfaces →
   vertex/index buffers; flat/Gouraud shading from sector lighting.
6. **JKL parser** in `Sed.Formats` → load a real level into the model.
7. Camera/navigation, surface picking, then the editing/undo layer.
