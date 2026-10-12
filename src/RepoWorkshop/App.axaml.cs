using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using RepoWorkshop.Services;
using RepoWorkshop.ViewModels;
using RepoWorkshop.Views;

namespace RepoWorkshop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DiffService.CleanupOldTempFiles();

            var store = new SettingsStore();
            var settings = store.Load();
            ApplyTheme(settings.Theme);
            var log = new LogService();
            var git = new GitService(log);
            var winMerge = new WinMergeService(() => settings.WinMergePath);
            var diff = new DiffService(git, winMerge);

            var window = new MainWindow();
            var dialogs = new DialogService(() => window);
            var services = new AppServices(store, settings, git, winMerge, diff, log, dialogs);

            window.DataContext = new MainViewModel(services);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string? theme)
    {
        if (Current is null)
            return;
        Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }
}
