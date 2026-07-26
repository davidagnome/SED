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
- Add `AddSection(ranges, src, "SECTION", GenerateXxx(level))` in
  `JklWriter.Build` (`src/Sed.Formats/Jkl/JklWriter.cs:24`).
- Write a `GenerateXxx` that returns `List<string>` of section lines.

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
  S1 SelectionSet    L1 LightCalculator    U1 HeaderEditor
  U2 LayerPanel      U3 TemplateEditor     F1 SaveJklGob
  G1 Bridge          T1 TexFlags

WAVE 1:
  S2 BoxSelect (needs S1)      S3 CopyPaste (needs S1)
  Q1 FindDialogs (needs S1 for multi-result selection)
  U4 CogEditor (needs U3)

WAVE 2:
  F2 SaveAndTest (needs F1)    I1 DfImport      I2 ThreeDoExport
  X1 UxParity

WAVE 3:
  P1 PluginModel (last)
```

---

## TRACK S — Selection model (the biggest practical gap)

### S1 — SelectionSet
- **Deps**: none
- **Delphi ref**: `U_MULTISEL.PAS`
- **New file**: `src/Sed.Core/Editing/SelectionSet.cs`.
- Holds sets of vertices / things / surfaces / sectors with add / remove / toggle /
  clear and a `Changed` event. One instance shared by `MainWindow`, `VulkanView`
  and `MapView` (as `EditHistory` already is).
- Replace the single `_selectedThing` / `_selectedSurface` / `_selectedVertex`
  fields in `VulkanView` with reads off the set, keeping a "primary" for the
  inspector.
- Ctrl+click adds to the selection; plain click replaces it.
- **Test**: set algebra + that `TransformVerticesCommand` accepts the set.

### S2 — Box-select
- **Deps**: S1
- Rubber-band rectangle in `MapView` (drag on empty space); populate the
  `SelectionSet` with everything inside, respecting the active `EditMode`.
- Also render a selection highlight for **all** selected items, not just the
  primary — `SceneBuilder.BuildEdgeHighlight` currently draws one surface.

### S3 — Copy / paste
- **Deps**: S1
- **Delphi ref**: `U_COPYPASTE.PAS`
- Serialize the selected fragment (sectors + their surfaces/vertices, things,
  lights) into an in-memory clipboard; paste-in-place with an offset; undoable
  via a single composite `IEditCommand`.
- Remember that JK shares world vertices across sectors — paste must create new
  vertices, not alias the originals.

---

## TRACK L — Lighting

### L1 — Lighting calculation
- **Deps**: none (LIGHTS already parses + writes)
- **Delphi ref**: `LEV_UTILS.PAS`, `Calculate &Lighting`
- **New file**: `src/Sed.Core/Lighting/LightCalculator.cs`.
- Point light → per-vertex intensity with quadratic falloff, respecting sector
  ambient and extra light; optional shadow ray-cast against surfaces.
- Writes into `Surface.Corner.Intensity`, so it persists through the existing
  GEORESOURCE regeneration with no writer changes.
- Wire to **Tools ▸ Calculate Lighting**; must be a single undoable command over
  the whole level.
- **Test**: a light at a known distance produces the expected falloff; undo
  restores every original intensity.

---

## TRACK U — Data editors whose formats are already done

Each of these has a complete parse + faithful write already; the work is purely
UI plus the field commands.

### U1 — Header editor
- **Deps**: none · **Delphi ref**: `U_LHEADER.PAS`
- **New file**: `src/Sed.App/HeaderEditorWindow.cs`, opened from **Tools**.
- Typed editors for every `LevelHeader` field (gravity, ceiling/horizon sky,
  mipmap/LOD distances, perspective/gouraud distance, fog). Each edit is an
  `IEditCommand` (add `HeaderFieldCommands.cs` alongside the other field commands).

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

### Q1 — Find dialogs
- **Deps**: S1 (so a search can select many results)
- **Delphi ref**: `Q_SECTORS.PAS`, `Q_SURFS.PAS`, `Q_THINGS.PAS`, `Q_UTILS.PAS`
- **New file**: `src/Sed.App/FindDialog.cs`.
- Query sectors / surfaces / things by index, name, template, material or flag
  mask; results list; jump-to-and-frame the camera on the picked result.
- `ConsistencyWindow` already demonstrates the results-list-plus-jump pattern —
  follow it.

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
| F1 Save JKL+GOB | none | `GobWriter` exists; add **File ▸ Save JKL + GOB** |
| F2 Save and test | F1 | Launch the game with the level (macOS: user-configured Wine/CrossOver command) |
| I1 DF import | none | `U_DFI.PAS` / `DF_IMPORT.INC` |
| I2 3DO export | none | Export a sector as `.3do`; ASC/LEV import |
| I3 3DO tooling | none | Hierarchy viewer, standalone preview (`U_3DOS`, `U_3DOFORM`, `U_3DOPREV`) |
| X1 UX parity | none | Configurable keybindings, recent files, autosave/backup, crash recovery (`U_OPTIONS.PAS`) |
| P1 Plugin model | none | Managed `Sed.Plugins` contract via `AssemblyLoadContext` (replaces the COM/DLL host; last) |

---

## Known weak spots worth fixing opportunistically

- **No test coverage for `Sed.App` or `Sed.Rendering.Vulkan`.** Vulkan is
  genuinely hard to unit-test, but `MapView`'s hit-testing and screen↔world
  projection are pure math and should be extracted into `Sed.Core` and tested.
- **`GobWriter` has a single test.** Thin for a format writer — add odd-size,
  many-entry and long-name cases.
- **Only the active sector's vertices are drawn** in the 2D map view.
- **No measurement / coordinate readout** in `MapView`.
