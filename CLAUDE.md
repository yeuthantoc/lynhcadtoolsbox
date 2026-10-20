# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

AutoCAD .NET plugin (C#) that draws Australian reinforced-concrete manhole details: plan, section A-A, roof slab reinforcement, bar schedule (AS/NZS 4671 D500N) and notes. User-facing text, comments and README are in Vietnamese; drawing text is English.

## Git workflow

- Never open a pull request. Work, commit and push directly on `main`.
- If a session starts on another branch, bring the work onto `main` (fast-forward or merge) and push `main`.

## Commands

```bash
# Core checks (the only test suite: assertion-style checks in tools/LynhCad.Manhole.Preview/Program.cs, class Checks)
dotnet run --project tools/LynhCad.Manhole.Preview -- check

# SVG preview of a parameter file (no AutoCAD needed) - use this to eyeball drawing changes
dotnet run --project tools/LynhCad.Manhole.Preview -- svg samples/rect-mh01.json out.svg

# Rewrite the golden SVGs in samples/snapshots (checked by `check`; every samples/*.json is rendered).
# Run after an intended drawing change and review the SVG diff before committing.
dotnet run --project tools/LynhCad.Manhole.Preview -- snapshot

# Regenerate the two sample JSON + SVG files
dotnet run --project tools/LynhCad.Manhole.Preview -- sample <folder>
```

There is no per-test filter; to run one area, temporarily call only that method (`Geometry`, `RectangularSample`, `CircularSample`, `Validation`, `Json`, `Naming`, `Snapshot`) from `Checks.Run`. `Snapshot` compares text exactly and numbers within 0.015, so a pure refactor must leave the snapshots passing unchanged.

Plugin build (Windows only, WPF + net48):

```powershell
.\build.ps1                 # Release build + bundle in .\dist
.\build.ps1 -Acad 2023      # build against a specific AutoCAD API (2021/2022/2023/2024 -> AutoCAD.NET 24.0.0/24.1.51000/24.2.0/24.3.0)
.\build.ps1 -Install        # also install the autoloader bundle to %APPDATA%\Autodesk\ApplicationPlugins
.\build.ps1 -AcadDir "C:\Program Files\Autodesk\AutoCAD 2024"   # reference AutoCAD's own DLLs instead of NuGet
```

`.github/workflows/build.yml` builds the bundle on `windows-latest`, uploads it as an artifact and, on `main`, replaces `LynhCadToolsBox-AutoCAD2023.zip` on the `latest` GitHub release. `install-acad2023.ps1` (end-user installer, saved as UTF-8 with BOM + CRLF so Windows PowerShell 5.1 reads the Vietnamese text) downloads that zip and copies the bundle to `%APPDATA%\Autodesk\ApplicationPlugins`. The user's machine runs PowerShell in Constrained Language Mode and has an 8.3 short `%TEMP%` path: in that script use cmdlets only (no `[Type]::Method()` calls or `New-Object` of .NET types, except inside `try { } catch { }` as optional extras) and work under `%LOCALAPPDATA%`. The plugin project does not build on Linux (no WPF SDK); `src/LynhCad.Loader` does build on Linux; to type-check the plugin, compile `src/LynhCad.Manhole.Plugin/*.cs` (all files except `UI/`) together with Core in a throwaway net48 project that references `AutoCAD.NET` and stubs `ManholeWindow`.

## Architecture

Two projects with a hard split:

- **`LynhCad.Manhole.Core`** (netstandard2.0, C# 7.3, must NOT reference AutoCAD). All engineering and drawing logic.
- **`LynhCad.Manhole.Plugin`** (net48, x64, WPF). Thin AutoCAD layer: commands, dialog, and translation of primitives to entities.

Pipeline: `ManholeParams` → `ManholeGeometry` (derived dimensions + validation errors/warnings) → `RebarDesigner.Design` (bar layout + schedule with merged, numbered marks W/B/R/T) → `PlanBuilder` / `SectionBuilder` / `RoofPlanBuilder` / schedule → `DetailComposer.Compose` lays the views out into one `DrawingModel` of intermediate primitives (`Drawing/Primitives.cs`: PPolyline, PHatch, PText, PDim, PLeader, PTable, ...). Both `SvgExporter` (preview) and `AcadRenderer` (AutoCAD entities) consume that same model, so a drawing change is made once in Core and verified via SVG.

Key conventions:
- Units: mm for sizes, m (AHD) for levels. Drawn 1:1 in model space; text/dim sizes come from `DrawStyle(scale)` (default 1:20).
- Composed model origin (0,0) = manhole centre on plan; the plugin translates it to the picked point.
- Section A-A is always cut along the outlet pipe axis (outlet on the right).
- Rectangular pipes are placed by `wall` + `offset`; circular pipes by `angle` (0° = +X, CCW).
- JSON (`ParamsJson`, DataContract) is the parameter format for the dialog's Load/Save, `LCT_MANHOLE_JSON` (single object or array), and the last-used settings in `%APPDATA%\LynhCadToolsBox\manhole-last.json`. Missing fields take defaults, so new `[DataMember]` fields must have sensible defaults.

Plugin specifics:
- AutoCAD loads only `src/LynhCad.Loader` (`LynhCad.Loader.dll`). It registers the fixed `[CommandMethod]`s and forwards each to `LynhCad.Manhole.Plugin.Entry.Run(name)` via reflection on a copy of `LynhCad.Manhole.dll` + `LynhCad.Manhole.Core.dll` loaded with `Assembly.Load(byte[])` (files stay unlocked; `AssemblyResolve` maps each plugin generation to its Core). Before every command it reloads if the DLLs on disk are newer; `LCT_RELOAD` forces it. DLL folder = `LYNHCAD_PLUGIN_DIR` env var or the loader's folder.
- Consequences: the plugin must not declare `[CommandMethod]`/`ExtensionApplication` or reference the loader, and the loader must not reference the plugin/Core. Adding a command = case in `Entry.Run` (`Entry.cs`) + `[CommandMethod]` in `Loader.cs` + entry in `deploy/LynhCadToolsBox.bundle/PackageContents.xml`. Keep the loader minimal: changing it needs an AutoCAD restart.
- The plugin gets a unique `AssemblyVersion` per build (csproj `BuildDay`/`BuildRev`); WPF caches XAML by assembly name+version, so without it a reload would show the old dialog. CI fails if the generated `ManholeWindow.g.cs` URI lacks the version.
- Commands: `LCT_MANHOLE` (dialog), `LCT_MANHOLE_JSON` (batch from file), `LCT_MANHOLE_EDIT` (update a drawn detail), `LCT_RELOAD`.
- Each detail is a named **Group** `LCT_<id>` (not a block); naming and Xrecord chunking rules live in Core `DetailNaming` so `check` covers them. `DetailData` (`DetailData.cs`) stores the params JSON (chunked strings), the centre point and an anchor curve in an Xrecord on the group's extension dictionary; `LCT_MANHOLE_EDIT` reads it, offsets by how far the anchor moved, erases the group's entities and redraws in place.
- `AcadSetup.EnsureArrow` sets DIMBLK and must run outside a transaction. Layers (`Drawing/Layers.cs`, `S-*`), text style `LCT_ROMANS` and dim style are created only if missing; existing user-edited layers are kept.
- `AcadRenderer.Add` re-applies text/dim styles after `SetDatabaseDefaults`; hatch `PatternScale`/`PatternAngle` must be set before `SetHatchPattern`. A failing primitive is skipped and reported ("Bỏ qua ...") rather than aborting the detail.

The tool does not do structural design: bar sizes/spacings are user inputs; output needs engineer review.
