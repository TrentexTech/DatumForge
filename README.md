# DatumForge

DatumForge is an open-source, cross-platform 2D CAD workspace built with C#, .NET 10, and Avalonia 12. It is an early foundation for a project-oriented drawing application—not yet an AutoCAD replacement.

![Build](https://github.com/TrentexTech/DatumForge/actions/workflows/ci.yml/badge.svg)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Current milestone

- CAD-style top ribbon with file actions and drawing tools
- separate tabs for welcome/open projects and open drawings
- interactive 2D viewport with grid, snapping, zoom-aware rendering, and basic line, polyline, circle, rectangle, text, and dimension entities
- per-project layouts with an Excel-like bottom switcher and layout-specific display options
- layers with visibility, locking, color, linetype, lineweight, opacity, printability, and grouping in the file model
- portable `.dfproj` project manifests and `.dfdraw` drawing documents
- automatic project and drawing snapshots under `.datumforge/history`
- explicit warning when a project-associated drawing is opened without its owning project
- tests for serialization, history, version handling, association safety, hit testing, and edit journaling

The geometry editor now includes single and additive selection, endpoint grips, locked-layer-aware erase, two-point move/copy/rotate/scale workflows, and per-drawing undo/redo for every current geometry edit. Constraints, numeric precision input, references, DXF/DWG interoperability, printing, and production-grade CAD kernels remain future work.

Select an entity by clicking it with the Select tool. Hold Ctrl or Shift to toggle entities in the selection set. Delete or Backspace erases the selection; Ctrl+Z undoes and Ctrl+Y or Ctrl+Shift+Z redoes while the viewport has focus.

See [docs/editing.md](docs/editing.md) for transform input and journal behavior.

## Windows builds

Every successful `main` CI run publishes an unsigned, self-contained Windows x64 artifact. The artifact and executable names include the seven-character source commit, for example `DatumForge-win-x64-acc9c42-unsigned` and `DatumForge-acc9c42-win-x64.exe`.

The downloaded ZIP contains only the executable, `START-HERE.txt`, and `LICENSE.txt`. No separate .NET installation is required.

## Run locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet restore DatumForge.slnx
dotnet run --project src/DatumForge.App/DatumForge.App.csproj
```

Run verification:

```powershell
dotnet build DatumForge.slnx --configuration Release --no-restore
dotnet test DatumForge.slnx --configuration Release --no-build
```

## Project folder

```text
My Project/
├── My Project.dfproj
├── drawings/
│   └── Drawing 1.dfdraw
└── .datumforge/
    └── history/
```

Both public file types are versioned, indented JSON. History is local project data and is intentionally kept beside the project rather than inside the Git repository for DatumForge itself. See [docs/file-formats.md](docs/file-formats.md).

## Architecture

- `DatumForge.Core` — file/domain models, geometry hit testing, edit journaling, and project-association policy
- `DatumForge.Infrastructure` — guarded paths, atomic JSON persistence, history snapshots
- `DatumForge.App` — Avalonia shell, view models, and the 2D viewport
- `DatumForge.Tests` — headless domain and persistence tests

See [docs/architecture.md](docs/architecture.md), [docs/roadmap.md](docs/roadmap.md), and [CONTRIBUTING.md](CONTRIBUTING.md).

## License

DatumForge is available under the [MIT License](LICENSE). AutoCAD is a trademark of Autodesk, Inc.; DatumForge is independent and is not affiliated with or endorsed by Autodesk.
