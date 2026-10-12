using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using RepoWorkshop.Api;

namespace RepoWorkshop;

/// <summary>Entry point.</summary>
internal sealed class Program {
    /// <summary>How long to wait for an already-running instance to answer.</summary>
    private static readonly TimeSpan ActivateTimeout = TimeSpan.FromSeconds(2);

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

    /// <summary>
    /// Asks a running instance (via the API pipe) to come to the front; returns whether one answered
    /// in time. An instance that doesn't answer is treated as absent, so it can't block a new start.
    /// </summary>
    private static bool ActivateRunningInstance() {
        try {
            using CancellationTokenSource cts = new(ActivateTimeout);
            return Task.Run(async () => {
                await using ApiClient? client = await ApiClient.TryConnectAsync(timeout: TimeSpan.FromMilliseconds(150), ct: cts.Token);
                if (client is null) {
                    return false;
                }
                await client.CallAsync("app.activate", ct: cts.Token);
                return true;
            }).GetAwaiter().GetResult();
        } catch (Exception) {
            return false; // Timed out or anything unexpected: just start normally.
        }
    }
}
