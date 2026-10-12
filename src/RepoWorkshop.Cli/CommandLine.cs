using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoWorkshop.Cli;

/// <summary>The parsed form of an <c>rw</c> command line.</summary>
/// <param name="Method">The API method to call; null when none was given (shows help).</param>
/// <param name="Params">The method's parameters, built from the --name value options.</param>
/// <param name="NoLaunch">Don't start the app if it isn't running.</param>
/// <param name="Raw">Print help as raw JSON instead of formatted text.</param>
/// <param name="Timeout">How long to wait for the call to finish.</param>
/// <param name="PipeName">Pipe to connect to; null for the default.</param>
public sealed record CommandLine(
    string? Method,
    JsonObject Params,
    bool NoLaunch,
    bool Raw,
    TimeSpan Timeout,
    string? PipeName) {
    /// <summary>Whether this command asks for help rather than calling a method.</summary>
    public bool IsHelp => Method is null or "help";

    /// <summary>
    /// Parses <c>rw &lt;method&gt; [--name value | --flag]... [--json '{...}']</c>.
    /// Repeating an option makes a list; <c>--some-name</c> becomes <c>someName</c>.
    /// <c>--message-file</c> and <c>--json-file</c> read a value from a file, or standard input for "-",
    /// which avoids shell quoting problems with multi-line text.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="readText">Reads a file's text ("-" for standard input); replaceable for tests.</param>
    public static CommandLine Parse(IReadOnlyList<string> args, Func<string, string>? readText = null) {
        readText ??= ReadTextFromFileOrStdin;
        string? method = null;
        JsonObject parameters = new();
        bool noLaunch = false;
        bool raw = false;
        TimeSpan timeout = TimeSpan.FromMinutes(10);
        string? pipe = null;

        for (int i = 0; i < args.Count; i++) {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                if (method is not null) {
                    // "rw help repo.commit" -> help for one method.
                    if (method == "help" && !parameters.ContainsKey("method")) {
                        parameters["method"] = arg;
                        continue;
                    }
                    throw new UsageException($"Unexpected argument \"{arg}\". Parameters are passed as --name value.");
                }
                method = arg is "-h" or "/?" ? "help" : arg;
                continue;
            }

            // --name=value or --name value or --flag
            string name = arg[2..];
            string? value = null;
            if (name.IndexOf('=') is int eq and > 0) {
                value = name[(eq + 1)..];
                name = name[..eq];
            } else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                value = args[++i];
            }

            switch (name) {
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
                case "json-file":
                    MergeJson(parameters, ReadOrThrow(readText, value, name));
                    continue;
                case "message-file":
                    parameters["message"] = ReadOrThrow(readText, value, name);
                    continue;
            }

            Add(parameters, ToCamelCase(name), value is null ? JsonValue.Create(true) : JsonValue.Create(value));
        }

        return new CommandLine(method, parameters, noLaunch, raw, timeout, pipe);
    }

    /// <summary>Converts include-untracked to includeUntracked; names without dashes are unchanged.</summary>
    internal static string ToCamelCase(string name) {
        string[] parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1
            ? name
            : parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// <summary>Reads the file named by an option's value, turning failures into usage errors.</summary>
    private static string ReadOrThrow(Func<string, string> readText, string? path, string option) {
        if (path is null) {
            throw new UsageException($"--{option} needs a file path, or - for standard input.");
        }
        try {
            return readText(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            throw new UsageException($"--{option}: can't read \"{path}\": {ex.Message}");
        }
    }

    /// <summary>Reads a file as text, or all of standard input for "-".</summary>
    private static string ReadTextFromFileOrStdin(string path) => path == "-" ? Console.In.ReadToEnd() : File.ReadAllText(path);

    /// <summary>Adds a parameter value; a repeated name turns into a list.</summary>
    private static void Add(JsonObject parameters, string name, JsonNode node) {
        switch (parameters[name]) {
            case null when !parameters.ContainsKey(name):
                parameters[name] = node;
                break;
            case JsonArray list:
                list.Add(node);
                break;
            case JsonNode existing:
                parameters[name] = new JsonArray(existing.DeepClone(), node);
                break;
        }
    }

    /// <summary>Merges the properties of a --json object into the parameters.</summary>
    private static void MergeJson(JsonObject parameters, string json) {
        JsonNode? node;
        try {
            node = JsonNode.Parse(json);
        } catch (JsonException ex) {
            throw new UsageException($"--json is not valid JSON: {ex.Message}");
        }
        if (node is not JsonObject obj) {
            throw new UsageException("--json must be a JSON object, e.g. --json '{\"paths\":[\"a.txt\"]}'.");
        }

        foreach ((string key, JsonNode? value) in obj.ToList()) {
            obj.Remove(key);
            parameters[key] = value;
        }
    }
}
