using System.Text.Json.Nodes;
using RepoWorkshop.Api;

namespace RepoWorkshop.Tests;

/// <summary><see cref="ApiServer"/> and <see cref="ApiClient"/> talking over a real named pipe.</summary>
public class ApiPipeTests {
    /// <summary>One request over a connection; a null result comes back as true.</summary>
    [Fact]
    public async Task RoundTripsRequestsOverOneConnection() {
        string pipe = UniquePipe();
        await using ApiServer server = new(pipe, Dispatch);
        server.Start();

        await using ApiClient? client = await ApiClient.TryConnectAsync(pipe, TimeSpan.FromSeconds(5));
        Assert.NotNull(client);

        JsonNode? first = await client.CallAsync("echo", new JsonObject { ["a"] = 1, ["b"] = "two" });
        Assert.Equal(1, first!["a"]!.GetValue<int>());
        Assert.Equal("two", first["b"]!.GetValue<string>());

        // A null result comes back as true, so callers can tell success from "no response".
        Assert.True((await client.CallAsync("nothing"))!.GetValue<bool>());
    }

    /// <summary>Handler errors become <see cref="ApiException"/>s, and the connection survives them.</summary>
    [Fact]
    public async Task ReturnsErrorsAsApiExceptions() {
        string pipe = UniquePipe();
        await using ApiServer server = new(pipe, Dispatch);
        server.Start();
        await using ApiClient? client = await ApiClient.TryConnectAsync(pipe, TimeSpan.FromSeconds(5));

        ApiException declined = await Assert.ThrowsAsync<ApiException>(() => client!.CallAsync("fail"));
        Assert.Equal(ApiErrorCodes.Declined, declined.Code);

        ApiException crashed = await Assert.ThrowsAsync<ApiException>(() => client!.CallAsync("crash"));
        Assert.Equal(ApiErrorCodes.InternalError, crashed.Code);
        Assert.Contains("boom", crashed.Message);

        Assert.NotNull(await client!.CallAsync("echo", new JsonObject()));
    }

    /// <summary>Several clients connected at the same time.</summary>
    [Fact]
    public async Task ServesSeveralClientsAtOnce() {
        string pipe = UniquePipe();
        await using ApiServer server = new(pipe, Dispatch);
        server.Start();

        IEnumerable<Task<int>> calls = Enumerable.Range(0, 4).Select(async i => {
            await using ApiClient? client = await ApiClient.TryConnectAsync(pipe, TimeSpan.FromSeconds(5));
            JsonNode? result = await client!.CallAsync("echo", new JsonObject { ["i"] = i });
            return result!["i"]!.GetValue<int>();
        });
        Assert.Equal([0, 1, 2, 3], (await Task.WhenAll(calls)).Order());
    }

    /// <summary>Connecting with no server gives null rather than an exception.</summary>
    [Fact]
    public async Task ConnectReturnsNullWhenNothingIsListening() {
        Assert.Null(await ApiClient.TryConnectAsync(UniquePipe(), TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>A pipe name no other test (or app) uses.</summary>
    private static string UniquePipe() => $"RepoWorkshop.Tests.{Guid.NewGuid():N}";

    /// <summary>A test dispatcher: echo, fail (declined), crash (unexpected error) and nothing (null result).</summary>
    private static Task<JsonNode?> Dispatch(string method, JsonObject p, CancellationToken ct) => method switch {
        "echo" => Task.FromResult<JsonNode?>(p.DeepClone()),
        "fail" => throw new ApiException(ApiErrorCodes.Declined, "nope"),
        "crash" => throw new InvalidOperationException("boom"),
        "nothing" => Task.FromResult<JsonNode?>(null),
        _ => throw new ApiException(ApiErrorCodes.MethodNotFound, $"Unknown method \"{method}\"."),
    };
}
