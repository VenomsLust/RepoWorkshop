using RepoWorkshop.Cli;

namespace RepoWorkshop.Tests;

/// <summary>Parsing of <c>rw</c> command lines.</summary>
public class CommandLineTests {
    /// <summary>Method, string values, repeated options as lists, and bare flags as true.</summary>
    [Fact]
    public void ParsesMethodValuesFlagsAndLists() {
        CommandLine cmd = CommandLine.Parse(["repo.stage", "--repo", "api", "--paths", "a.txt", "--paths", "b c.txt", "--all"]);

        Assert.Equal("repo.stage", cmd.Method);
        Assert.Equal("api", cmd.Params["repo"]!.GetValue<string>());
        Assert.Equal(["a.txt", "b c.txt"], cmd.Params["paths"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(cmd.Params["all"]!.GetValue<bool>());
    }

    /// <summary>--name=value, kebab-case names, and --json parameters.</summary>
    [Fact]
    public void SupportsEqualsSyntaxKebabCaseAndJson() {
        CommandLine cmd = CommandLine.Parse(["repo.stash", "--message=--odd message", "--include-untracked", "false",
            "--json", "{\"repo\":\"web\"}"]);

        Assert.Equal("--odd message", cmd.Params["message"]!.GetValue<string>());
        Assert.Equal("false", cmd.Params["includeUntracked"]!.GetValue<string>());
        Assert.Equal("web", cmd.Params["repo"]!.GetValue<string>());
    }

    /// <summary>Help forms, and client-only options that are not sent as parameters.</summary>
    [Fact]
    public void HandlesHelpAndClientOptions() {
        Assert.True(CommandLine.Parse([]).IsHelp);
        CommandLine help = CommandLine.Parse(["help", "repo.commit", "--raw"]);
        Assert.True(help.IsHelp);
        Assert.True(help.Raw);
        Assert.Equal("repo.commit", help.Params["method"]!.GetValue<string>());

        CommandLine cmd = CommandLine.Parse(["app.info", "--no-launch", "--timeout", "5", "--pipe", "x"]);
        Assert.True(cmd.NoLaunch);
        Assert.Equal(TimeSpan.FromSeconds(5), cmd.Timeout);
        Assert.Equal("x", cmd.PipeName);
        Assert.Empty(cmd.Params);
    }

    /// <summary>Positional arguments after the method, and non-object JSON, are rejected.</summary>
    [Fact]
    public void RejectsStrayArguments() {
        Assert.Throws<UsageException>(() => CommandLine.Parse(["repo.status", "extra", "more"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["repo.status", "--json", "[1]"]));
    }
}
