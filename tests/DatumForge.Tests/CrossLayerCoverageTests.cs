using DatumForge.App.ViewModels;
using DatumForge.Core.Editing;
using DatumForge.Core.Models;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.Tests;

public sealed class ModelNormalizationTests
{
    [Fact]
    public void ViewportModulesRestoreDefaultsWhenMissing()
    {
        var layout = new LayoutDefinition { ViewportModules = null! };

        layout.NormalizeViewportModules();

        Assert.Equal(
            [ViewportModuleKind.Layers, ViewportModuleKind.Viewer, ViewportModuleKind.Properties],
            layout.ViewportModules.Select(module => module.Kind));
    }

    [Fact]
    public void ViewportModulesRemoveDuplicatesAndNormalizeSizes()
    {
        var layout = new LayoutDefinition
        {
            ViewportModules =
            [
                new() { Kind = ViewportModuleKind.Layers, Size = 10 },
                new() { Kind = ViewportModuleKind.Viewer, Size = double.NaN },
                new() { Kind = ViewportModuleKind.Layers, Size = 500 },
                new() { Kind = ViewportModuleKind.Properties, Size = 5000 }
            ]
        };

        layout.NormalizeViewportModules();

        Assert.Equal(3, layout.ViewportModules.Count);
        Assert.Equal(160, layout.ViewportModules[0].Size);
        Assert.Equal(ViewportModuleDefinition.DefaultSize(ViewportModuleKind.Viewer), layout.ViewportModules[1].Size);
        Assert.Equal(1600, layout.ViewportModules[2].Size);
    }
}

public sealed class UserSettingsBehaviorTests
{
    [Fact]
    public void ProjectViewsRejectEmptyIdsAndNormalizeUnsafeValues()
    {
        var settings = new UserSettings();
        var projectId = Guid.NewGuid();
        var layoutId = Guid.NewGuid();

        settings.RememberProjectView(Guid.Empty, layoutId, 2, new CadPoint(1, 2));
        settings.RememberProjectView(projectId, Guid.Empty, 2, new CadPoint(1, 2));
        settings.RememberProjectView(projectId, layoutId, double.PositiveInfinity, new CadPoint(double.NaN, double.NegativeInfinity));

        Assert.Single(settings.ProjectViews);
        Assert.True(settings.TryGetProjectView(projectId, layoutId, out var view));
        Assert.Equal(ViewportNavigation.MaximumZoom, view.Zoom);
        Assert.Equal(default, view.Pan);
        Assert.False(settings.TryGetProjectView(projectId, Guid.NewGuid(), out _));
    }

    [Fact]
    public void NormalizeRepairsMissingCollectionsAndTrimsBindings()
    {
        var settings = new UserSettings
        {
            Preferences = null!,
            Window = null!,
            KeyBindings = new Dictionary<string, string> { [KeyBindingDefaults.Save] = "  Ctrl+Shift+S  " },
            RecentProjects = null!,
            RecentDrawings = null!,
            ProjectViews = null!
        };

        settings.Normalize();

        Assert.NotNull(settings.Preferences);
        Assert.NotNull(settings.Window);
        Assert.Empty(settings.RecentProjects);
        Assert.Empty(settings.RecentDrawings);
        Assert.Empty(settings.ProjectViews);
        Assert.Equal("Ctrl+Shift+S", settings.KeyBindings[KeyBindingDefaults.Save]);
        Assert.True(settings.KeyBindings.ContainsKey(KeyBindingDefaults.ToggleObjectSnap));
    }

    [Fact]
    public void NormalizeOrdersDeduplicatesAndNamesRecentFiles()
    {
        var older = DateTimeOffset.UtcNow.AddMinutes(-1);
        var newer = DateTimeOffset.UtcNow;
        var path = Path.Combine(Path.GetTempPath(), "DatumForge-normalize.dfproj");
        var settings = new UserSettings
        {
            RecentProjects =
            [
                new() { Path = path, DisplayName = "Older", LastOpenedUtc = older },
                new() { Path = path, DisplayName = string.Empty, LastOpenedUtc = newer },
                new() { Path = string.Empty, DisplayName = "Invalid", LastOpenedUtc = newer }
            ]
        };

        settings.Normalize();

        var recent = Assert.Single(settings.RecentProjects);
        Assert.Equal("DatumForge-normalize", recent.DisplayName);
        Assert.Equal(Path.GetFullPath(path), recent.Path);
        Assert.True(settings.ForgetProject(path));
        Assert.False(settings.ForgetProject(path));
    }
}

public sealed class SettingsViewModelBehaviorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"DatumForge.SettingsViewModel.{Guid.NewGuid():N}");

    [Fact]
    public async Task InvalidGestureIsRejectedWithActionableMessage()
    {
        var settings = CreateViewModel();
        settings.Shortcuts.Single(shortcut => shortcut.Id == KeyBindingDefaults.Line).Gesture = "not a gesture";

        var saved = await settings.SaveAsync(gesture => gesture != "not a gesture");

        Assert.False(saved);
        Assert.True(settings.HasValidationMessage);
        Assert.Contains("Line tool", settings.ValidationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetRestoresDefaultsAndValidSavePersistsThem()
    {
        var settings = CreateViewModel();
        var save = settings.Shortcuts.Single(shortcut => shortcut.Id == KeyBindingDefaults.Save);
        save.Gesture = "Alt+S";
        settings.ResetKeyBindings();

        Assert.Equal("Ctrl+S", save.Gesture);
        Assert.False(settings.HasValidationMessage);
        Assert.True(await settings.SaveAsync(_ => true));
        Assert.True(File.Exists(Path.Combine(_directory, UserSettingsStore.SettingsFileName)));
    }

    [Fact]
    public void ShortcutDisplayReflectsCaptureAndUnassignedStates()
    {
        var item = new ShortcutItemViewModel(new ShortcutDefinition("test", "Test", "Tests"), string.Empty);
        Assert.Equal("Not assigned", item.DisplayGesture);

        item.IsCapturing = true;
        Assert.Equal("Press shortcut…", item.DisplayGesture);

        item.IsCapturing = false;
        item.Gesture = "Ctrl+T";
        Assert.Equal("Ctrl+T", item.DisplayGesture);
    }

    private SettingsViewModel CreateViewModel()
    {
        var store = new UserSettingsStore(_directory);
        return new SettingsViewModel(new MainViewModel(new ProjectStore(), store));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

public sealed class AppViewModelBehaviorTests
{
    [Fact]
    public void LayerItemMirrorsLayerStateAndOnlyReportsActualChanges()
    {
        var changes = 0;
        var layer = new DrawingLayer
        {
            Name = "Walls",
            Color = "#112233",
            LineType = CadLineType.Dashed,
            LineWeightMm = 0.35,
            Opacity = 0.5
        };
        var item = new LayerItemViewModel(layer, () => changes++);

        item.IsVisible = true;
        item.IsVisible = false;
        item.IsLocked = true;

        Assert.Equal(2, changes);
        Assert.False(layer.IsVisible);
        Assert.True(layer.IsLocked);
        Assert.Equal("Walls", item.Name);
        Assert.Equal("#112233", item.Color);
        Assert.Equal("Dashed", item.LineType);
        Assert.Equal($"{0.35:0.00} mm", item.LineWeight);
        Assert.Equal($"{0.5:P0}", item.Opacity);
    }

    [Fact]
    public void ViewportModulePositionExposesValidMoveDirections()
    {
        var owner = new MainViewModel(new ProjectStore(), new UserSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var item = new ViewportModuleItemViewModel(owner, new ViewportModuleDefinition { Kind = ViewportModuleKind.Viewer });

        item.UpdatePosition(0, 3);
        Assert.False(item.CanMoveLeft);
        Assert.True(item.CanMoveRight);

        item.UpdatePosition(2, 3);
        Assert.True(item.CanMoveLeft);
        Assert.False(item.CanMoveRight);
        Assert.Equal("VIEWER", item.Title);
        Assert.True(item.IsViewer);
    }
}
