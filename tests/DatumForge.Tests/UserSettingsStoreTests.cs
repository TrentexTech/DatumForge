using DatumForge.Infrastructure.Persistence;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class UserSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"DatumForge.Settings.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task RoundTripPreservesPreferencesWindowAndRecents()
    {
        var store = new UserSettingsStore(_directory);
        var settings = new UserSettings();
        settings.Preferences.Theme = "Dark";
        settings.Preferences.RecentItemLimit = 7;
        settings.Window.Width = 1280;
        settings.Window.Height = 720;
        settings.Window.IsMaximized = true;
        settings.KeyBindings[KeyBindingDefaults.Save] = "Ctrl+Shift+S";
        settings.KeyBindings[KeyBindingDefaults.Line] = "L";
        settings.RememberProject(Path.Combine(_directory, "Workshop.dfproj"), "Workshop");
        settings.RememberDrawing(Path.Combine(_directory, "Detail.dfdraw"), "Detail");
        var projectId = Guid.NewGuid();
        var layoutId = Guid.NewGuid();
        settings.RememberProjectView(projectId, layoutId, 2.5, new CadPoint(12, -7));

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(UserSettings.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Equal(7, loaded.Preferences.RecentItemLimit);
        Assert.Equal(1280, loaded.Window.Width);
        Assert.Equal(720, loaded.Window.Height);
        Assert.True(loaded.Window.IsMaximized);
        Assert.Equal("Ctrl+Shift+S", loaded.KeyBindings[KeyBindingDefaults.Save]);
        Assert.Equal("L", loaded.KeyBindings[KeyBindingDefaults.Line]);
        Assert.Equal("Workshop", Assert.Single(loaded.RecentProjects).DisplayName);
        Assert.Equal("Detail", Assert.Single(loaded.RecentDrawings).DisplayName);
        Assert.True(loaded.TryGetProjectView(projectId, layoutId, out var view));
        Assert.Equal(2.5, view.Zoom);
        Assert.Equal(new CadPoint(12, -7), view.Pan);
    }

    [Fact]
    public void NormalizeAddsNewDefaultKeyBindingsWithoutOverwritingRemaps()
    {
        var settings = new UserSettings
        {
            KeyBindings = new Dictionary<string, string>
            {
                [KeyBindingDefaults.Save] = "Ctrl+Shift+S"
            }
        };

        settings.Normalize();

        Assert.Equal("Ctrl+Shift+S", settings.KeyBindings[KeyBindingDefaults.Save]);
        Assert.Equal("Ctrl+Z", settings.KeyBindings[KeyBindingDefaults.Undo]);
        Assert.Equal("F7", settings.KeyBindings[KeyBindingDefaults.ToggleGrid]);
        Assert.Equal("F8", settings.KeyBindings[KeyBindingDefaults.ToggleOrtho]);
        Assert.Equal("F10", settings.KeyBindings[KeyBindingDefaults.TogglePolar]);
        Assert.True(settings.KeyBindings.ContainsKey(KeyBindingDefaults.Arc));
        Assert.True(settings.KeyBindings.ContainsKey(KeyBindingDefaults.Mirror));
    }

    [Fact]
    public void SynchronousLoadAndSaveSupportApplicationStartupAndWindowClosing()
    {
        var store = new UserSettingsStore(_directory);
        store.Save(new UserSettings { Window = new WindowSettings { Width = 1100, Height = 700 } });

        var loaded = store.Load();

        Assert.Equal(1100, loaded.Window.Width);
        Assert.Equal(700, loaded.Window.Height);
        Assert.False(File.Exists(store.SettingsFilePath + ".tmp"));
    }

    [Fact]
    public void RecentItemsAreDeduplicatedAndBounded()
    {
        var settings = new UserSettings();
        settings.Preferences.RecentItemLimit = 2;
        var first = Path.Combine(_directory, "First.dfproj");
        var second = Path.Combine(_directory, "Second.dfproj");
        var third = Path.Combine(_directory, "Third.dfproj");

        settings.RememberProject(first, "First");
        settings.RememberProject(second, "Second");
        settings.RememberProject(first, "First renamed");
        settings.RememberProject(third, "Third");

        Assert.Equal(2, settings.RecentProjects.Count);
        Assert.Equal("Third", settings.RecentProjects[0].DisplayName);
        Assert.Equal("First renamed", settings.RecentProjects[1].DisplayName);
    }

    [Fact]
    public async Task InvalidSettingsArePreservedForRecovery()
    {
        Directory.CreateDirectory(_directory);
        var store = new UserSettingsStore(_directory);
        await File.WriteAllTextAsync(store.SettingsFilePath, "{ definitely not valid json");

        var loaded = await store.LoadAsync();

        Assert.Equal(UserSettings.CurrentFormatVersion, loaded.FormatVersion);
        Assert.False(File.Exists(store.SettingsFilePath));
        Assert.NotNull(store.RecoveryNotice);
        Assert.Single(Directory.EnumerateFiles(_directory, "settings.invalid-*.json"));
    }

    [Fact]
    public void SynchronousLoadPreservesUnsupportedSettingsForRecovery()
    {
        Directory.CreateDirectory(_directory);
        var store = new UserSettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, "{\"formatVersion\":999}");

        var loaded = store.Load();

        Assert.Equal(UserSettings.CurrentFormatVersion, loaded.FormatVersion);
        Assert.NotNull(store.RecoveryNotice);
        Assert.False(File.Exists(store.SettingsFilePath));
        Assert.Single(Directory.EnumerateFiles(_directory, "settings.invalid-*.json"));
    }

    [Fact]
    public async Task SaveRejectsNullSettingsWithoutCreatingDataDirectory()
    {
        var store = new UserSettingsStore(_directory);

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!));

        Assert.False(Directory.Exists(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
