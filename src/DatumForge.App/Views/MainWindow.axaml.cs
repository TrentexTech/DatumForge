using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using DatumForge.App.ViewModels;
using DatumForge.App.Controls;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.App.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closePromptOpen;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            RestoreWindowSettings();
            ApplyKeyBindings();
        };
        SizeChanged += (_, _) => CaptureNormalWindowSize();
        Closing += MainWindow_OnClosing;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private void Minimize_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestore_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async void Settings_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var saved = await new SettingsWindow(ViewModel).ShowDialog<bool>(this);
        if (!saved) return;
        ApplyKeyBindings();
        ViewModel.StatusText = "Settings saved";
    }

    private void ApplyKeyBindings()
    {
        KeyBindings.Clear();
        foreach (var definition in ShortcutCatalog.All)
        {
            var gestureText = ViewModel.UserSettings.KeyBindings.GetValueOrDefault(definition.Id, string.Empty);
            if (string.IsNullOrWhiteSpace(gestureText)) continue;

            var command = definition.Id switch
            {
                KeyBindingDefaults.Save => ViewModel.SaveCommand,
                KeyBindingDefaults.Undo => ViewModel.UndoCommand,
                KeyBindingDefaults.Redo => ViewModel.RedoCommand,
                KeyBindingDefaults.Erase => ViewModel.EraseCommand,
                KeyBindingDefaults.ToggleGrid => ViewModel.ToggleGridCommand,
                KeyBindingDefaults.ToggleOrtho => ViewModel.ToggleOrthoCommand,
                KeyBindingDefaults.TogglePolar => ViewModel.TogglePolarCommand,
                KeyBindingDefaults.ToggleGridSnap => ViewModel.ToggleSnapCommand,
                KeyBindingDefaults.ToggleObjectSnap => ViewModel.ToggleObjectSnapCommand,
                _ when definition.CommandParameter is not null => ViewModel.SelectToolCommand,
                _ => null
            };
            if (command is null) continue;

            try
            {
                var keyBinding = new KeyBinding
                {
                    Gesture = KeyGesture.Parse(gestureText),
                    Command = command
                };
                if (definition.CommandParameter is not null)
                    keyBinding.CommandParameter = definition.CommandParameter;
                KeyBindings.Add(keyBinding);
            }
            catch (FormatException)
            {
                ViewModel.StatusText = $"Ignored invalid shortcut for {definition.Name}: {gestureText}";
            }
        }
    }

    private async void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || !ViewModel.HasUnsavedWork)
        {
            SaveWindowSettings();
            return;
        }

        e.Cancel = true;
        if (_closePromptOpen) return;

        _closePromptOpen = true;
        try
        {
            var choice = await new UnsavedChangesDialog(ViewModel.UnsavedWorkSummary)
                .ShowDialog<UnsavedChangesChoice>(this);
            if (choice == UnsavedChangesChoice.Cancel) return;
            if (choice == UnsavedChangesChoice.Save && !await ViewModel.SaveAllUnsavedWorkAsync()) return;

            _closeApproved = true;
            Close();
        }
        finally
        {
            _closePromptOpen = false;
        }
    }

    private void RestoreWindowSettings()
    {
        if (DataContext is not MainViewModel viewModel) return;
        var settings = viewModel.UserSettings.Window;
        if (double.IsFinite(settings.Width)) Width = Math.Clamp(settings.Width, MinWidth, 7680);
        if (double.IsFinite(settings.Height)) Height = Math.Clamp(settings.Height, MinHeight, 4320);
        if (settings.IsMaximized) WindowState = WindowState.Maximized;
    }

    private void CaptureNormalWindowSize()
    {
        if (DataContext is not MainViewModel viewModel || WindowState != WindowState.Normal) return;
        viewModel.UserSettings.Window.Width = Math.Max(MinWidth, ClientSize.Width);
        viewModel.UserSettings.Window.Height = Math.Max(MinHeight, ClientSize.Height);
    }

    private void SaveWindowSettings()
    {
        if (DataContext is not MainViewModel viewModel) return;
        CaptureNormalWindowSize();
        viewModel.UserSettings.Window.IsMaximized = WindowState == WindowState.Maximized;
        try { viewModel.SaveUserSettings(); }
        catch (Exception exception) { viewModel.StatusText = $"Could not save user settings: {exception.Message}"; }
    }

    private void CoordinateInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SubmitCoordinate();
        e.Handled = true;
    }

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (ViewportModulesHost.HandleEscape())
        {
            e.Handled = true;
            return;
        }

        if (ViewModel.ActiveTool == "Select") return;
        ViewModel.SelectToolCommand.Execute("Select");
        e.Handled = true;
    }

    private void CoordinateSubmit_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SubmitCoordinate();

    private void ViewportModulesHost_OnTextEntryRequested(object? sender, EventArgs e)
    {
        CoordinateInput.Focus();
        CoordinateInput.SelectAll();
    }

    private void SubmitCoordinate()
    {
        if (!ViewportModulesHost.IsAwaitingTextEntry && ViewModel.TryExecuteTextCommand(ViewModel.CoordinateInputText))
        {
            CoordinateInput.Focus();
            return;
        }
        var result = ViewportModulesHost.SubmitCoordinate(ViewModel.CoordinateInputText);
        ViewModel.ReportCoordinateSubmission(result.Success, result.Completed, result.Message);
        if (result.Success) CoordinateInput.Focus();
    }

    private async void NewProject_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a parent folder for the new DatumForge project",
            AllowMultiple = false
        });
        var parent = folders.FirstOrDefault()?.TryGetLocalPath();
        if (parent is null) return;

        const string baseName = "DatumForge Project";
        var directory = Path.Combine(parent, baseName);
        var suffix = 2;
        while (Directory.Exists(directory)) directory = Path.Combine(parent, $"{baseName} {suffix++}");

        try
        {
            await ViewModel.CreateProjectAsync(directory, Path.GetFileName(directory));
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = $"Could not create project: {exception.Message}";
        }
    }

    private async void OpenProject_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open DatumForge project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("DatumForge project") { Patterns = [$"*{ProjectStore.ProjectExtension}"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        try { await ViewModel.OpenProjectAsync(path); }
        catch (Exception exception) { ViewModel.StatusText = $"Could not open project: {exception.Message}"; }
    }

    private async void OpenDrawing_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open DatumForge drawing",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("DatumForge drawing") { Patterns = [$"*{ProjectStore.DrawingExtension}"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        try { await ViewModel.OpenDrawingAsync(path); }
        catch (Exception exception) { ViewModel.StatusText = $"Could not open drawing: {exception.Message}"; }
    }
}
