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
  U2 LayerPanel               U3 TemplateEditor
  G1 Bridge                   T1 TexFlags

WAVE 1:
  S2 BoxSelect ✅ DONE    S3 CopyPaste ✅ DONE    L2 LightEntities ✅ DONE
  Q1 FindDialogs ✅ DONE  U4 CogEditor (needs U3)

WAVE 2:
  F2 SaveAndTest (needs F1)    I1 DfImport      I2 ThreeDoExport
  X1 UxParity

WAVE 3:
  P1 PluginModel (last)
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

### U2 — Layer panel
- **Deps**: none · **Delphi ref**: layers in `JED_MAIN`
- Left-panel list of `Level.Layers` with per-layer **visibility** toggles;
  filter what `SceneAssembler` and `MapView` draw by layer.
- Assignment already exists on the inspectors (`SetSectorLayerCommand` etc.).

### U3 — Template editor
- **Deps**: none · **Delphi ref**: `U_TEMPLATES.PAS`, `U_TPLCREATE.PAS`
- List `Level.Templates`, edit values, add/clone/delete a template.
- Needs a typed param model rather than the current string map.

### U4 — COG editor
- **Deps**: U3 · **Delphi ref**: `U_COGFORM.PAS`, `U_COGGEN.PAS`, `U_CSCENE.PAS`
- Placed-cog symbol-value editor; COG generator; cutscene helper.

---

## TRACK Q — Find / navigate

### Q1 — Find dialogs ✅ DONE
`Sed.Core.Query.LevelQuery` + `src/Sed.App/FindWindow.cs` (**Ctrl+Shift+F**).
Searches sectors / surfaces / things / lights by index, material, name, template,
colormap/sound or flag mask; click a result to select and frame it
(`VulkanView.JumpTo`); "Select all matches" feeds every hit into the selection.
Covered by `LevelQueryTests` and both probes.

**Remaining**: the original offers a per-field query builder (a comparison
operator per field — material, adjoin sector/surface, each flag word). The
current dialog is free text plus one flag mask; extend `FindQuery` if the
finer-grained form is wanted.

---

## TRACK G / T — Remaining geometry & texturing

### G1 — Bridge / connect sectors
- **Deps**: none (cleave + adjoin already exist)
- **Delphi ref**: `LEV_UTILS.PAS`
- Composite command: cleave two facing surfaces by each other, then adjoin the
  overlapping results.

### T1 — Texture flag support
- **Deps**: none
- `SF_DoubleRes` / `SF_HalfRes` and `FF_TexClampX/Y` must affect how the renderer
  samples; add align-from-adjoin (stitch a surface's UVs to its neighbour's).

---

## TRACK F / I / X / P — Infrastructure

| Task | Deps | Summary |
|------|------|---------|
| F1 Save JKL+GOB | ✅ DONE | **File ▸ Save as GOB…** writes `jkl\<name>.jkl` into a GOB v2 archive |
| F2 Save and test | F1 | Launch the game with the level (macOS: user-configured Wine/CrossOver command) |
| I1 DF import | none | `U_DFI.PAS` / `DF_IMPORT.INC` |
| I2 3DO export | none | Export a sector as `.3do`; ASC/LEV import |
| I3 3DO tooling | none | Hierarchy viewer, standalone preview (`U_3DOS`, `U_3DOFORM`, `U_3DOPREV`) |
| X1 UX parity | none | Configurable keybindings, recent files, autosave/backup, crash recovery (`U_OPTIONS.PAS`) |
| P1 Plugin model | none | Managed `Sed.Plugins` contract via `AssemblyLoadContext` (replaces the COM/DLL host; last) |

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
