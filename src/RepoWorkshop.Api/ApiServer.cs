using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoWorkshop.Api;

/// <summary>
/// Accepts connections on a current-user-only named pipe and hands each request to a dispatcher.
/// Several clients can be connected at once; requests on one connection are handled in order.
/// </summary>
public sealed class ApiServer : IAsyncDisposable {
    /// <summary>Maximum simultaneous client connections.</summary>
    private const int MaxInstances = 8;

    /// <summary>The pipe to listen on.</summary>
    private readonly string _pipeName;

    /// <summary>Runs a method and returns its result.</summary>
    private readonly Func<string, JsonObject, CancellationToken, Task<JsonNode?>> _dispatch;

    /// <summary>Optional sink for unexpected errors.</summary>
    private readonly Action<string>? _logError;

    /// <summary>Cancelled when the server shuts down.</summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>The loop accepting new connections, once started.</summary>
    private Task? _acceptLoop;

    /// <summary>Creates a server; call <see cref="Start"/> to begin listening.</summary>
    /// <param name="pipeName">The pipe to listen on.</param>
    /// <param name="dispatch">Runs a method with its params and returns the result (or throws <see cref="ApiException"/>).</param>
    /// <param name="logError">Optional sink for unexpected errors.</param>
    public ApiServer(string pipeName, Func<string, JsonObject, CancellationToken, Task<JsonNode?>> dispatch, Action<string>? logError = null) {
        _pipeName = pipeName;
        _dispatch = dispatch;
        _logError = logError;
    }

    /// <summary>The pipe this server listens on.</summary>
    public string PipeName => _pipeName;

    /// <summary>Starts accepting connections in the background.</summary>
    public void Start() => _acceptLoop ??= Task.Run(AcceptLoopAsync);

    /// <summary>Stops listening and waits for the accept loop to finish.</summary>
    public async ValueTask DisposeAsync() {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_acceptLoop is not null) {
            try {
                await _acceptLoop.ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Expected on shutdown.
            }
        }
        _cts.Dispose();
    }

    /// <summary>Parses one request line, dispatches it and builds the response.</summary>
    internal async Task<ApiResponse> HandleLineAsync(string line) {
        ApiRequest? request;
        try {
            request = JsonSerializer.Deserialize<ApiRequest>(line, ApiProtocol.JsonOptions);
        } catch (JsonException ex) {
            return Error(null, ApiErrorCodes.ParseError, $"Invalid JSON: {ex.Message}");
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Method)) {
            return Error(request?.Id, ApiErrorCodes.InvalidRequest, "Request must have a \"method\".");
        }

        try {
            JsonNode? result = await _dispatch(request.Method, request.Params ?? [], _cts.Token).ConfigureAwait(false);
            return new ApiResponse { Id = request.Id, Result = result ?? JsonValue.Create(true) };
        } catch (ApiException ex) {
            return Error(request.Id, ex.Code, ex.Message);
        } catch (Exception ex) {
            _logError?.Invoke($"API {request.Method} failed: {ex}");
            return Error(request.Id, ApiErrorCodes.InternalError, ex.Message);
        }
    }

    /// <summary>Creates a pipe instance per client and hands each connection off.</summary>
    private async Task AcceptLoopAsync() {
        while (!_cts.IsCancellationRequested) {
            NamedPipeServerStream pipe;
            try {
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // All instances busy, or another process owns the name: back off and retry.
                _logError?.Invoke($"API pipe unavailable: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false);
                continue;
            }

            try {
                await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }

            _ = Task.Run(() => HandleConnectionAsync(pipe));
        }
    }

    /// <summary>Serves requests on one connection until the client disconnects.</summary>
    private async Task HandleConnectionAsync(NamedPipeServerStream pipe) {
        await using NamedPipeServerStream connection = pipe;
        using StreamReader reader = new(connection, new UTF8Encoding(false), leaveOpen: true);
        await using StreamWriter writer = new(connection, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        try {
            while (!_cts.IsCancellationRequested && await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false) is { } line) {
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }
                ApiResponse response = await HandleLineAsync(line).ConfigureAwait(false);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, ApiProtocol.JsonOptions)).ConfigureAwait(false);
            }
        } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) {
            // Client went away or the server is shutting down.
        }
    }

    /// <summary>Builds an error response.</summary>
    private static ApiResponse Error(JsonNode? id, int code, string message) =>
        new() { Id = id, Error = new ApiError { Code = code, Message = message } };
}
