using Avalonia;
using System;
using System.Threading.Tasks;
using RepoWorkshop.Api;

namespace RepoWorkshop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Single instance: if the app is already running, bring it to the front instead.
        if (ActivateRunningInstance())
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool ActivateRunningInstance()
    {
        try
        {
            return Task.Run(async () =>
            {
                await using var client = await ApiClient.TryConnectAsync(timeout: TimeSpan.FromMilliseconds(150));
                if (client is null)
                    return false;
                await client.CallAsync("app.activate");
                return true;
            }).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return false; // Anything unexpected: just start normally.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
