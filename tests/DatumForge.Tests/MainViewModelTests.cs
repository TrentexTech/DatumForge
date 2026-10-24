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

    [Fact]
    public async Task KeyBindingChangesArePersistedAndReflectedInTooltips()
    {
        var settingsStore = new UserSettingsStore(Path.Combine(_directory, "settings"));
        var viewModel = new MainViewModel(new ProjectStore(), settingsStore);
        var bindings = KeyBindingDefaults.Create();
        bindings[KeyBindingDefaults.Save] = "Ctrl+Shift+S";
        bindings[KeyBindingDefaults.Line] = "L";

        await viewModel.UpdateKeyBindingsAsync(bindings);

        var loaded = await settingsStore.LoadAsync();
        Assert.Equal("Ctrl+Shift+S", loaded.KeyBindings[KeyBindingDefaults.Save]);
        Assert.Equal("L", loaded.KeyBindings[KeyBindingDefaults.Line]);
        Assert.Equal("Save (Ctrl+Shift+S)", viewModel.SaveToolTip);
    }

    [Fact]
    public async Task SettingsRejectDuplicateKeyBindings()
    {
        var settingsStore = new UserSettingsStore(Path.Combine(_directory, "settings"));
        var settings = new SettingsViewModel(new MainViewModel(new ProjectStore(), settingsStore));
        settings.Shortcuts.Single(shortcut => shortcut.Id == KeyBindingDefaults.Undo).Gesture = "Ctrl+S";

        var saved = await settings.SaveAsync(_ => true);

        Assert.False(saved);
        Assert.Contains("assigned to both", settings.ValidationMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(settingsStore.SettingsFilePath));
    }

    [Fact]
    public async Task ViewportModulesAreLayoutSpecificAndReorderable()
    {
        var store = new ProjectStore();
        var workspace = await store.CreateAsync(Path.Combine(_directory, "modules"), "Modular");
        var viewModel = new MainViewModel(store, new UserSettingsStore(Path.Combine(_directory, "settings")));
        await viewModel.OpenProjectAsync(workspace.ProjectFilePath);

        var layers = viewModel.ViewportModules.Single(module => module.IsLayers);
        viewModel.RemoveViewportModuleCommand.Execute(layers);
        Assert.False(viewModel.CanAddViewerModule);
        Assert.True(viewModel.CanAddLayersModule);

        viewModel.AddViewportModuleCommand.Execute("Layers");
        var restoredLayers = viewModel.ViewportModules.Single(module => module.IsLayers);
        viewModel.MoveViewportModuleLeftCommand.Execute(restoredLayers);
        viewModel.MoveViewportModuleLeftCommand.Execute(restoredLayers);

        Assert.Equal([ViewportModuleKind.Layers, ViewportModuleKind.Viewer, ViewportModuleKind.Properties],
            viewModel.SelectedLayout!.ViewportModules.Select(module => module.Kind));

        var modelLayout = viewModel.SelectedLayout;
        viewModel.AddLayoutCommand.Execute(null);
        var layoutLayers = viewModel.ViewportModules.Single(module => module.IsLayers);
        viewModel.RemoveViewportModuleCommand.Execute(layoutLayers);
        Assert.DoesNotContain(viewModel.ViewportModules, module => module.IsLayers);

        viewModel.SelectedLayout = modelLayout;
        Assert.Contains(viewModel.ViewportModules, module => module.IsLayers);
        Assert.True(viewModel.HasUnsavedWork);
    }

    [Fact]
    public void RibbonTabsSwitchTheirVisibleCommandGroup()
    {
        var viewModel = new MainViewModel(new ProjectStore(), new UserSettingsStore(Path.Combine(_directory, "settings")));
        Assert.True(viewModel.IsFileRibbonTab);

        viewModel.SelectRibbonTabCommand.Execute("View");

        Assert.False(viewModel.IsFileRibbonTab);
        Assert.True(viewModel.IsViewRibbonTab);
        Assert.Equal("View", viewModel.SelectedRibbonTab);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
