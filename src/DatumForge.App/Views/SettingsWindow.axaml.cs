using Avalonia.Controls;
using Avalonia.Input;
using DatumForge.App.ViewModels;

namespace DatumForge.App.Views;

public partial class SettingsWindow : Window
{
    private ShortcutItemViewModel? _capturingShortcut;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(MainViewModel mainViewModel) : this()
    {
        DataContext = new SettingsViewModel(mainViewModel);
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private void Shortcut_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ShortcutItemViewModel shortcut }) return;
        StopCapturing();
        _capturingShortcut = shortcut;
        shortcut.IsCapturing = true;
    }

    private void SettingsWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingShortcut is null) return;
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            StopCapturing();
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Delete or Key.Back)
        {
            _capturingShortcut.Gesture = string.Empty;
            StopCapturing();
            return;
        }

        if (IsModifierKey(e.Key)) return;

        _capturingShortcut.Gesture = new KeyGesture(e.Key, e.KeyModifiers).ToString();
        StopCapturing();
    }

    private async void Save_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        StopCapturing();
        if (await ViewModel.SaveAsync(IsValidGesture)) Close(true);
    }

    private void ResetKeyBindings_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        StopCapturing();
        ViewModel.ResetKeyBindings();
    }

    private void Cancel_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

    private void StopCapturing()
    {
        if (_capturingShortcut is not null) _capturingShortcut.IsCapturing = false;
        _capturingShortcut = null;
    }

    private static bool IsValidGesture(string gesture)
    {
        try
        {
            _ = KeyGesture.Parse(gesture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;
}
