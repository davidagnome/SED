# SED .NET — Full Parity Execution Plan

A task-by-task breakdown for reaching feature parity with the original Delphi/VCL
editor. Each task is self-contained: any agent can pick it up and implement it.
Tasks are grouped into waves of decreasing parallelism.

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

### JKL section parse pattern
- Add a `case "SECTION": LoadXxx(r, level); break;` in `JklParser.Parse`
  (`src/Sed.Formats/Jkl/JklParser.cs:49`).
- Use `JklReader.Next()`, `Tokens()`, `StripIndex()`, `ParseFloat/Int/Hex`.

### JKL section write pattern
- Add `AddSection(ranges, src, "SECTION", GenerateXxx(level))` in
  `JklWriter.Build` (`src/Sed.Formats/Jkl/JklWriter.cs:22`).
- Write a `GenerateXxx` that returns `List<string>` of section lines.

### Test pattern
- `[Fact]` in `tests/Sed.Core.Tests/`.
- Round-trip: `JklParser.ParseDocument(jkl)` → mutate → `JklWriter.Build(doc)` →
  `JklParser.Parse(output)` → assert.
- Unit: construct model objects directly, apply/revert commands, assert.

### Build / test
```
dotnet build dotnet/Sed.slnx
dotnet test  dotnet/tests/Sed.Core.Tests
```

### Key file map
| Concern        | File                                          |
|----------------|-----------------------------------------------|
| Parser         | `src/Sed.Formats/Jkl/JklParser.cs`            |
| Low-level read | `src/Sed.Formats/Jkl/JklReader.cs`            |
| Parse tables   | `src/Sed.Formats/Jkl/GeoData.cs`              |
| Round-trip doc | `src/Sed.Formats/Jkl/JklDocument.cs`          |
| Writers        | `src/Sed.Formats/Jkl/JklWriter.cs` + `GeoResourceWriter.cs` |
| Commands       | `src/Sed.Core/Editing/*.cs`                   |
| Domain model   | `src/Sed.Core/Model/*.cs`                     |
| 3D viewport    | `src/Sed.App/VulkanView.cs`                   |
| 2D map view    | `src/Sed.App/MapView.cs`                      |
| Shell/menus    | `src/Sed.App/MainWindow.cs`                   |
| Tests          | `tests/Sed.Core.Tests/*.cs`                   |

---

## Dependency graph

```
WAVE 0 (fully parallel — no inter-dependencies):
  A1 LIGHTS      A2 COGS       A3 TEMPLATES-write
  A4 HEADER      A5 LAYERS     A6 GOB-writer
  B1 MapView-edit   C1 Adjoin   C4 Flip   D1 UV-commands   E4 Consistency

WAVE 1 (after B1):
  B2 Snap-grid    B3 Multi-select

WAVE 2 (after Wave-0 + B3):
  B4 Copy/paste   C2 Extrude   C3 Cleave/split
  C5 Bridge (needs C1+C3)   E1 Lighting (needs A1)
  E2 Property-inspector

WAVE 3:
  E3 Find-dialogs   E5 Template/COG-editor (needs A2,A3)
  E6 Header-editor (needs A4)   E7 Layer-UI (needs A5)

WAVE 4:
  F1 GOB-test-launch (needs A6)   F2 Import/export   F3 3DO-tooling   F4 UX-parity

WAVE 5:
  F5 Plugin-model (last)
```

---

## TRACK B0 — Mode system (preceds E2 inspector; unblocks per-mode inspectors)

### B0 — EditMode + mode toolbar + mode-driven picking + inspector framework
- **Deps**: none
- **Delphi ref**: `src/JED_MAIN.PAS:13-22` (MM_* constants), `ITEM_EDIT.PAS` (Load* methods)
- **Modes**: Sector (`S`), Surface (`F`), Vertex (`V`), Edge (`E`), Thing (`T`), Light (`L`)
- **Infrastructure** (built once, shared):
  - `EditMode` enum in `Sed.Core/Editing`
  - Flag constants: `SF_*`, `FF_*`, `SECF_*`, `SAF_*`, `LF_*` in `Sed.Core/Model`
  - `InspectorPanel` (`Sed.App`) — a `ContentControl` that rebuilds per mode+selection
  - Mode toolbar — row of toggle buttons in `MainWindow` (S/F/V/E/T/L)
  - `VulkanView.Mode` / `MapView.Mode` — picking hit-tests only the active mode's entity
