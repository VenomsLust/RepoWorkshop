using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using RepoWorkshop.Api;
using RepoWorkshop.Models;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Automation;

/// <summary>
/// One API request: typed access to its parameters, and resolution of the project and
/// repository it refers to.
/// </summary>
public sealed class ApiCall(string method, JsonObject parameters, MainViewModel main, AppServices services)
{
    /// <summary>Sent by the rw command so a repo can be inferred from the caller's working directory.</summary>
    public const string CwdParam = "cwd";

    public string Method { get; } = method;

    public MainViewModel Main { get; } = main;

    public AppServices Services { get; } = services;

    // ---- Parameters ---------------------------------------------------------------------------

    public bool Has(string name) => parameters[name] is not null;

    public string? String(string name)
    {
        var node = parameters[name];
        return node switch
        {
            null => null,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            JsonValue v => v.ToJsonString(),
            _ => throw Invalid($"\"{name}\" must be a string."),
        };
    }

    public string RequiredString(string name) =>
        String(name) is { Length: > 0 } value ? value : throw Invalid($"Missing required parameter \"{name}\".");

    public bool Bool(string name, bool defaultValue = false)
    {
        var node = parameters[name];
        if (node is null)
            return defaultValue;
        if (node is JsonValue v)
        {
            switch (v.GetValueKind())
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String when bool.TryParse(v.GetValue<string>(), out var b): return b;
            }
        }
        throw Invalid($"\"{name}\" must be true or false.");
    }

    public int Int(string name, int defaultValue)
    {
        var node = parameters[name];
        if (node is null)
            return defaultValue;
        if (node is JsonValue v)
        {
            if (v.GetValueKind() == JsonValueKind.Number)
                return v.GetValue<int>();
            if (v.GetValueKind() == JsonValueKind.String && int.TryParse(v.GetValue<string>(), out var n))
                return n;
        }
        throw Invalid($"\"{name}\" must be a whole number.");
    }

    /// <summary>A list parameter; a single string is accepted as a one-item list.</summary>
    public IReadOnlyList<string> StringList(string name)
    {
        return parameters[name] switch
        {
            null => [],
            JsonArray a => a.Select(n => n?.GetValue<string>() ?? throw Invalid($"\"{name}\" must not contain nulls.")).ToList(),
            JsonValue v when v.GetValueKind() == JsonValueKind.String => [v.GetValue<string>()],
            _ => throw Invalid($"\"{name}\" must be a list of strings."),
        };
    }

    public static ApiException Invalid(string message) => new(ApiErrorCodes.InvalidParams, message);

    public static ApiException NotFound(string message) => new(ApiErrorCodes.NotFound, message);

    // ---- Projects and repositories ------------------------------------------------------------

    /// <summary>
    /// The project named by "project" (name or id). Without it, the project containing the repo
    /// named by "repo" or the caller's working directory, if any.
    /// </summary>
    public ProjectViewModel Project()
    {
        if (String("project") is { Length: > 0 } key)
            return FindProject(key) ?? throw NotFound($"No project named \"{key}\". Projects: {ProjectNames()}.");

        var repo = TryRepo();
        return repo is not null
            ? Main.Projects.First(p => p.Repos.Contains(repo))
            : throw Invalid($"Specify \"project\" (one of: {ProjectNames()}).");
    }

    private ProjectViewModel? FindProject(string key) =>
        Main.Projects.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
        ?? Main.Projects.FirstOrDefault(p => p.Model.Id.ToString().Equals(key, StringComparison.OrdinalIgnoreCase));

    private string ProjectNames() =>
        Main.Projects.Count == 0 ? "(none)" : string.Join(", ", Main.Projects.Select(p => $"\"{p.Name}\""));

    /// <summary>The repository named by "repo" (name, alias or path), or containing the caller's working directory.</summary>
    public RepoViewModel Repo() =>
        TryRepo() ?? throw Invalid(
            "Specify \"repo\" (a repository name or path), or run from inside a repository that belongs to a project. " +
            "Use project.list to see them, or project.addRepo to add one.");

    private RepoViewModel? TryRepo()
    {
        var scope = String("project") is { Length: > 0 } key
            ? [FindProject(key) ?? throw NotFound($"No project named \"{key}\". Projects: {ProjectNames()}.")]
            : Main.Projects.ToList();
        var repos = scope.SelectMany(p => p.Repos).ToList();

        if (String("repo") is { Length: > 0 } repoKey)
        {
            var byPath = repos.Where(r => PathsEqual(r.Path, repoKey)).ToList();
            if (byPath.Count > 0)
                return byPath[0];

            var byName = repos.Where(r => string.Equals(r.Name, repoKey, StringComparison.OrdinalIgnoreCase)).ToList();
            var distinctPaths = byName.Select(r => Normalize(r.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return distinctPaths.Count switch
            {
                0 => throw NotFound($"No repository \"{repoKey}\"{(Has("project") ? $" in project \"{scope[0].Name}\"" : "")}. " +
                                    "Use project.list to see the registered repositories."),
                1 => byName[0],
                _ => throw Invalid($"\"{repoKey}\" matches several repositories ({string.Join(", ", distinctPaths)}); " +
                                   "pass its path or a \"project\"."),
            };
        }

        // Fall back to the repository containing the caller's working directory (the deepest match).
        if (String(CwdParam) is { Length: > 0 } cwd)
        {
            return repos
                .Where(r => IsSameOrUnder(cwd, r.Path))
                .OrderByDescending(r => r.Path.Length)
                .FirstOrDefault();
        }
        return null;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        var p = Normalize(path) + Path.DirectorySeparatorChar;
        var r = Normalize(root) + Path.DirectorySeparatorChar;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Confirmation -------------------------------------------------------------------------

    /// <summary>Asks the user in the app to approve a destructive request; throws if they decline.</summary>
    public async Task ConfirmAsync(string title, string description, string actionText)
    {
        Services.Dialogs.BringToFront();
        var approved = await Services.Dialogs.ConfirmAsync($"{title} (requested via API)",
            $"A program using the Repo Workshop API (such as an AI agent) wants to:\n\n{description}", actionText);
        if (!approved)
            throw new ApiException(ApiErrorCodes.Declined, $"The user declined: {title}.");
    }

    /// <summary>Throws an error carrying git's message if the result failed.</summary>
    public static void EnsureSuccess(GitResult result, string what)
    {
        if (!result.Success)
            throw new ApiException(ApiErrorCodes.OperationFailed, $"{what} failed: {result.Message}");
    }
}
