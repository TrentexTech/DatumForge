# Editing workflow

DatumForge keeps viewport interaction separate from drawing mutations. The viewport collects selection and reference points; `DrawingEditJournal` applies each mutation to the active `DrawingDocument` and records enough state to reverse it.

## View navigation

- Drag with the **middle mouse button** to pan without changing the active drawing or modify tool.
- Scroll the **mouse wheel** up to zoom in and down to zoom out.
- Wheel zoom stays anchored to the drawing point beneath the pointer.
- Pan and zoom are remembered per project and layout in the user's local DatumForge settings; they are not written to the portable project file.

## Selection

- Click an entity with **Select** to replace the current selection.
- Hold **Ctrl** or **Shift** while clicking to toggle entities in the selection set.
- Selected entities use a cyan highlight with white endpoint grips.
- Hidden-layer entities are not selectable. Locked-layer entities may be selected for inspection but are excluded from editing operations.
- Press **Escape** to cancel the active reference point and clear selection.

## Modify tools

Select entities before choosing a modify tool. Object snapping is evaluated before grid snapping for pointer input.

- **Move**: choose a base point and then its target point.
- **Copy**: choose a base point and then the target for the copied entities. Copies retain geometry and layer ownership but receive new entity IDs.
- **Rotate**: the selection bounds center is the pivot. Choose a reference point and then a target point to define the angle.
- **Scale**: the selection bounds center is the origin. Choose a reference point and then a target point to define the distance ratio.
- **Erase**: click the toolbar action or press **Delete** or **Backspace**.

Degenerate operations, such as a zero-distance move or a rotation reference at the pivot, do not create journal entries.

## Command and coordinate entry

The narrow command bar below the viewport accepts tool names and precise points. Press **Enter** or click the arrow button to submit an entry.

- Absolute Cartesian point: `100,50`
- Relative Cartesian point: `@25,-10`
- Relative polar point: `@50<30` (distance 50 at 30 degrees)

Relative points use the active tool's first point as their origin. Typed coordinates are exact and therefore do not pass through grid or object snapping.

Commands include `select` (`sel`), `line` (`l`), `polyline` (`pl`), `circle` (`c`), `rectangle` (`rect`), `text` (`t`), `dimension` (`dim`), `move` (`m`), `copy` (`co`), `rotate` (`ro`), `scale` (`sc`), `erase` (`e`), `undo` (`u`), `redo`, `grid`, `snap`, and `osnap`.

## Object snaps

Object snapping is stored per layout and can be toggled independently from the grid. The property panel enables endpoint, midpoint, center, and line-segment intersection modes separately. Hidden-layer geometry is ignored.

Snap markers identify the candidate before or during point input: square for endpoint, diamond for midpoint, circle for center, and cross for intersection. When both systems are enabled, a nearby object snap wins; otherwise the point falls back to the layout grid.

## Undo and redo

Creation, erase, move, copy, rotate, and scale are journaled independently per drawing. Use **Ctrl+Z** to undo and **Ctrl+Y** or **Ctrl+Shift+Z** to redo while the viewport has focus. A new edit clears that drawing's redo stack.

The in-memory edit journal is distinct from saved project history. Saving still creates durable project and drawing snapshots under `.datumforge/history`.
