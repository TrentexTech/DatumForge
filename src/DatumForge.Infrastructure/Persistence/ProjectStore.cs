using System.Text.Json;
using System.Text.Json.Serialization;
using DatumForge.Core.Models;

namespace DatumForge.Infrastructure.Persistence;

public sealed class ProjectStore
{
    public const string ProjectExtension = ".dfproj";
    public const string DrawingExtension = ".dfdraw";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ProjectWorkspace> CreateAsync(string directory, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "drawings"));

        var drawing = new DrawingDocument { Name = "Drawing 1", Layers = [new DrawingLayer()] };
        var project = new ProjectDocument { Name = name };
        drawing.ProjectId = project.Id;
        project.Drawings.Add(new DrawingReference
        {
            DrawingId = drawing.Id,
            Name = drawing.Name,
            RelativePath = $"drawings/{SafeFileName(drawing.Name)}{DrawingExtension}"
        });
        project.Layouts.Add(new LayoutDefinition { Name = "Model", ActiveDrawingId = drawing.Id });

        var projectPath = Path.Combine(directory, $"{SafeFileName(name)}{ProjectExtension}");
        await SaveDrawingCoreAsync(directory, project, drawing, false, cancellationToken);
        await SaveProjectCoreAsync(projectPath, project, false, cancellationToken);
        return new ProjectWorkspace(projectPath, project, new Dictionary<Guid, DrawingDocument> { [drawing.Id] = drawing });
    }

    public async Task<ProjectWorkspace> LoadProjectAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        var project = await ReadAsync<ProjectDocument>(projectFilePath, cancellationToken);
        ValidateVersion(project.FormatVersion, ProjectDocument.CurrentFormatVersion, "project");
        foreach (var layout in project.Layouts) layout.NormalizeViewportModules();
        var root = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;
        var drawings = new Dictionary<Guid, DrawingDocument>();
        foreach (var reference in project.Drawings)
        {
            var drawingPath = ResolveInsideRoot(root, reference.RelativePath);
            if (!File.Exists(drawingPath)) continue;
            var drawing = await LoadDrawingAsync(drawingPath, cancellationToken);
            drawings[drawing.Id] = drawing;
        }

        return new ProjectWorkspace(Path.GetFullPath(projectFilePath), project, drawings);
    }

    public async Task<DrawingDocument> LoadDrawingAsync(string drawingFilePath, CancellationToken cancellationToken = default)
    {
        var drawing = await ReadAsync<DrawingDocument>(drawingFilePath, cancellationToken);
        ValidateVersion(drawing.FormatVersion, DrawingDocument.CurrentFormatVersion, "drawing");
        return drawing;
    }

    public async Task SaveAsync(ProjectWorkspace workspace, CancellationToken cancellationToken = default)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(workspace.ProjectFilePath))!;
        foreach (var drawing in workspace.Drawings.Values)
            await SaveDrawingCoreAsync(root, workspace.Project, drawing, workspace.Project.Settings.CreateHistoryOnSave, cancellationToken);
        await SaveProjectCoreAsync(workspace.ProjectFilePath, workspace.Project, workspace.Project.Settings.CreateHistoryOnSave, cancellationToken);
        TrimHistory(root, workspace.Project.Settings.HistoryLimit);
    }

    public async Task SaveDrawingAsync(string drawingFilePath, DrawingDocument drawing, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(drawingFilePath);
        ArgumentNullException.ThrowIfNull(drawing);
        drawing.FormatVersion = DrawingDocument.CurrentFormatVersion;
        drawing.Revision++;
        drawing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await WriteAtomicAsync(Path.GetFullPath(drawingFilePath), drawing, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryEntry>> ReadHistoryAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;
        var indexPath = Path.Combine(root, ".datumforge", "history", "index.json");
        return File.Exists(indexPath) ? await ReadAsync<List<HistoryEntry>>(indexPath, cancellationToken) : [];
    }

    private static async Task SaveProjectCoreAsync(string path, ProjectDocument project, bool createHistory, CancellationToken cancellationToken)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (createHistory && File.Exists(path)) await SnapshotAsync(root, "project", project.Id, project.Revision, path, cancellationToken);
        project.Revision++;
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await WriteAtomicAsync(path, project, cancellationToken);
    }

    private static async Task SaveDrawingCoreAsync(string root, ProjectDocument project, DrawingDocument drawing, bool createHistory, CancellationToken cancellationToken)
    {
        var reference = project.Drawings.Single(x => x.DrawingId == drawing.Id);
        var path = ResolveInsideRoot(root, reference.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (createHistory && File.Exists(path)) await SnapshotAsync(root, "drawing", drawing.Id, drawing.Revision, path, cancellationToken);
        drawing.FormatVersion = DrawingDocument.CurrentFormatVersion;
        drawing.Revision++;
        drawing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await WriteAtomicAsync(path, drawing, cancellationToken);
    }

    private static async Task SnapshotAsync(string root, string scope, Guid id, int revision, string sourcePath, CancellationToken cancellationToken)
    {
        var historyRoot = Path.Combine(root, ".datumforge", "history");
        var scopeDirectory = Path.Combine(historyRoot, scope);
        Directory.CreateDirectory(scopeDirectory);
        var fileName = $"{id:N}.r{revision:D6}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json";
        var destination = Path.Combine(scopeDirectory, fileName);
        File.Copy(sourcePath, destination, false);
        var indexPath = Path.Combine(historyRoot, "index.json");
        var entries = File.Exists(indexPath) ? await ReadAsync<List<HistoryEntry>>(indexPath, cancellationToken) : [];
        entries.Add(new HistoryEntry(scope, id, revision, DateTimeOffset.UtcNow, Path.GetRelativePath(root, destination).Replace('\\', '/')));
        await WriteAtomicAsync(indexPath, entries, cancellationToken);
    }

    private static void TrimHistory(string root, int limit)
    {
        if (limit <= 0) return;
        var historyRoot = Path.Combine(root, ".datumforge", "history");
        foreach (var directory in new[] { Path.Combine(historyRoot, "project"), Path.Combine(historyRoot, "drawing") })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var group in Directory.EnumerateFiles(directory, "*.json").GroupBy(path => Path.GetFileName(path).Split('.')[0], StringComparer.OrdinalIgnoreCase))
                foreach (var oldFile in group.OrderByDescending(File.GetCreationTimeUtc).Skip(limit)) File.Delete(oldFile);
        }
    }

    private static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException($"'{path}' contains no valid DatumForge data.");
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, path, true);
    }

    private static string ResolveInsideRoot(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path '{relativePath}' escapes the project directory.");
        return fullPath;
    }

    private static void ValidateVersion(int actual, int supported, string kind)
    {
        if (actual > supported) throw new InvalidDataException($"This {kind} uses format version {actual}; this build supports up to {supported}.");
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "Untitled" : result;
    }
}
