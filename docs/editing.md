# Editing workflow

DatumForge keeps viewport interaction separate from drawing mutations. The viewport collects selection and reference points; `DrawingEditJournal` applies each mutation to the active `DrawingDocument` and records enough state to reverse it.

## Selection

- Click an entity with **Select** to replace the current selection.
- Hold **Ctrl** or **Shift** while clicking to toggle entities in the selection set.
- Selected entities use a cyan highlight with white endpoint grips.
- Hidden-layer entities are not selectable. Locked-layer entities may be selected for inspection but are excluded from editing operations.
- Press **Escape** to cancel the active reference point and clear selection.

## Modify tools

Select entities before choosing a modify tool. Grid snapping applies to both input points when enabled.

- **Move**: choose a base point and then its target point.
- **Copy**: choose a base point and then the target for the copied entities. Copies retain geometry and layer ownership but receive new entity IDs.
- **Rotate**: the selection bounds center is the pivot. Choose a reference point and then a target point to define the angle.
- **Scale**: the selection bounds center is the origin. Choose a reference point and then a target point to define the distance ratio.
- **Erase**: click the toolbar action or press **Delete** or **Backspace**.

Degenerate operations, such as a zero-distance move or a rotation reference at the pivot, do not create journal entries.

## Undo and redo

Creation, erase, move, copy, rotate, and scale are journaled independently per drawing. Use **Ctrl+Z** to undo and **Ctrl+Y** or **Ctrl+Shift+Z** to redo while the viewport has focus. A new edit clears that drawing's redo stack.

The in-memory edit journal is distinct from saved project history. Saving still creates durable project and drawing snapshots under `.datumforge/history`.
