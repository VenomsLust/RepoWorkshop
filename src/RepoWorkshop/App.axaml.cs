using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using RepoWorkshop.Automation;
using RepoWorkshop.Models;
using RepoWorkshop.Services;
using RepoWorkshop.ViewModels;
using RepoWorkshop.Views;

namespace RepoWorkshop;

/// <summary>The application: wires up the services, main window and API.</summary>
public partial class App : Application {
    /// <summary>The longest the app waits for the API to stop when exiting.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Applies the "Dark", "Light" or "System" theme.</summary>
    public static void ApplyTheme(string? theme) {
        if (Current is null) {
            return;
        }
        Current.RequestedThemeVariant = theme switch {
            "Light" => ThemeVariant.Light,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }

    /// <summary>Loads App.axaml.</summary>
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Creates the services, main window and API host.</summary>
    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            DiffService.CleanupOldTempFiles();

            SettingsStore store = new();
            AppSettings settings = store.Load();
            ApplyTheme(settings.Theme);
            LogService log = new();
            GitService git = new(log);
            WinMergeService winMerge = new(() => settings.WinMergePath);
            DiffService diff = new(git, winMerge);

            MainWindow window = new();
            DialogService dialogs = new(() => window);
            AppServices services = new(store, settings, git, winMerge, diff, log, dialogs);

            MainViewModel main = new(services);
            window.DataContext = main;
            desktop.MainWindow = window;

            ApiHost api = new(main, services);
            _ = api.ApplySettingsAsync();
            main.SettingsApplied += () => _ = api.ApplySettingsAsync();
            // Stop the API on a worker thread: blocking the UI thread on async work that needs it deadlocks the exit.
            desktop.Exit += (_, _) => Task.Run(() => api.DisposeAsync().AsTask()).Wait(ShutdownTimeout);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
