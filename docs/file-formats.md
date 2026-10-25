# DatumForge file formats

DatumForge currently writes two UTF-8 JSON formats.

## `.dfproj`

The project manifest contains a stable project ID, settings and arbitrary properties, layout definitions, and relative references to drawings. Each layout stores its viewport background, grid visibility/spacing, orthographic and polar-tracking state, polar-angle increment, object-snap state, enabled object-snap modes, and an ordered list of viewport modules with their resized widths. Transient pan and zoom positions are deliberately excluded and stored in the user's local DatumForge settings instead. Drawing paths must resolve inside the directory containing the project file.

## `.dfdraw`

A drawing contains its stable ID, optional owning project ID, units, layers, and geometry entities. Entities refer to layers by ID. The initial entity schema supports line, polyline, circle, arc, rectangle, text, and dimension kinds using points plus optional radius/text fields. An arc stores three ordered points: start, a point on the arc, and end. A rectangle stores its four ordered corners so rotation and reflection preserve its shape.

## Compatibility and history

Each document has a numeric `formatVersion`. The application reads its current or older versions and rejects newer versions to avoid silent data loss. Newly introduced optional layout fields use safe defaults when loading files created before those fields existed.

Drawing format version 2 adds the three-point `arc` entity kind. Version-1 drawings remain readable and are upgraded when next saved.

Drawing format version 3 expands rectangles from two opposite corners to four ordered corners. Version-1 and version-2 rectangles remain readable and are expanded when transformed or next saved.

Before overwriting an existing document, DatumForge copies the previous revision into `.datumforge/history/project` or `.datumforge/history/drawing` and appends metadata to `index.json`. The project setting `historyLimit` limits retained snapshot files per document.

The format is pre-1.0 and may change. Do not build long-lived integrations without pinning a DatumForge release and validating `formatVersion`.
