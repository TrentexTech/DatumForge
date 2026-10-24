using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.App.ViewModels;

public sealed record ShortcutDefinition(string Id, string Name, string Category, string? CommandParameter = null);

public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutDefinition> All { get; } =
    [
        new(KeyBindingDefaults.Save, "Save", "Editing"),
        new(KeyBindingDefaults.Undo, "Undo", "Editing"),
        new(KeyBindingDefaults.Redo, "Redo", "Editing"),
        new(KeyBindingDefaults.Erase, "Erase selection", "Editing"),
        new(KeyBindingDefaults.Select, "Select tool", "Draw", "Select"),
        new(KeyBindingDefaults.Line, "Line tool", "Draw", "Line"),
        new(KeyBindingDefaults.Polyline, "Polyline tool", "Draw", "Polyline"),
        new(KeyBindingDefaults.Circle, "Circle tool", "Draw", "Circle"),
        new(KeyBindingDefaults.Arc, "Arc tool", "Draw", "Arc"),
        new(KeyBindingDefaults.Rectangle, "Rectangle tool", "Draw", "Rectangle"),
        new(KeyBindingDefaults.Text, "Text tool", "Draw", "Text"),
        new(KeyBindingDefaults.Dimension, "Dimension tool", "Draw", "Dimension"),
        new(KeyBindingDefaults.Move, "Move tool", "Modify", "Move"),
        new(KeyBindingDefaults.Copy, "Copy tool", "Modify", "Copy"),
        new(KeyBindingDefaults.Rotate, "Rotate tool", "Modify", "Rotate"),
        new(KeyBindingDefaults.Scale, "Scale tool", "Modify", "Scale"),
        new(KeyBindingDefaults.ToggleGrid, "Toggle grid", "View"),
        new(KeyBindingDefaults.ToggleOrtho, "Toggle orthographic tracking", "View"),
        new(KeyBindingDefaults.TogglePolar, "Toggle polar tracking", "View"),
        new(KeyBindingDefaults.ToggleGridSnap, "Toggle grid snap", "View"),
        new(KeyBindingDefaults.ToggleObjectSnap, "Toggle object snap", "View")
    ];
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _mainViewModel;

    public SettingsViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        Shortcuts = new ObservableCollection<ShortcutItemViewModel>(ShortcutCatalog.All.Select(definition =>
            new ShortcutItemViewModel(
                definition,
                mainViewModel.UserSettings.KeyBindings.GetValueOrDefault(definition.Id, string.Empty))));

        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? assembly.GetName().Version?.ToString()
                  ?? "Development build";
        Executable = Path.GetFileName(Environment.ProcessPath) ?? "DatumForge.App";
        Runtime = $".NET {Environment.Version} · {RuntimeInformation.ProcessArchitecture}";
        LocalData = mainViewModel.UserDataDirectory;
    }

    public ObservableCollection<ShortcutItemViewModel> Shortcuts { get; }
    public string Version { get; }
    public string Executable { get; }
    public string Runtime { get; }
    public string LocalData { get; }
    public string License => "MIT License";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    public partial string ValidationMessage { get; set; } = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public void ResetKeyBindings()
    {
        foreach (var shortcut in Shortcuts)
            shortcut.Gesture = KeyBindingDefaults.Values.GetValueOrDefault(shortcut.Id, string.Empty);
        ValidationMessage = string.Empty;
    }

    public async Task<bool> SaveAsync(Func<string, bool> isValidGesture)
    {
        ValidationMessage = Validate(isValidGesture);
        if (HasValidationMessage) return false;

        await _mainViewModel.UpdateKeyBindingsAsync(
            Shortcuts.ToDictionary(shortcut => shortcut.Id, shortcut => shortcut.Gesture, StringComparer.OrdinalIgnoreCase));
        return true;
    }

    private string Validate(Func<string, bool> isValidGesture)
    {
        foreach (var shortcut in Shortcuts.Where(shortcut => !string.IsNullOrWhiteSpace(shortcut.Gesture)))
        {
            if (!isValidGesture(shortcut.Gesture))
                return $"{shortcut.Gesture} is not a valid shortcut for {shortcut.Name}.";
        }

        var duplicate = Shortcuts
            .Where(shortcut => !string.IsNullOrWhiteSpace(shortcut.Gesture))
            .GroupBy(shortcut => shortcut.Gesture, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        return duplicate is null
            ? string.Empty
            : $"{duplicate.Key} is assigned to both {duplicate.First().Name} and {duplicate.Skip(1).First().Name}.";
    }
}

public partial class ShortcutItemViewModel : ViewModelBase
{
    public ShortcutItemViewModel(ShortcutDefinition definition, string gesture)
    {
        Id = definition.Id;
        Name = definition.Name;
        Category = definition.Category;
        Gesture = gesture;
    }

    public string Id { get; }
    public string Name { get; }
    public string Category { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayGesture))]
    public partial string Gesture { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayGesture))]
    public partial bool IsCapturing { get; set; }

    public string DisplayGesture => IsCapturing
        ? "Press shortcut…"
        : string.IsNullOrWhiteSpace(Gesture) ? "Not assigned" : Gesture;
}
