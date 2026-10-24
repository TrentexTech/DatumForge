# Architecture

DatumForge uses a small layered architecture so UI evolution does not define the file format.

## Dependency direction

```text
DatumForge.App ───────────────┐
       │                      │
       ▼                      ▼
DatumForge.Infrastructure → DatumForge.Core

DatumForge.Tests → Core + Infrastructure
```

`Core` contains drawing entities, layers, layouts, project settings, geometry hit testing, transform math, the per-drawing edit journal, and the policy used when a drawing is opened outside its project. It has no UI or storage dependencies.

`Infrastructure` serializes versioned JSON documents, resolves drawing references only inside the project root, writes through a temporary file before replacement, and snapshots the previous revision before each save.

`App` uses MVVM for workspace state. `DrawingViewport` is the rendering and pointer-input boundary; it transforms model coordinates into device coordinates, collects transform reference points, and renders only visible layers. Geometry mutation and reversible edit state remain in `Core` rather than accumulating in the control.

## Important boundaries

- A project owns layout configuration and relative drawing references.
- A drawing owns geometry and layers and records its associated project ID.
- Opening a drawing alone never silently claims or rewrites project context.
- File-format changes require compatibility tests and documentation.
- History snapshots are recovery aids, not source control and not a substitute for backups.
