# SED .NET — Full Parity Execution Plan

A task-by-task breakdown of the work that **remains** to reach feature parity with
the original Delphi/VCL editor. Each task is self-contained: any agent can pick it
up and implement it.

For what is already **done**, see the milestone list in `ARCHITECTURE.md` — do not
re-derive it from this file. Formats (all nine JKL sections read + write), GOB
read/write, MAT/CMP, 3DO read, the Vulkan renderer, the command/undo system, the
2D map view, the mode system with per-entity inspectors, the geometry and texture
operations, and the consistency checker are all complete and wired to the UI.

## Conventions

### IEditCommand pattern
Every editing operation is a reversible command (`src/Sed.Core/Editing/`):
```csharp
public sealed class XxxCommand : IEditCommand
{
    public string Name => "Xxx";
    public void Apply()  { /* mutate model; capture state on first call */ }
    public void Revert() { /* restore captured state */ }
}
```
Push through `EditHistory.Do(cmd)` for undo/redo. Reference:
`CreateDeleteCommands.cs`, `TransformCommands.cs`.

### Reaching the UI
A command is not done when it compiles and passes tests — it is done when a user
can invoke it. Wire every new command to **both** a menu item and (where it earns
one) a key binding in `MainWindow.BuildMenu`, using the `Item(header, gesture,
action)` helper. That helper also registers a window-level `KeyBinding`, because
Avalonia's `MenuItem.InputGesture` is display-only. Guard on the current
selection and report failure through `SelectionChanged` rather than silently
doing nothing.

Before calling a track done, run:
```
grep -rho 'class [A-Za-z]*Command' src/Sed.Core/Editing/ | awk '{print $2}' | sort -u
```
and confirm each name is referenced somewhere under `src/Sed.App/`.

### JKL section parse pattern
- Add a `case "SECTION": LoadXxx(r, level); break;` in `JklParser.Parse`
  (`src/Sed.Formats/Jkl/JklParser.cs:51`).
- Use `JklReader.Next()`, `Tokens()`, `StripIndex()`, `ParseFloat/Int/Hex`.

### JKL section write pattern
- Add `AddSection(ranges, appended, src, "SECTION", GenerateXxx(level), hasContent)`
  in `JklWriter.Build` (`src/Sed.Formats/Jkl/JklWriter.cs`).
- Write a `GenerateXxx` that returns `List<string>` of section lines.
- `hasContent` decides what happens when the source lacks the section: true
  appends it at the end of the file, false skips it. Pass `level.Xxx.Count > 0`
  for editor-authored sections so untouched levels don't gain empty ones.

### Test pattern
- `[Fact]` in `tests/Sed.Core.Tests/`.
- Round-trip: `JklParser.ParseDocument(jkl)` → mutate → `JklWriter.Build(doc)` →
  `JklParser.Parse(output)` → assert.
- Unit: construct model objects directly, apply/revert commands, assert.
- Pure geometry belongs in `Sed.Core` (e.g. `GeometryOps.MidCleavePlane`), not in
  a view — views cannot be unit-tested, `Sed.Core` can.

### Build / test / verify
```
dotnet build dotnet/Sed.slnx
dotnet test  dotnet/tests/Sed.Core.Tests
dotnet run --project dotnet/tools/Sed.OpsProbe -- "<game dir>" 03katarn
dotnet run --project dotnet/tools/Sed.AppShot -- /tmp/sed.png
```
`Sed.OpsProbe` exercises the editing operations against a retail level and checks
the result survives a save/reparse; `Sed.AppShot` renders the whole shell
headlessly so UI changes can be eyeballed without opening a window.

### Key file map
| Concern        | File                                          |
|----------------|-----------------------------------------------|
| Parser         | `src/Sed.Formats/Jkl/JklParser.cs`            |
| Low-level read | `src/Sed.Formats/Jkl/JklReader.cs`            |
| Parse tables   | `src/Sed.Formats/Jkl/GeoData.cs`              |
| Round-trip doc | `src/Sed.Formats/Jkl/JklDocument.cs`          |
| Writers        | `src/Sed.Formats/Jkl/JklWriter.cs` + `GeoResourceWriter.cs` |
| GOB write      | `src/Sed.Formats/Gob/GobWriter.cs`            |
| Commands       | `src/Sed.Core/Editing/*.cs`                   |
| Domain model   | `src/Sed.Core/Model/*.cs`                     |
| Geometry math  | `src/Sed.Core/GeometryOps.cs`                 |
| Validation     | `src/Sed.Core/Validation/ConsistencyChecker.cs`|
| 3D viewport    | `src/Sed.App/VulkanView.cs`                   |
| 2D map view    | `src/Sed.App/MapView.cs`                      |
| Shell/menus    | `src/Sed.App/MainWindow.cs`                   |
| Inspectors     | `src/Sed.App/Inspectors/*.cs`                 |
| Tests          | `tests/Sed.Core.Tests/*.cs`                   |

