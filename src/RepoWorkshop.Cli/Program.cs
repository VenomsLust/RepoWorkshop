using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepoWorkshop.Api;

namespace RepoWorkshop.Cli;

/// <summary>rw: command-line client for the Repo Workshop API. Prints JSON results; see "rw help".</summary>
internal static class Program {
    /// <summary>Exit code when the API returned an error.</summary>
    private const int ExitApiError = 1;

    /// <summary>Exit code for a malformed command line.</summary>
    private const int ExitUsage = 2;

    /// <summary>Exit code when the app could not be reached.</summary>
    private const int ExitUnavailable = 3;

    /// <summary>How long to wait for the app to start listening after launching it.</summary>
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Readable output: indented, without escaping quotes, apostrophes or non-ASCII characters.</summary>
    private static readonly JsonSerializerOptions OutputOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses the command line, calls the API and prints the result.</summary>
    private static async Task<int> Main(string[] args) {
        Console.OutputEncoding = new UTF8Encoding(false);

        CommandLine command;
        try {
            command = CommandLine.Parse(args);
        } catch (Exception ex) when (ex is UsageException or FormatException) {
            Console.Error.WriteLine($"rw: {ex.Message}");
            Console.Error.WriteLine("Usage: rw <method> [--name value]...   (rw help lists methods)");
            return ExitUsage;
        }

        using CancellationTokenSource cts = new(command.Timeout);
        await using ApiClient? client = await ConnectAsync(command, cts.Token);
        if (client is null) {
            return ExitUnavailable;
        }

        string method = command.IsHelp ? "help" : command.Method!;
        JsonObject parameters = command.Params;
        if (!command.IsHelp && !parameters.ContainsKey("cwd")) {
            parameters["cwd"] = Environment.CurrentDirectory;
        }

        try {
            JsonNode? result = await client.CallAsync(method, parameters, cts.Token);
            if (command.IsHelp && !command.Raw) {
                Console.Write(HelpText.Format(result, parameters["method"]?.GetValue<string>()));
            } else {
                Console.WriteLine(result?.ToJsonString(OutputOptions) ?? "true");
            }
            return 0;
        } catch (ApiException ex) {
            JsonObject error = new() { ["error"] = new JsonObject { ["code"] = ex.Code, ["message"] = ex.Message } };
            Console.Error.WriteLine(error.ToJsonString(OutputOptions));
            return ExitApiError;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine($"rw: timed out after {command.Timeout.TotalSeconds:0}s waiting for Repo Workshop (is a confirmation dialog open?).");
            return ExitApiError;
        } catch (IOException ex) {
            Console.Error.WriteLine($"rw: lost connection to Repo Workshop: {ex.Message}");
            return ExitUnavailable;
        }
    }

    /// <summary>Connects to the running app, starting it first if needed (unless --no-launch).</summary>
    private static async Task<ApiClient?> ConnectAsync(CommandLine command, CancellationToken ct) {
        ApiClient? client = await ApiClient.TryConnectAsync(command.PipeName, TimeSpan.FromSeconds(1), ct);
        if (client is not null) {
            return client;
        }

        if (Process.GetProcessesByName("RepoWorkshop").Length > 0) {
            Console.Error.WriteLine("rw: Repo Workshop is running but its API is not available. Enable it in Settings > Automation.");
            return null;
        }
        if (command.NoLaunch) {
            Console.Error.WriteLine("rw: Repo Workshop is not running.");
            return null;
        }

        string? exe = FindApp();
        if (exe is null) {
            Console.Error.WriteLine("rw: Repo Workshop is not running and RepoWorkshop.exe was not found next to rw.exe.");
            return null;
        }

        Console.Error.WriteLine("rw: starting Repo Workshop…");
        // Launch through Explorer so the app runs as a normal desktop process. If rw itself was started
        // by a packaged (MSIX) app such as an AI agent's host, a direct child would inherit that package's
        // file-system virtualization and read a different settings file than the user's own copy.
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { exe }, UseShellExecute = false })?.Dispose();

        DateTime deadline = DateTime.UtcNow + LaunchTimeout;
        while (DateTime.UtcNow < deadline) {
            client = await ApiClient.TryConnectAsync(command.PipeName, TimeSpan.FromSeconds(1), ct);
            if (client is not null) {
                return client;
            }
        }
        Console.Error.WriteLine($"rw: Repo Workshop did not start listening within {LaunchTimeout.TotalSeconds:0} seconds.");
        return null;
    }

    /// <summary>Finds RepoWorkshop.exe next to rw.exe, or in the default install folder.</summary>
    private static string? FindApp() {
        string[] candidates = [
            Path.Combine(AppContext.BaseDirectory, "RepoWorkshop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RepoWorkshop", "RepoWorkshop.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
