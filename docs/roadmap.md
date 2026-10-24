# Roadmap

## Foundation — current

- project/drawing formats, history, layouts, layers, workspace shell
- per-user settings, window state, and recent projects/drawings — complete
- layout-specific viewport modules with resizing, reordering, removal, and restoration — complete
- basic viewport rendering and primitive creation
- cross-platform build and test automation

## Editing core

- single and additive selection, endpoint grips, locked-layer-aware erase — complete
- undo/redo transaction journal for create and erase operations — complete
- move/copy/rotate/scale with two-point viewport input and undo/redo — complete
- command line plus absolute, relative Cartesian, and relative polar point entry — complete
- extend the transaction journal to property edits
- endpoint, midpoint, center, and line-segment intersection object snaps — complete
- orthographic tracking — complete; polar tracking remains
- precision geometry and spatial indexing

## Documentation and interchange

- external references with repairable relative paths
- print layouts, viewports, dimensions, styles, and PDF output
- import/export work beginning with open, documented formats such as DXF/SVG
- drawing recovery and history browser

## Extensibility and production readiness

- plugin API, scripting, and command discovery
- large-drawing performance benchmarks
- accessibility, localization, signing, installers, and crash recovery

Priorities will be tracked in GitHub issues once the public repository is established.
