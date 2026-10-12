using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoWorkshop.Cli;

/// <summary>The parsed form of an <c>rw</c> command line.</summary>
public sealed record CommandLine(
    string? Method,
    JsonObject Params,
    bool NoLaunch,
    bool Raw,
    TimeSpan Timeout,
    string? PipeName)
{
    public bool IsHelp => Method is null or "help";

    /// <summary>
    /// Parses <c>rw &lt;method&gt; [--name value | --flag]... [--json '{...}']</c>.
    /// Repeating an option makes a list; <c>--some-name</c> becomes <c>someName</c>.
    /// </summary>
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        string? method = null;
        var parameters = new JsonObject();
        var noLaunch = false;
        var raw = false;
        var timeout = TimeSpan.FromMinutes(10);
        string? pipe = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (method is not null)
                {
                    // "rw help repo.commit" -> help for one method.
                    if (method == "help" && !parameters.ContainsKey("method"))
                    {
                        parameters["method"] = arg;
                        continue;
                    }
                    throw new UsageException($"Unexpected argument \"{arg}\". Parameters are passed as --name value.");
                }
                method = arg is "-h" or "/?" ? "help" : arg;
                continue;
            }

            // --name=value or --name value or --flag
            var name = arg[2..];
            string? value = null;
            if (name.IndexOf('=') is var eq and > 0)
            {
                value = name[(eq + 1)..];
                name = name[..eq];
            }
            else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            switch (name)
            {
                case "":
                    throw new UsageException("Empty option name \"--\".");
                case "help":
                    method ??= "help";
                    continue;
                case "no-launch":
                    noLaunch = value is null || bool.Parse(value);
                    continue;
                case "raw":
                    raw = value is null || bool.Parse(value);
                    continue;
                case "timeout":
                    timeout = TimeSpan.FromSeconds(int.Parse(value ?? throw new UsageException("--timeout needs a number of seconds.")));
                    continue;
                case "pipe":
                    pipe = value ?? throw new UsageException("--pipe needs a name.");
                    continue;
                case "json":
                    MergeJson(parameters, value ?? throw new UsageException("--json needs a JSON object."));
                    continue;
            }

            Add(parameters, ToCamelCase(name), value is null ? JsonValue.Create(true) : JsonValue.Create(value));
        }

        return new CommandLine(method, parameters, noLaunch, raw, timeout, pipe);
    }

    private static void Add(JsonObject parameters, string name, JsonNode node)
    {
        switch (parameters[name])
        {
            case null when !parameters.ContainsKey(name):
                parameters[name] = node;
                break;
            case JsonArray list:
                list.Add(node);
                break;
            case var existing:
                parameters[name] = new JsonArray(existing!.DeepClone(), node);
                break;
        }
    }

    private static void MergeJson(JsonObject parameters, string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new UsageException($"--json is not valid JSON: {ex.Message}");
        }
        if (node is not JsonObject obj)
            throw new UsageException("--json must be a JSON object, e.g. --json '{\"paths\":[\"a.txt\"]}'.");
        foreach (var (key, value) in obj.ToList())
        {
            obj.Remove(key);
            parameters[key] = value;
        }
    }

    /// <summary>include-untracked -> includeUntracked; names without dashes are unchanged.</summary>
    internal static string ToCamelCase(string name)
    {
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1
            ? name
            : parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}

public sealed class UsageException(string message) : Exception(message);