---

## Dependency graph

```
WAVE 0 (fully parallel — no inter-dependencies):
  S1 SelectionSet ✅ DONE     L1 LightCalculator ✅ DONE
  U1 HeaderEditor ✅ DONE     F1 SaveJklGob ✅ DONE
  U2 LayerPanel ✅ DONE       U3 TemplateEditor ✅ DONE
  G1 Bridge+Connect ✅ DONE   T1 TexFlags 🟡 partial

WAVE 1:
  S2 BoxSelect ✅ DONE    S3 CopyPaste ✅ DONE    L2 LightEntities ✅ DONE
  Q1 FindDialogs ✅ DONE  U4 CogEditor 🟡 partial

WAVE 2:
  F2 SaveAndTest ✅ DONE     I1 DfImport ✅ DONE   I2 ThreeDoExport ✅ DONE
  X1 UxParity ✅ DONE (recent files, autosave, backup, recovery)

WAVE 3:
  P1 PluginModel ✅ DONE
```

---

## TRACK S — Selection model (the biggest practical gap)

### S1 — SelectionSet ✅ DONE
`src/Sed.Core/Editing/SelectionSet.cs` + `CompositeCommand.cs`. Owned by
`VulkanView`, handed to `MapView`. Ctrl/Cmd+click toggles, plain click replaces,
Esc clears, Ctrl+A selects the active sector's surfaces. Move/rotate/scale/
delete/set-material act on the whole selection as one undo step.
Covered by `SelectionSetTests` + `MultiEditTests`, and by `Sed.OpsProbe` on
retail levels.

### S2 — Box-select ✅ DONE
`MapView` rubber-bands on a plain drag over empty space; Ctrl+drag extends;
panning moved to middle-drag / Shift+drag / Alt+drag. Surfaces and sectors need
full containment; vertices and things just need their point inside.
Covered by `tools/Sed.UiProbe` (simulated pointer input).

### S3 — Copy / paste ✅ DONE
`LevelFragment` + `PasteFragmentCommand` (`src/Sed.Core/Editing/LevelClipboard.cs`),
bound to Ctrl+C / Ctrl+V / Ctrl+D. Deep-clones vertices (never aliases), remaps
in-fragment adjoins and clears outward-pointing ones. Covered by `ClipboardTests`
and by `Sed.OpsProbe`, which also confirms a pasted room survives the JKL
round-trip. Lights are copied too (L2).

---

## TRACK L — Lighting

### L1 — Lighting calculation ✅ DONE
`src/Sed.Core/Lighting/LightCalculator.cs` + `CalculateLightingCommand`, on
**Tools ▸ Calculate Lighting** (F9 / Shift+F9 for no shadows). Ports the engine
falloff, shadow tracing (portals pass light unless `SAF_BlockLight`) and the
sector-ambient pass; one undo step; accelerated by a uniform spatial grid
(full bake of the largest retail level ≈ 0.5 s). Covered by `LightCalculatorTests`
and `Sed.OpsProbe`.

### L2 — Light entities as selectable objects ✅ DONE
`SelectionSet` has a `Light` bucket; `Picker.PickLight` + `MapView` diamonds make
lights pickable and box-selectable in Light mode; `CreateLightCommand` /
`DeleteLightCommand` / `MoveLightCommand`; `LevelFragment` copies lights; the
Light inspector is wired to the primary selection. Covered by `LightEntityTests`
and by both probes.

Fixing this exposed a writer bug — `JklWriter` dropped any section the source
lacked, so lights added to a retail level were lost on save. Missing sections
with content are now appended (`SectionRoundTripTests`).

---

## TRACK U — Data editors whose formats are already done

Each of these has a complete parse + faithful write already; the work is purely
UI plus the field commands.

### U1 — Header editor ✅ DONE
`src/Sed.App/HeaderEditorWindow.cs` (Tools ▸ Level Header…) + a generic
`SetHeaderFieldCommand<T>` in `HeaderFieldCommands.cs`. Covers gravity, both sky
descriptions, the mipmap/LOD arrays, perspective/gouraud and fog; every field is
its own undo step. Covered by `HeaderFieldTests` and both probes.

