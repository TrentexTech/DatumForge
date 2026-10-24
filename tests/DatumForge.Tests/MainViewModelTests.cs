using DatumForge.App.ViewModels;
using DatumForge.Core.Models;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"DatumForge.ViewModelTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ProjectChangesAreTrackedAndSaveAllClearsDirtyState()
    {
        var store = new ProjectStore();
        var workspace = await store.CreateAsync(Path.Combine(_directory, "project"), "Workshop");
        var viewModel = new MainViewModel(store, new UserSettingsStore(Path.Combine(_directory, "settings")));
        await viewModel.OpenProjectAsync(workspace.ProjectFilePath);

        viewModel.AddLayerCommand.Execute(null);

        Assert.True(viewModel.HasUnsavedWork);
        Assert.Contains("Workshop", viewModel.UnsavedWorkSummary, StringComparison.Ordinal);
        Assert.True(await viewModel.SaveAllUnsavedWorkAsync());
        Assert.False(viewModel.HasUnsavedWork);
        var loaded = await store.LoadProjectAsync(workspace.ProjectFilePath);
        Assert.Equal(2, Assert.Single(loaded.Drawings.Values).Layers.Count);
    }

    [Fact]
    public async Task StandaloneDrawingChangesAreSavedBySaveAll()
    {
        var store = new ProjectStore();
        var path = Path.Combine(_directory, "Standalone.dfdraw");
        var drawing = new DrawingDocument { Name = "Standalone", Layers = [new DrawingLayer()] };
        await store.SaveDrawingAsync(path, drawing);
        var viewModel = new MainViewModel(store, new UserSettingsStore(Path.Combine(_directory, "settings")));
        await viewModel.OpenDrawingAsync(path);
        var entity = new DrawingEntity { LayerId = drawing.Layers[0].Id };
        viewModel.ActiveDrawing!.Entities.Add(entity);

        viewModel.RecordEntityCreated(entity);

        Assert.True(viewModel.HasUnsavedWork);
        Assert.True(await viewModel.SaveAllUnsavedWorkAsync());
        Assert.False(viewModel.HasUnsavedWork);
        Assert.Single((await store.LoadDrawingAsync(path)).Entities);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
