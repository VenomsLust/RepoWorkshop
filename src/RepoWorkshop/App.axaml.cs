using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using RepoWorkshop.Automation;
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

            var main = new MainViewModel(services);
            window.DataContext = main;
            desktop.MainWindow = window;

            var api = new ApiHost(main, services);
            _ = api.ApplySettingsAsync();
            main.SettingsApplied += () => _ = api.ApplySettingsAsync();
            desktop.Exit += (_, _) => api.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