- **Per-mode inspectors** (independent subagents — each creates commands + a panel):
  - `IS-SC` SectorInspector — Flags, Ambient, ExtraLight, Tint, ColorMap, Sound, Layer
  - `IS-SF` SurfaceInspector — SurfFlags, FaceFlags, Material, Geo, Light, Tex, Adjoin, ExtraLight, UScale, VScale
  - `IS-VX` VertexInspector — X, Y, Z
  - `IS-TH` ThingInspector — Template, Name, Sector, X/Y/Z, PYR, Layer, template values
  - `IS-LT` LightInspector — Flags, Range, Intensity, Color, X/Y/Z, Layer

---

## TRACK A — Format parity

### A1 — LIGHTS: parse + write
- **Deps**: none
- **Delphi ref**: `src/LEVEL_IO.INC:659-686`, `src/SAVEJKL.INC:890-912`
- **Format**: header `Editor lights %d`; per light:
  - JK:   `num: flags(hex) layer x y z range intensity`
  - MotS: `num: flags(hex) layer x y z range intensity r g b`
- **Model**: `Light.cs` already has all fields; add `Num`.
- **Parse**: `case "LIGHTS": LoadLights(r, level); break;` in `JklParser`.
- **Write**: `GenerateLights` in `JklWriter` — JK vs MotS by `level.Kind`.
- **Test**: `[Fact] Lights_RoundTrip` in `JklRoundTripTests.cs`.

### A2 — COGS: parse + write
- **Deps**: none
- **Delphi ref**: `src/LEVEL_IO.INC:746-789`, `src/SAVEJKL.INC:758-774`
- **Format**: header `World cogs %d`; per cog: `num:\tname\tval\tval...`
- **Model**: `Cog` in `Light.cs` — change `Values` to ordered `List<string>`.
- **Parse**: `case "COGS": LoadCogs(r, level); break;`
- **Write**: `GenerateCogs` in `JklWriter`.
- **Test**: `[Fact] Cogs_RoundTrip`.

### A3 — TEMPLATES: faithful write
- **Deps**: none
- **Already parsed** at `JklParser.cs:367-385`.
- **Write**: `GenerateTemplates` in `JklWriter`. Ensure insertion order (use
  `level.Templates` in declaration order).
- **Test**: `[Fact] Templates_RoundTrip` — modify a value, save, re-parse.

### A4 — HEADER: full read + write
- **Deps**: none
- **Delphi ref**: `src/LEVEL_IO.INC:791-833`, `src/SAVEJKL.INC:132-157`
- **Model**: `LevelHeader.cs` already has all fields.
- **Parse**: extend `LoadHeader` (`JklParser.cs:92`) for all fields (CEILING SKY
  Z/OFFSET, HORIZON DISTANCE/PIXELS PER REV/OFFSET, MIPMAP/LOD DISTANCES,
  PERSPECTIVE/GOURAUD DISTANCE, FOG).
- **Write**: `GenerateHeader` in `JklWriter`.
- **Test**: `[Fact] Header_RoundTrip`.

### A5 — LAYERS: parse + write
- **Deps**: none
- **Delphi ref**: `src/LEVEL_IO.INC:688-744`, `src/SAVEJKL.INC:914-976`
- **Format**: `Editor layers %d` then per layer: name, sector-indices line,
  thing-indices line.
- **Model**: `Level.Layers` (`List<string>`) + `Sector.Layer`/`Thing.Layer` exist.
- **Parse**: `case "LAYERS": LoadLayers(r, level); break;`
- **Write**: `GenerateLayers` in `JklWriter`.
- **Test**: `[Fact] Layers_RoundTrip`.

### A6 — GOB writer
- **Deps**: none
- **Format**: same GOB v2 the reader expects (see `GobArchive.cs`).
- **New file**: `src/Sed.Formats/Gob/GobWriter.cs`.
- **Test**: `[Fact] WrittenGob_CanBeReadBack` in `GobArchiveTests.cs`.

