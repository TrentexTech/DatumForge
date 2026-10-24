using System.Text.Json.Serialization;

namespace DatumForge.Infrastructure.Persistence;

public sealed class UserSettings
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public UserPreferences Preferences { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
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
        RecentProjects ??= [];
        RecentDrawings ??= [];
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
