using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoWorkshop.Api;

/// <summary>A connection to a running Repo Workshop instance.</summary>
public sealed class ApiClient : IAsyncDisposable {
    /// <summary>The connected pipe.</summary>
    private readonly NamedPipeClientStream _pipe;

    /// <summary>Reads response lines.</summary>
    private readonly StreamReader _reader;

    /// <summary>Writes request lines.</summary>
    private readonly StreamWriter _writer;

    /// <summary>The id of the last request sent.</summary>
    private int _nextId;

    /// <summary>Wraps an already-connected pipe.</summary>
    private ApiClient(NamedPipeClientStream pipe) {
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>Connects to the app, or returns null if it is not listening within the timeout.</summary>
    public static async Task<ApiClient?> TryConnectAsync(string? pipeName = null, TimeSpan? timeout = null, CancellationToken ct = default) {
        NamedPipeClientStream pipe = new(".", pipeName ?? ApiProtocol.DefaultPipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try {
            await pipe.ConnectAsync(timeout ?? TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            return new ApiClient(pipe);
        } catch (TimeoutException) {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>Calls a method and returns its result; throws <see cref="ApiException"/> on an error response.</summary>
    public async Task<JsonNode?> CallAsync(string method, JsonObject? parameters = null, CancellationToken ct = default) {
        int id = Interlocked.Increment(ref _nextId);
        ApiRequest request = new() { Id = id, Method = method, Params = parameters };
        await _writer.WriteLineAsync(JsonSerializer.Serialize(request, ApiProtocol.JsonOptions).AsMemory(), ct).ConfigureAwait(false);

        string line = await _reader.ReadLineAsync(ct).ConfigureAwait(false)
                      ?? throw new IOException("Repo Workshop closed the connection.");
        ApiResponse response = JsonSerializer.Deserialize<ApiResponse>(line, ApiProtocol.JsonOptions)
                               ?? throw new IOException("Empty response from Repo Workshop.");
        if (response.Error is { } error) {
            throw new ApiException(error.Code, error.Message);
        }
        return response.Result;
    }

    /// <summary>Closes the connection.</summary>
    public async ValueTask DisposeAsync() {
        _reader.Dispose();
        await _writer.DisposeAsync().ConfigureAwait(false);
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}