### U2 — Layer panel ✅ DONE
`LayerVisibility` (`src/Sed.Core/Editing/`) shared by both views; checkbox list +
"Show all" in the left panel. Hidden layers are dropped from `SceneAssembler`,
the 2D render and every `Picker` entry point. Covered by `LayerVisibilityTests`
and `Sed.OpsProbe`.

**Remaining**: no UI to rename/add/remove layers or re-assign objects in bulk —
per-object assignment is still via the inspectors. Episode editor not started.

### U3 — Template editor ✅ DONE
`src/Sed.App/TemplateEditorWindow.cs` (Tools ▸ Templates…) + `TemplateCommands.cs`
+ `TemplateParams` (param-name → kind, transcribed from `VALUES.PAS`). Browse and
filter, edit/add/remove parameters, override inherited values, set parent, and
create/clone/rename/delete. Rename repoints things and child templates.
Covered by `TemplateEditTests` and `Sed.OpsProbe`.

Asset pickers are wired in (milestone 52).

### U4 — COG editor 🟡
- ✅ **Placed-cog symbol-value editor** — `src/Sed.Formats/Cogs/CogScript.cs` +
  `CogScriptLibrary` + `src/Sed.App/CogEditorWindow.cs` (Tools ▸ COGs…), with
  `CogCommands.cs` for add/delete/set-value/set-script. Values are labelled with
  the script symbol they feed. Covered by `CogScriptTests` and `Sed.OpsProbe`.
- ✅ COG **generator** — `MasterCogGenerator` + `CogGeneratorWindow`
  (Tools ▸ Generate Master COG).
- 🟡 **Cutscene helper** (`U_CSCENE.PAS`) — the **KEY parser** now exists
  (`Sed.Formats.Keyframe.KeyFile`: HEADER + per-node entries with rest pose +
  per-frame deltas, `GetFrame` linear interpolation, faithful to
  `PJKEY_IO.INC`/`U_PJKEY.PAS`), and the 3DO viewer's **frame scrubber**
  shows each node's interpolated pose per frame. What's still missing is
  playing the pose back on the rendered 3DO in a preview viewport.
- ✅ Asset pickers: asset symbols browse the archives, and `thing`/`sector`/
  `surface` symbols browse the level with Find-style labels (milestone 52).

---

## TRACK Q — Find / navigate

### Q1 — Find dialogs ✅ DONE
`Sed.Core.Query.LevelQuery` + `src/Sed.App/FindWindow.cs` (**Ctrl+Shift+F**).
Searches sectors / surfaces / things / lights by index, material, name, template,
colormap/sound or flag mask; click a result to select and frame it
(`VulkanView.JumpTo`); "Select all matches" feeds every hit into the selection.
Covered by `LevelQueryTests` and both probes.

The original's per-field query builder is now the **Fields** tab: one row per
field (material, adjoin sector/surface, each flag word, geo/light/tex, name,
template, position, volume, layer name…) with the original's operator set
(`=`, `<>`, `>`, `<`, SET / NOT SET bitmask, contains) — `FieldCriteria` +
`FieldQueryTests`.

---

## TRACK G / T — Remaining geometry & texturing

### G1 — Bridge / connect surfaces ✅ DONE
`BridgeSurfacesCommand` (**Ctrl+B**, two surfaces selected) ports `ConnectSurfaces`
from `LEV_UTILS.PAS`: trims each face by the other's edge planes, then adjoins the
shared region. Rolls back its own trimming if the results don't overlap.
Covered by `BridgeTests`.

`ConnectSectorsCommand` (**Ctrl+Shift+B**) and `CleaveSectorCommand` complete the
sector-level half. Covered by `CleaveSectorTests` and `ConnectSectorsTests`.

### T1 — Texture flag support 🟡
- ✅ `FF_TexClampX`/`FF_TexClampY` affect rendering — clamp mode is part of the
  render batch key and applied in the fragment shader.
- ✅ Flag constants corrected against `J_LEVEL.PAS` / `GEOMETRY.PAS` and pinned
  by `EngineFlagTests`.
- ❌ **Do not** map `SF_DoubleRes`/`HalfRes` to a UV scale. They drive the
  `SlideWall` COG function at runtime; the original's `GetSurfResScale` has no
  callers and its uscale/vscale mapping in `LEVEL_IO.INC` is commented out.
  This entry previously asserted the opposite — it was wrong.
