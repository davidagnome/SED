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
| `Sed.Rendering.Vulkan` | Silk.NET Vulkan backend; MoltenVK locator + instance/device | ✅ instance + GPU enumeration verified on M4 Pro |
| `tools/Sed.VulkanSmoke` | Headless Vulkan bring-up check | ✅ passes |
| `tests/Sed.Core.Tests` | xUnit | ✅ 6 passing |
| `Sed.App` (planned) | Avalonia desktop UI | ⬜ not started |

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

1. **Device + swapchain**: pick a physical device, create logical device +
   graphics/present queues, enable `VK_KHR_portability_subset` on macOS.
2. **Avalonia shell** (`Sed.App`) hosting a Vulkan-rendered surface.
3. **First triangle**, then **level geometry** pipeline (sectors → surfaces →
   triangulated meshes from `Sed.Core.Model`).
4. **JKL parser** in `Sed.Formats` → load a real level into the model.
5. Camera/navigation, surface picking, then the editing/undo layer.
