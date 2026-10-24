using System.Text.Json.Serialization;

namespace DatumForge.Infrastructure.Persistence;

public sealed class UserSettings
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public UserPreferences Preferences { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public Dictionary<string, string> KeyBindings { get; set; } = KeyBindingDefaults.Create();
    public List<RecentFileEntry> RecentProjects { get; set; } = [];
    public List<RecentFileEntry> RecentDrawings { get; set; } = [];

    public void RememberProject(string path, string? displayName = null) =>
        Remember(RecentProjects, path, displayName, Preferences.RecentItemLimit);

    public void RememberDrawing(string path, string? displayName = null) =>
        Remember(RecentDrawings, path, displayName, Preferences.RecentItemLimit);

    public bool ForgetProject(string path) => Forget(RecentProjects, path);

    public bool ForgetDrawing(string path) => Forget(RecentDrawings, path);

    public void Normalize()
    {
        Preferences ??= new UserPreferences();
        Window ??= new WindowSettings();
        KeyBindings ??= [];
        RecentProjects ??= [];
        RecentDrawings ??= [];
        foreach (var binding in KeyBindingDefaults.Values)
            KeyBindings.TryAdd(binding.Key, binding.Value);
        foreach (var key in KeyBindings.Keys.ToArray())
            KeyBindings[key] = KeyBindings[key]?.Trim() ?? string.Empty;
        Preferences.RecentItemLimit = Math.Clamp(Preferences.RecentItemLimit, 1, 50);
        NormalizeRecents(RecentProjects, Preferences.RecentItemLimit);
        NormalizeRecents(RecentDrawings, Preferences.RecentItemLimit);
    }

    private static void Remember(List<RecentFileEntry> entries, string path, string? displayName, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var boundedLimit = Math.Clamp(limit, 1, 50);
        Forget(entries, fullPath);
        entries.Insert(0, new RecentFileEntry
        {
            Path = fullPath,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(fullPath)
                : displayName,
            LastOpenedUtc = DateTimeOffset.UtcNow
        });
        if (entries.Count > boundedLimit)
            entries.RemoveRange(boundedLimit, entries.Count - boundedLimit);
    }

    private static bool Forget(List<RecentFileEntry> entries, string path)
    {
        var fullPath = SafeFullPath(path);
        return entries.RemoveAll(entry => PathComparer.Equals(SafeFullPath(entry.Path), fullPath)) > 0;
    }

    private static void NormalizeRecents(List<RecentFileEntry> entries, int limit)
    {
        var unique = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .OrderByDescending(entry => entry.LastOpenedUtc)
            .GroupBy(entry => SafeFullPath(entry.Path), PathComparer)
            .Select(group => group.First())
            .Take(limit)
            .ToList();
        foreach (var entry in unique)
        {
            entry.Path = SafeFullPath(entry.Path);
            if (string.IsNullOrWhiteSpace(entry.DisplayName))
                entry.DisplayName = Path.GetFileNameWithoutExtension(entry.Path);
        }
        entries.Clear();
        entries.AddRange(unique);
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

public static class KeyBindingDefaults
{
    public const string Save = "save";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string Erase = "erase";
    public const string Select = "tool.select";
    public const string Line = "tool.line";
    public const string Polyline = "tool.polyline";
    public const string Circle = "tool.circle";
    public const string Rectangle = "tool.rectangle";
    public const string Text = "tool.text";
    public const string Dimension = "tool.dimension";
    public const string Move = "tool.move";
    public const string Copy = "tool.copy";
    public const string Rotate = "tool.rotate";
    public const string Scale = "tool.scale";
    public const string ToggleGrid = "view.toggle-grid";
    public const string ToggleGridSnap = "view.toggle-grid-snap";
    public const string ToggleObjectSnap = "view.toggle-object-snap";

    public static IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [Save] = "Ctrl+S",
        [Undo] = "Ctrl+Z",
        [Redo] = "Ctrl+Y",
        [Erase] = "Delete",
        [Select] = string.Empty,
        [Line] = string.Empty,
        [Polyline] = string.Empty,
        [Circle] = string.Empty,
        [Rectangle] = string.Empty,
        [Text] = string.Empty,
        [Dimension] = string.Empty,
        [Move] = string.Empty,
        [Copy] = string.Empty,
        [Rotate] = string.Empty,
        [Scale] = string.Empty,
        [ToggleGrid] = "F7",
        [ToggleGridSnap] = "F9",
        [ToggleObjectSnap] = "F3"
    };

    public static Dictionary<string, string> Create() =>
        new(Values, StringComparer.OrdinalIgnoreCase);
}

public sealed class UserPreferences
{
    public string Theme { get; set; } = "Dark";
    public int RecentItemLimit { get; set; } = 10;
}

public sealed class WindowSettings
{
    public double Width { get; set; } = 1440;
    public double Height { get; set; } = 900;
    public bool IsMaximized { get; set; }
}

public sealed class RecentFileEntry
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset LastOpenedUtc { get; set; }

    [JsonIgnore]
    public string Location => System.IO.Path.GetDirectoryName(Path) ?? Path;
}