- ✅ Align-to-neighbour (`AlignTextureToNeighbourCommand`, **Ctrl+Shift+T**).
- ❌ `FF_TexNoFiltering` is a non-item: MAT textures are palette *indices*, so the
  sampler must be `Filter.Nearest` — interpolating indices yields garbage. Every
  surface already behaves as "no filtering". Do not "implement" this.

---

## TRACK F / I / X / P — Infrastructure

| Task | Deps | Summary |
|------|------|---------|
| F1 Save JKL+GOB | ✅ DONE | **File ▸ Save as GOB…** writes `jkl\<name>.jkl` into a GOB v2 archive |
| F2 Save and test | F1 | **File ▸ Save GOB and Test…** writes `Test_<level>.gob` into the project dir (configured in **Game ▸ Test Setup…**, default `~/Documents/SED`), then launches: on Windows the original's generated `Test_<level>.bat` (`jk.exe -devmode -dispstats -debug log -displayconfig -path <projectdir>`); elsewhere the user's Wine/CrossOver command template with `{project}` `{gob}` `{game}` `{gameexe}` `{levelname}` placeholders |
| I1 DF import | ✅ DONE | `Sed.Formats.Df.DfLevelImporter` ports `DF_IMPORT.INC` (`.lev` + `.O`): wall cycles, ear-clip triangulation + polygon merging, BOT/TOP/MID wall splitting, reversed-vertex adjoin matching, ambient light, `df2jk.lst` logic conversion, `Layer%d` naming. **Tools ▸ Import Dark Forces Level…** (`DfImportWindow`) with scale + texture options. Covered by `DfImportTests` |
| I2 3DO export + ASC import | ✅ DONE | `ThreeDoWriter` (faithful `T3DO.SaveToFile`: sections, RADIUS, sorted TX-vertex dedup, averaged normals, hierarchy lines) + `ThreeDoExport` (the original's "Export Sector as 3DO": one mesh per layer, position-dedup'd vertices, adjoined surfaces skipped). **Tools ▸ Export Sector as 3DO…**. `AscImporter` ports `ASC_IMPORT.INC` (one sector per Tri-mesh; accepts both the count-bearing and standard 3DS layouts). **Tools ▸ Import 3D Studio ASC…**. Covered by `ThreeDoAndAscTests` |
| X1 UX parity | 🟡 | **Recent files** (File ▸ Recent Files, persisted), **autosave** on a timer into `<projectdir>\autosave` (the original's SaveTimer), **backup copies** (`backup\<level>_NN.jkl`, 100 slots), a **crash-recovery prompt** for the newest autosave on startup, and **configurable keybindings** (`CommandKeys` + Tools ▸ Key Bindings… — every menu command remappable, semicolon-separated gesture lists, resolved by both the menus and the view's chord handling). Remaining: grid/units options |
| I3 3DO tooling | 🟡 | **3DO Model Viewer** (Tools ▸ 3DO Model Viewer…): hierarchy tree (mesh/parent/offset/pyr), mesh stats, KEY file loading with a frame scrubber showing each node's interpolated pose (`KeyFile` parser ports `PJKEY_IO.INC`/`U_PJKEY`). Remaining: standalone rendered preview + KEY playback on the 3DO |
| Episode editor | ✅ DONE | `EpisodeFile` + `CogStrings` (episode.jk: name, game type, LEVEL/DECIDE sequences with line/cd/level/gotoA-B/force powers; cogstrings.uni level names + mission text). **Tools ▸ Episode Editor…** writes both into the project dir. Covered by `EpisodeTests` |
| P1 Plugin model | ✅ DONE | `Sed.Plugins` contract + `PluginHost`; Plugins menu. Covered by `PluginHostTests` |

---

## Known weak spots worth fixing opportunistically

- **Thin test coverage for `Sed.App`, none for `Sed.Rendering.Vulkan`.**
  `tools/Sed.UiProbe` now drives `MapView`'s pointer path headlessly — extend it
  rather than adding new untested interaction code. `MapView`'s hit-testing and
  screen↔world projection are still pure math that would be better in `Sed.Core`.
- **`GobWriter` has a single test.** Thin for a format writer — add odd-size,
  many-entry and long-name cases.
- **Only the active sector's vertices are drawn** in the 2D map view.
- **No measurement / coordinate readout** in `MapView`.
