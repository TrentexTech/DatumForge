using DatumForge.Core.Models;
using DatumForge.Core.Services;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.Tests;

public sealed class DrawingAssociationInspectorTests
{
    [Fact]
    public void StandaloneDrawingDoesNotRequireWarning()
    {
        var drawing = new DrawingDocument { ProjectId = null };
        Assert.False(DrawingAssociationInspector.Assess(drawing, null).RequiresWarning);
    }

    [Fact]
    public void DrawingFromActiveProjectDoesNotRequireWarning()
    {
        var projectId = Guid.NewGuid();
        var drawing = new DrawingDocument { ProjectId = projectId };
        Assert.False(DrawingAssociationInspector.Assess(drawing, projectId).RequiresWarning);
    }

    [Fact]
    public void ProjectDrawingOpenedAloneExplainsRisks()
    {
        var drawing = new DrawingDocument { ProjectId = Guid.NewGuid() };
        var result = DrawingAssociationInspector.Assess(drawing, null);
        Assert.True(result.RequiresWarning);
        Assert.Contains("history", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("references", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProjectStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"DatumForge.Tests.{Guid.NewGuid():N}");
    private readonly ProjectStore _store = new();

    [Fact]
    public async Task CreateProducesPortableProjectStructure()
    {
        var workspace = await _store.CreateAsync(_directory, "Workshop");
        Assert.True(File.Exists(workspace.ProjectFilePath));
        Assert.Single(workspace.Project.Drawings);
        Assert.Single(workspace.Project.Layouts);
        Assert.True(File.Exists(Path.Combine(_directory, "drawings", "Drawing 1.dfdraw")));
    }

    [Fact]
    public async Task RoundTripPreservesLayerOptionsAndGeometry()
    {
        var workspace = await _store.CreateAsync(_directory, "Workshop");
        var drawing = Assert.Single(workspace.Drawings.Values);
        var layer = Assert.Single(drawing.Layers);
        layer.Color = "#FF00FF";
        layer.LineType = CadLineType.DashDot;
        layer.LineWeightMm = 0.7;
        layer.Opacity = 0.45;
        layer.Group = "Annotations";
        var layout = Assert.Single(workspace.Project.Layouts);
        layout.SnapToObjects = false;
        layout.ObjectSnaps = ObjectSnapMode.Endpoint | ObjectSnapMode.Center;
        drawing.Entities.Add(new DrawingEntity { Kind = EntityKind.Line, LayerId = layer.Id, Points = [new(1, 2), new(3, 4)] });
        await _store.SaveAsync(workspace);

        var loaded = await _store.LoadProjectAsync(workspace.ProjectFilePath);
        var loadedDrawing = Assert.Single(loaded.Drawings.Values);
        var loadedLayer = Assert.Single(loadedDrawing.Layers);
        Assert.Equal(CadLineType.DashDot, loadedLayer.LineType);
        Assert.Equal(0.7, loadedLayer.LineWeightMm);
        Assert.Equal(0.45, loadedLayer.Opacity);
        Assert.Equal("Annotations", loadedLayer.Group);
        Assert.Single(loadedDrawing.Entities);
        var loadedLayout = Assert.Single(loaded.Project.Layouts);
        Assert.False(loadedLayout.SnapToObjects);
        Assert.Equal(ObjectSnapMode.Endpoint | ObjectSnapMode.Center, loadedLayout.ObjectSnaps);
    }

    [Fact]
    public async Task SaveCreatesProjectAndDrawingHistory()
    {
        var workspace = await _store.CreateAsync(_directory, "Workshop");
        await _store.SaveAsync(workspace);
        var history = await _store.ReadHistoryAsync(workspace.ProjectFilePath);
        Assert.Contains(history, entry => entry.Scope == "project");
        Assert.Contains(history, entry => entry.Scope == "drawing");
        Assert.All(history, entry => Assert.True(File.Exists(Path.Combine(_directory, entry.RelativePath))));
    }

    [Fact]
    public async Task SaveIncrementsDocumentRevisions()
    {
        var workspace = await _store.CreateAsync(_directory, "Workshop");
        var initialProjectRevision = workspace.Project.Revision;
        var drawing = Assert.Single(workspace.Drawings.Values);
        var initialDrawingRevision = drawing.Revision;
        await _store.SaveAsync(workspace);
        Assert.Equal(initialProjectRevision + 1, workspace.Project.Revision);
        Assert.Equal(initialDrawingRevision + 1, drawing.Revision);
    }

    [Fact]
    public async Task StandaloneDrawingCanBeSavedDirectly()
    {
        var path = Path.Combine(_directory, "Standalone.dfdraw");
        var drawing = new DrawingDocument { Name = "Standalone", Layers = [new DrawingLayer()] };
        drawing.Entities.Add(new DrawingEntity { LayerId = drawing.Layers[0].Id });

        await _store.SaveDrawingAsync(path, drawing);

        var loaded = await _store.LoadDrawingAsync(path);
        Assert.Equal(1, loaded.Revision);
        Assert.Single(loaded.Entities);
    }

    [Fact]
    public async Task NewerFormatVersionIsRejected()
    {
        var workspace = await _store.CreateAsync(_directory, "Workshop");
        var json = await File.ReadAllTextAsync(workspace.ProjectFilePath);
        var changed = json.Replace("\"formatVersion\": 1", "\"formatVersion\": 999", StringComparison.Ordinal);
        await File.WriteAllTextAsync(workspace.ProjectFilePath, changed);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadProjectAsync(workspace.ProjectFilePath));
        Assert.Contains("supports up to", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
