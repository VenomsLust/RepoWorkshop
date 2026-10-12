using System;
using System.Threading.Tasks;
using Avalonia;
using RepoWorkshop.Api;

namespace RepoWorkshop;

/// <summary>Entry point.</summary>
internal sealed class Program {
    /// <summary>
    /// Starts the app, or brings an already-running instance to the front. Don't use Avalonia or
    /// any SynchronizationContext-reliant code before the app is built.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) {
        if (ActivateRunningInstance()) {
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration; also used by the visual designer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    /// <summary>Asks a running instance (via the API pipe) to come to the front; returns whether one answered.</summary>
    private static bool ActivateRunningInstance() {
        try {
            return Task.Run(async () => {
                await using ApiClient? client = await ApiClient.TryConnectAsync(timeout: TimeSpan.FromMilliseconds(150));
                if (client is null) {
                    return false;
                }
                await client.CallAsync("app.activate");
                return true;
            }).GetAwaiter().GetResult();
        } catch (Exception) {
            return false; // Anything unexpected: just start normally.
        }
    }
}
