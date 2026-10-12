using System.Text.Json.Nodes;
using RepoWorkshop.Api;
using RepoWorkshop.Cli;

namespace RepoWorkshop.Tests;

public class ApiPipeTests
{
    private static string UniquePipe() => $"RepoWorkshop.Tests.{Guid.NewGuid():N}";

    private static Task<JsonNode?> Dispatch(string method, JsonObject p, CancellationToken ct) => method switch
    {
        "echo" => Task.FromResult<JsonNode?>(p.DeepClone()),
        "fail" => throw new ApiException(ApiErrorCodes.Declined, "nope"),
        "crash" => throw new InvalidOperationException("boom"),
        "nothing" => Task.FromResult<JsonNode?>(null),
        _ => throw new ApiException(ApiErrorCodes.MethodNotFound, $"Unknown method \"{method}\"."),
    };

    [Fact]
    public async Task RoundTripsRequestsOverOneConnection()
    {
        var pipe = UniquePipe();
        await using var server = new ApiServer(pipe, Dispatch);
        server.Start();

        await using var client = await ApiClient.TryConnectAsync(pipe, TimeSpan.FromSeconds(5));
        Assert.NotNull(client);

        var first = await client.CallAsync("echo", new JsonObject { ["a"] = 1, ["b"] = "two" });
        Assert.Equal(1, first!["a"]!.GetValue<int>());
        Assert.Equal("two", first["b"]!.GetValue<string>());

        // A null result comes back as true, so callers can tell success from "no response".
        Assert.True((await client.CallAsync("nothing"))!.GetValue<bool>());
    }

    [Fact]
    public async Task ReturnsErrorsAsApiExceptions()
    {
        var pipe = UniquePipe();
        await using var server = new ApiServer(pipe, Dispatch);
        server.Start();
        await using var client = await ApiClient.TryConnectAsync(pipe, TimeSpan.FromSeconds(5));

        var declined = await Assert.ThrowsAsync<ApiException>(() => client!.CallAsync("fail"));
        Assert.Equal(ApiErrorCodes.Declined, declined.Code);

        var crashed = await Assert.ThrowsAsync<ApiException>(() => client!.CallAsync("crash"));
        Assert.Equal(ApiErrorCodes.InternalError, crashed.Code);
        Assert.Contains("boom", crashed.Message);

        // The connection survives errors.
        Assert.NotNull(await client!.CallAsync("echo", new JsonObject()));
    }

    [Fact]
    public async Task ServesSeveralClientsAtOnce()
    {
        var pipe = UniquePipe();
        await using var server = new ApiServer(pipe, Dispatch);
        server.Start();

        var calls = Enumerable.Range(0, 4).Select(async i =>
        {
            await using var client = await ApiClient.TryConnectAsync(pipe, TimeSpan.FromSeconds(5));
            var result = await client!.CallAsync("echo", new JsonObject { ["i"] = i });
            return result!["i"]!.GetValue<int>();
        });
        Assert.Equal([0, 1, 2, 3], (await Task.WhenAll(calls)).Order());
    }

    [Fact]
    public async Task ConnectReturnsNullWhenNothingIsListening()
    {
        Assert.Null(await ApiClient.TryConnectAsync(UniquePipe(), TimeSpan.FromMilliseconds(200)));
    }
}

public class CommandLineTests
{
    [Fact]
    public void ParsesMethodValuesFlagsAndLists()
    {
        var cmd = CommandLine.Parse(["repo.stage", "--repo", "api", "--paths", "a.txt", "--paths", "b c.txt", "--all"]);

        Assert.Equal("repo.stage", cmd.Method);
        Assert.Equal("api", cmd.Params["repo"]!.GetValue<string>());
        Assert.Equal(["a.txt", "b c.txt"], cmd.Params["paths"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(cmd.Params["all"]!.GetValue<bool>());
    }

    [Fact]
    public void SupportsEqualsSyntaxKebabCaseAndJson()
    {
        var cmd = CommandLine.Parse(["repo.stash", "--message=--odd message", "--include-untracked", "false",
            "--json", "{\"repo\":\"web\"}"]);

        Assert.Equal("--odd message", cmd.Params["message"]!.GetValue<string>());
        Assert.Equal("false", cmd.Params["includeUntracked"]!.GetValue<string>());
        Assert.Equal("web", cmd.Params["repo"]!.GetValue<string>());
    }

    [Fact]
    public void HandlesHelpAndClientOptions()
    {
        Assert.True(CommandLine.Parse([]).IsHelp);
        var help = CommandLine.Parse(["help", "repo.commit", "--raw"]);
        Assert.True(help.IsHelp);
        Assert.True(help.Raw);
        Assert.Equal("repo.commit", help.Params["method"]!.GetValue<string>());

        var cmd = CommandLine.Parse(["app.info", "--no-launch", "--timeout", "5", "--pipe", "x"]);
        Assert.True(cmd.NoLaunch);
        Assert.Equal(TimeSpan.FromSeconds(5), cmd.Timeout);
        Assert.Equal("x", cmd.PipeName);
        Assert.Empty(cmd.Params);
    }

    [Fact]
    public void RejectsStrayArguments()
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse(["repo.status", "extra", "more"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["repo.status", "--json", "[1]"]));
    }
}
