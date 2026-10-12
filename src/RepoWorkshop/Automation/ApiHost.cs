using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using RepoWorkshop.Api;
using RepoWorkshop.Services;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Automation;

/// <summary>
/// Hosts the named-pipe API: owns the server's lifetime and runs each request on the UI thread
/// against the same view models the window uses, so the UI always reflects what was done.
/// The methods themselves are defined in ApiHost.Methods.cs.
/// </summary>
public sealed partial class ApiHost : IAsyncDisposable {
    /// <summary>The app's main view model.</summary>
    private readonly MainViewModel _main;

    /// <summary>Shared app services.</summary>
    private readonly AppServices _services;

    /// <summary>The pipe to serve on.</summary>
    private readonly string _pipeName;

    /// <summary>The methods by name (case-insensitive).</summary>
    private readonly Dictionary<string, ApiMethodInfo> _methods;

    /// <summary>The running server, while the API is enabled.</summary>
    private ApiServer? _server;

    /// <summary>Creates the host; call <see cref="ApplySettingsAsync"/> to start serving.</summary>
    public ApiHost(MainViewModel main, AppServices services, string? pipeName = null) {
        _main = main;
        _services = services;
        _pipeName = pipeName ?? ApiProtocol.DefaultPipeName;
        _methods = BuildMethods().ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The app version, reported by app.info.</summary>
    public static string Version =>
        typeof(ApiHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>All API methods.</summary>
    public IReadOnlyCollection<ApiMethodInfo> Methods => _methods.Values;

    /// <summary>Starts or stops the server to match the current settings.</summary>
    public async Task ApplySettingsAsync() {
        if (_services.Settings.ApiEnabled && _server is null) {
            _server = new ApiServer(_pipeName, DispatchAsync, message => _services.Log.Error(message));
            _server.Start();
            _main.ApiStatus = "API: on";
        } else if (!_services.Settings.ApiEnabled && _server is not null) {
            await _server.DisposeAsync();
            _server = null;
            _main.ApiStatus = "API: off";
        }
    }

    /// <summary>Stops the server.</summary>
    public async ValueTask DisposeAsync() {
        if (_server is not null) {
            await _server.DisposeAsync();
        }
        _server = null;
    }

    /// <summary>Validates a request's method and parameter names, then runs it on the UI thread.</summary>
    private Task<JsonNode?> DispatchAsync(string method, JsonObject parameters, CancellationToken ct) {
        if (!_methods.TryGetValue(method, out ApiMethodInfo? info)) {
            throw new ApiException(ApiErrorCodes.MethodNotFound, $"Unknown method \"{method}\". Call \"help\" for the list of methods.");
        }

        List<string> unknown = parameters.Select(p => p.Key)
            .Where(k => k != ApiCall.CwdParam && info.Params.All(p => !string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (unknown.Count > 0) {
            throw ApiCall.Invalid($"Unknown parameter(s) for {info.Name}: {string.Join(", ", unknown)}. " +
                                  $"Accepted: {(info.Params.Count == 0 ? "(none)" : string.Join(", ", info.Params.Select(p => p.Name)))}.");
        }

        // View models are UI-thread objects, so every handler runs there.
        return Dispatcher.UIThread.InvokeAsync(() => RunHandlerAsync(info, parameters));
    }

    /// <summary>Runs a method's handler and serializes its result; git failures become API errors.</summary>
    private async Task<JsonNode?> RunHandlerAsync(ApiMethodInfo info, JsonObject parameters) {
        if (info.ChangesState) {
            _services.Log.Info($"API: {info.Name}");
        }

        object? result;
        try {
            result = await info.Handler(new ApiCall(info.Name, parameters, _main, _services));
        } catch (GitException ex) {
            throw new ApiException(ApiErrorCodes.OperationFailed, ex.Message);
        }
        return result is null ? null : JsonSerializer.SerializeToNode(result, ApiProtocol.JsonOptions);
    }
}
