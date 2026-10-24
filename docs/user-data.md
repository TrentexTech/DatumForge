# User data

DatumForge keeps application-wide state separate from project and drawing files. The app creates a `DatumForge` directory below the operating system's per-user local application-data folder and writes `settings.json` there.

On Windows the default location is:

```text
%LOCALAPPDATA%\DatumForge\settings.json
```

The settings document is versioned JSON and currently stores:

- the selected application theme and recent-item limit;
- the last normal window size and whether the window was maximized;
- remappable keyboard shortcut assignments;
- recently opened projects and standalone drawings, including their paths, display names, and last-opened timestamps.
- per-project, per-layout viewport pan and zoom positions.

Open the gear button in the DatumForge title bar and choose **Keybinds** to remap a command, clear an assignment, or restore the defaults. Saved changes take effect immediately and are reused in later sessions.

Settings are written through a temporary file and then atomically replaced. If the file is malformed or uses an unsupported newer format, DatumForge preserves it as `settings.invalid-<timestamp>.json` and starts with safe defaults.

Project content and project/drawing history are not stored here. They remain in the project folder so the project stays portable and its revision history travels with it.
