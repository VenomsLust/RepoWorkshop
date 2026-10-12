using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RepoWorkshop.Views;

/// <summary>The settings dialog; closes with true when saved.</summary>
public partial class SettingsWindow : Window {
    /// <summary>Builds the window.</summary>
    public SettingsWindow() {
        InitializeComponent();
    }

    /// <summary>Closes, saving.</summary>
    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);

    /// <summary>Closes without saving.</summary>
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
