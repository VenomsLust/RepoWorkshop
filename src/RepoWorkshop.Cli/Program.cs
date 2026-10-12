using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepoWorkshop.Api;
using RepoWorkshop.Cli;

// rw: command-line client for the Repo Workshop API. Prints JSON results; see "rw help".

const int ExitApiError = 1;
const int ExitUsage = 2;
const int ExitUnavailable = 3;

Console.OutputEncoding = new UTF8Encoding(false);

// Readable output: don't escape quotes, apostrophes or non-ASCII characters.
var outputOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

CommandLine command;
try
{
    command = CommandLine.Parse(args);
}
catch (Exception ex) when (ex is UsageException or FormatException)
{
    Console.Error.WriteLine($"rw: {ex.Message}");
    Console.Error.WriteLine("Usage: rw <method> [--name value]...   (rw help lists methods)");
    return ExitUsage;
}

using var cts = new CancellationTokenSource(command.Timeout);
await using var client = await ConnectAsync(command, cts.Token);
if (client is null)
    return ExitUnavailable;

var method = command.IsHelp ? "help" : command.Method!;
var parameters = command.Params;
if (!command.IsHelp && !parameters.ContainsKey("cwd"))
    parameters["cwd"] = Environment.CurrentDirectory;

try
{
    var result = await client.CallAsync(method, parameters, cts.Token);
    if (command.IsHelp && !command.Raw)
        Console.Write(HelpText.Format(result, parameters["method"]?.GetValue<string>()));
    else
        Console.WriteLine(result?.ToJsonString(outputOptions) ?? "true");
    return 0;
}
catch (ApiException ex)
{
    Console.Error.WriteLine(new JsonObject { ["error"] = new JsonObject { ["code"] = ex.Code, ["message"] = ex.Message } }
        .ToJsonString(outputOptions));
    return ExitApiError;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"rw: timed out after {command.Timeout.TotalSeconds:0}s waiting for Repo Workshop (is a confirmation dialog open?).");
    return ExitApiError;
}
catch (IOException ex)
{
    Console.Error.WriteLine($"rw: lost connection to Repo Workshop: {ex.Message}");
    return ExitUnavailable;
}

// Connects to the running app, starting it first if needed (unless --no-launch).
static async Task<ApiClient?> ConnectAsync(CommandLine command, CancellationToken ct)
{
    var client = await ApiClient.TryConnectAsync(command.PipeName, TimeSpan.FromSeconds(1), ct);
    if (client is not null)
        return client;

    if (Process.GetProcessesByName("RepoWorkshop").Length > 0)
    {
        Console.Error.WriteLine("rw: Repo Workshop is running but its API is not available. Enable it in Settings > Automation.");
        return null;
    }
    if (command.NoLaunch)
    {
        Console.Error.WriteLine("rw: Repo Workshop is not running.");
        return null;
    }

    var exe = FindApp();
    if (exe is null)
    {
        Console.Error.WriteLine("rw: Repo Workshop is not running and RepoWorkshop.exe was not found next to rw.exe.");
        return null;
    }

    Console.Error.WriteLine("rw: starting Repo Workshop…");
    // Launch through Explorer so the app runs as a normal desktop process. If rw itself was started
    // by a packaged (MSIX) app such as an AI agent's host, a direct child would inherit that package's
    // file-system virtualization and read a different settings file than the user's own copy.
    Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { exe }, UseShellExecute = false })?.Dispose();
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (DateTime.UtcNow < deadline)
    {
        client = await ApiClient.TryConnectAsync(command.PipeName, TimeSpan.FromSeconds(1), ct);
        if (client is not null)
            return client;
    }
    Console.Error.WriteLine("rw: Repo Workshop did not start listening within 30 seconds.");
    return null;
}

static string? FindApp()
{
    string[] candidates =
    [
        Path.Combine(AppContext.BaseDirectory, "RepoWorkshop.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RepoWorkshop", "RepoWorkshop.exe"),
    ];
    return candidates.FirstOrDefault(File.Exists);
}
