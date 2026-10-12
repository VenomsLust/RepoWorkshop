using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoWorkshop.Api;

/// <summary>
/// Accepts connections on a current-user-only named pipe and hands each request to a dispatcher.
/// Several clients can be connected at once; requests on one connection are handled in order.
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private const int MaxInstances = 8;

    private readonly string _pipeName;
    private readonly Func<string, JsonObject, CancellationToken, Task<JsonNode?>> _dispatch;
    private readonly Action<string>? _logError;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    /// <param name="dispatch">Runs a method with its params and returns the result (or throws <see cref="ApiException"/>).</param>
    public ApiServer(string pipeName, Func<string, JsonObject, CancellationToken, Task<JsonNode?>> dispatch, Action<string>? logError = null)
    {
        _pipeName = pipeName;
        _dispatch = dispatch;
        _logError = logError;
    }

    public string PipeName => _pipeName;

    public void Start() => _acceptLoop ??= Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // All instances busy, or another process owns the name: back off and retry.
                _logError?.Invoke($"API pipe unavailable: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }

            _ = Task.Run(() => HandleConnectionAsync(pipe));
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        await using var _ = pipe;
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        try
        {
            while (!_cts.IsCancellationRequested && await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var response = await HandleLineAsync(line).ConfigureAwait(false);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, ApiProtocol.JsonOptions)).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Client went away or the server is shutting down.
        }
    }

    internal async Task<ApiResponse> HandleLineAsync(string line)
    {
        ApiRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ApiRequest>(line, ApiProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            return Error(null, ApiErrorCodes.ParseError, $"Invalid JSON: {ex.Message}");
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Method))
            return Error(request?.Id, ApiErrorCodes.InvalidRequest, "Request must have a \"method\".");

        try
        {
            var result = await _dispatch(request.Method, request.Params ?? [], _cts.Token).ConfigureAwait(false);
            return new ApiResponse { Id = request.Id, Result = result ?? JsonValue.Create(true) };
        }
        catch (ApiException ex)
        {
            return Error(request.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logError?.Invoke($"API {request.Method} failed: {ex}");
            return Error(request.Id, ApiErrorCodes.InternalError, ex.Message);
        }
    }

    private static ApiResponse Error(JsonNode? id, int code, string message) =>
        new() { Id = id, Error = new ApiError { Code = code, Message = message } };

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}
