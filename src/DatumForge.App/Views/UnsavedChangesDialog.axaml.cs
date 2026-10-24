using Avalonia.Controls;

namespace DatumForge.App.Views;

public enum UnsavedChangesChoice
{
    Cancel,
    Save,
    Discard
}

public partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    public UnsavedChangesDialog(string summary) : this()
    {
        SummaryText.Text = summary;
    }

    private void Save_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(UnsavedChangesChoice.Save);
    private void Discard_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(UnsavedChangesChoice.Discard);
    private void Cancel_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(UnsavedChangesChoice.Cancel);
}