---

## TRACK B — 2D editing

### B1 — MapView hit-testing + drag + selection sync
- **Deps**: none
- **File**: `src/Sed.App/MapView.cs` (scaffolding declared but unimplemented).
- Implement `HitTestVertex/Thing/Surface` (inverse-project screen→world).
- `DragMode.Object`: on press-hit, capture object; on move, compute delta; on
  release, `History.Do(MoveVertex/Thing/Surface)`.
- Share `EditHistory` between `MapView` and `VulkanView` (set in `MainWindow`).
- Render selection highlight in 2D.

### B2 — Snap-to-grid
- **Deps**: B1
- Round drag target to `_gridStep`; toggle with **G**.

### B3 — Multi-select + box-select
- **Deps**: B1
- **New file**: `src/Sed.Core/Editing/SelectionSet.cs`.
- Rubber-band rect in `MapView`; populate selection set.
- Multi-vertex moves via `TransformVerticesCommand`.

### B4 — Copy/paste
- **Deps**: B3
- Serialize model fragment (sectors/things/lights) to in-memory clipboard;
  paste-in-place with offset; undoable.

---

## TRACK C — Geometry commands

### C1 — Adjoin make/remove
- **Deps**: none
- **Delphi ref**: `LEV_UTILS.PAS:1400` (SysAdjoinSurfaces), `3994` (MakeAdjoin).
- **New file**: `AdjoinCommand.cs` — set/clear `Surface.Adjoin` mirror pair.
- Writer already emits adjoin mirror-pairs.
- **Test**: apply/revert + round-trip adjoin count.

### C2 — Extrude surface
- **Deps**: none
- **Delphi ref**: `LEV_UTILS.PAS:1533` (ExtrudeSurface).
- **New file**: `ExtrudeCommand.cs` — create new sector with cloned verts offset
  along normal + side surfaces + adjoin.

### C3 — Cleave/split
- **Deps**: none (C1 helps)
- **Delphi ref**: `LEV_UTILS.PAS:1627-2094`.
- **New file**: `CleaveCommand.cs` + `GeometryOps.cs` math helpers.
- Classify vertices by plane; insert edge intersections; split surface/sector.

### C4 — Flip surface
- **Deps**: none
- **Delphi ref**: `LEV_UTILS.PAS:5334`.
- **New file**: `FlipSurfaceCommand.cs` — reverse `Corners`, `RecalcNormal`.

### C5 — Bridge/connect
- **Deps**: C1, C3
- Composite: cleave two surfaces by each other, then adjoin overlapping results.

---

## TRACK D — Texture mapping

### D1 — UV transform commands
- **Deps**: none
- **New file**: `TextureCommands.cs` — Shift/Scale/Rotate/AutoTexture.
- Each captures old UVs; persists via existing GEORESOURCE writer.
- **Test**: shift/scale/rotate round-trip.

---

## TRACK E — Feature capabilities

### E1 — Lighting calculation
- **Deps**: A1
- **New file**: `src/Sed.Core/Lighting/LightCalculator.cs`.
- Point-light → per-vertex intensity with quadratic falloff + shadow ray-cast.

### E2 — Property inspector
- **New file**: `src/Sed.App/InspectorPanel.cs`.
- Typed editors for sector/surface/thing/vertex/light fields → IEditCommands.

### E3 — Find/query dialogs
- **New file**: `src/Sed.App/FindDialog.cs`.

### E4 — Consistency checker
- **New file**: `src/Sed.Core/Validation/ConsistencyChecker.cs`.
- Checks: surface/vertex counts, convexity, adjoin validity, planarity, normals.

### E5 — Template/COG editor (needs A2, A3)
### E6 — Header editor UI (needs A4)
### E7 — Layer UI (needs A5)

---

## TRACK F — Infrastructure

| Task | Deps | Summary |
|------|------|---------|
| F1 GOB test-launch | A6 | Save JKL+GOB, launch game |
| F2 Import/export | varies | DF import, 3DO export, ASC/LEV |
| F3 3DO tooling | none | Hierarchy viewer, standalone preview |
| F4 UX parity | none | Keybindings, recent files, autosave |
| F5 Plugin model | none | Managed plugin contract (last) |
