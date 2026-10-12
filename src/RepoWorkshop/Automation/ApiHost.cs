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

/// <summary>A parameter of an API method, as listed by <c>help</c>.</summary>
public sealed record ApiParamInfo(string Name, string Type, bool Required, string Description);

/// <summary>An API method: its documentation and handler.</summary>
public sealed record ApiMethodInfo(
    string Name,
    string Summary,
    IReadOnlyList<ApiParamInfo> Params,
    bool ChangesState,
    Func<ApiCall, Task<object?>> Handler);

/// <summary>
/// Hosts the named-pipe API: owns the server's lifetime and runs each request on the UI thread
/// against the same view models the window uses, so the UI always reflects what was done.
/// </summary>
public sealed partial class ApiHost : IAsyncDisposable
{
    private readonly MainViewModel _main;
    private readonly AppServices _services;
    private readonly string _pipeName;
    private readonly Dictionary<string, ApiMethodInfo> _methods;
    private ApiServer? _server;

    public ApiHost(MainViewModel main, AppServices services, string? pipeName = null)
    {
        _main = main;
        _services = services;
        _pipeName = pipeName ?? ApiProtocol.DefaultPipeName;
        _methods = BuildMethods().ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ApiMethodInfo> Methods => _methods.Values;

    public static string Version =>
        typeof(ApiHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>Starts or stops the server to match the current settings.</summary>
    public async Task ApplySettingsAsync()
    {
        if (_services.Settings.ApiEnabled && _server is null)
        {
            _server = new ApiServer(_pipeName, DispatchAsync, message => _services.Log.Error(message));
            _server.Start();
            _main.ApiStatus = "API: on";
        }
        else if (!_services.Settings.ApiEnabled && _server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
            _main.ApiStatus = "API: off";
        }
    }

    private Task<JsonNode?> DispatchAsync(string method, JsonObject parameters, CancellationToken ct)
    {
        if (!_methods.TryGetValue(method, out var info))
            throw new ApiException(ApiErrorCodes.MethodNotFound, $"Unknown method \"{method}\". Call \"help\" for the list of methods.");

        var unknown = parameters.Select(p => p.Key)
            .Where(k => k != ApiCall.CwdParam && info.Params.All(p => !string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (unknown.Count > 0)
        {
            throw ApiCall.Invalid($"Unknown parameter(s) for {info.Name}: {string.Join(", ", unknown)}. " +
                                  $"Accepted: {(info.Params.Count == 0 ? "(none)" : string.Join(", ", info.Params.Select(p => p.Name)))}.");
        }

        // View models are UI-thread objects, so every handler runs there.
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (info.ChangesState)
                _services.Log.Info($"API: {info.Name}");
            object? result;
            try
            {
                result = await info.Handler(new ApiCall(info.Name, parameters, _main, _services));
            }
            catch (GitException ex)
            {
                throw new ApiException(ApiErrorCodes.OperationFailed, ex.Message);
            }
            return result is null ? null : JsonSerializer.SerializeToNode(result, ApiProtocol.JsonOptions);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
            await _server.DisposeAsync();
        _server = null;
    }
}
