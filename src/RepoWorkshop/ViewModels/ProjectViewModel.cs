using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;

namespace RepoWorkshop.ViewModels;

/// <summary>A project: a named set of repositories, with actions that apply to all of them at once.</summary>
public partial class ProjectViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private bool _activated;

    public ProjectViewModel(ProjectModel model, AppServices services)
    {
        Model = model;
        _services = services;
        Name = model.Name;
        Repos = new ObservableCollection<RepoViewModel>(model.Repos.Select(r => new RepoViewModel(r, services)));
        Repos.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SummaryText));
        SelectedRepo = Repos.FirstOrDefault();
    }

    public ProjectModel Model { get; }

    public ObservableCollection<RepoViewModel> Repos { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRepoCommand), nameof(RenameRepoCommand))]
    public partial RepoViewModel? SelectedRepo { get; set; }

    public string SummaryText => Repos.Count == 1 ? "1 repository" : $"{Repos.Count} repositories";

    partial void OnNameChanged(string value) => Model.Name = value;

    partial void OnSelectedRepoChanged(RepoViewModel? value) => value?.EnsureLoadedAsync();

    /// <summary>Called when the project is shown; loads repository state the first time.</summary>
    public Task ActivateAsync()
    {
        if (_activated)
            return Task.CompletedTask;
        _activated = true;
        return RefreshAll();
    }

    [RelayCommand]
    private async Task AddRepoAsync()
    {
        var folder = await _services.Dialogs.PickFolderAsync("Select a git repository");
        if (folder is null)
            return;

        var (_, error) = await AddRepoByPathAsync(folder);
        if (error is not null)
            await _services.Dialogs.ShowMessageAsync("Add repository", error);
    }

    /// <summary>Adds the repository containing <paramref name="path"/>. Returns the repo, or an error message.</summary>
    public async Task<(RepoViewModel? Repo, string? Error)> AddRepoByPathAsync(string path)
    {
        var topLevel = await _services.Git.GetTopLevelAsync(path);
        if (topLevel is null)
            return (null, $"{path}\n\nis not inside a git repository.");
        if (!TryAddRepo(topLevel))
            return (null, $"{topLevel}\n\nis already in this project.");
        _services.SaveSettings();
        return (Repos.Last(), null);
    }

    /// <summary>Adds every git repository found directly in (or one level below) a chosen folder.</summary>
    [RelayCommand]
    private async Task AddReposFromFolderAsync()
    {
        var folder = await _services.Dialogs.PickFolderAsync("Select a folder containing git repositories");
        if (folder is null)
            return;

        var found = FindRepositories(folder, depth: 2).ToList();
        var added = found.Count(TryAddRepo);
        _services.SaveSettings();

        await _services.Dialogs.ShowMessageAsync("Add repositories",
            found.Count == 0
                ? $"No git repositories were found in\n{folder}"
                : $"Found {found.Count} repositories; added {added} new to \"{Name}\".");
    }

    private static IEnumerable<string> FindRepositories(string folder, int depth)
    {
        if (Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")))
        {
            yield return Path.GetFullPath(folder);
            yield break;
        }
        if (depth == 0)
            yield break;

        string[] children;
        try
        {
            children = Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var child in children.Order(StringComparer.OrdinalIgnoreCase))
        foreach (var repo in FindRepositories(child, depth - 1))
            yield return repo;
    }

    private bool TryAddRepo(string path)
    {
        if (Model.Repos.Any(r => string.Equals(Path.GetFullPath(r.Path), path, StringComparison.OrdinalIgnoreCase)))
            return false;

        var model = new RepoModel { Path = path };
        Model.Repos.Add(model);
        var repo = new RepoViewModel(model, _services);
        Repos.Add(repo);
        SelectedRepo = repo;
        _ = repo.RefreshAsync();
        return true;
    }

    private bool HasSelectedRepo => SelectedRepo is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedRepo))]
    private async Task RemoveRepoAsync()
    {
        var repo = SelectedRepo!;
        var confirmed = await _services.Dialogs.ConfirmAsync("Remove repository",
            $"Remove \"{repo.Name}\" from the project \"{Name}\"?\n\n{repo.Path}\n\nThe repository on disk is not affected.", "Remove");
        if (confirmed)
            RemoveRepo(repo);
    }

    /// <summary>Removes a repository from this project (the repository on disk is not touched).</summary>
    public void RemoveRepo(RepoViewModel repo)
    {
        var index = Repos.IndexOf(repo);
        Repos.Remove(repo);
        Model.Repos.Remove(repo.Model);
        _services.SaveSettings();
        if (SelectedRepo is null || SelectedRepo == repo)
            SelectedRepo = Repos.Count == 0 ? null : Repos[Math.Min(index, Repos.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRepo))]
    private async Task RenameRepoAsync()
    {
        var repo = SelectedRepo!;
        var alias = await _services.Dialogs.PromptAsync("Rename repository",
            "Display name (leave empty to use the folder name):", repo.Model.Alias ?? "");
        if (alias is null)
            return;
        repo.Model.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        repo.NotifyNameChanged();
        _services.SaveSettings();
    }

    [RelayCommand]
    private Task RefreshAll() => RefreshAllAsync();

    public Task RefreshAllAsync() => Task.WhenAll(Repos.Select(r => r.RefreshAsync()));

    [RelayCommand]
    private Task FetchAll() => FetchAllAsync();

    [RelayCommand]
    private Task PullAll() => PullAllAsync();

    public Task<IReadOnlyList<RepoOpResult>> FetchAllAsync() => RunBulkAsync("Fetch", Repos.ToList(), r => r.FetchAsync());

    public Task<IReadOnlyList<RepoOpResult>> PullAllAsync() => RunBulkAsync("Pull", Repos.ToList(), r => r.PullAsync());

    [RelayCommand]
    private async Task SwitchAllAsync()
    {
        var name = await _services.Dialogs.PromptAsync("Switch all repositories",
            "Branch to switch to in every repository that has it:", SelectedRepo?.CurrentBranch ?? "");
        if (!string.IsNullOrWhiteSpace(name))
            await SwitchAllAsync(name.Trim());
    }

    /// <summary>Switches every repository that has the given branch (locally or on a remote) to it.</summary>
    public async Task<IReadOnlyList<RepoOpResult>> SwitchAllAsync(string name)
    {
        await RefreshAllAsync();
        var (withBranch, without) = Partition(Repos, r => r.HasBranch(name));
        return await RunBulkAsync($"Switch to {name}", withBranch,
            r => r.CurrentBranch == name ? Task.FromResult(new GitResult(0, "Already on this branch.", "")) : r.SwitchToAsync(name),
            without.Select(r => new RepoOpResult(r.Name, RepoOpOutcome.Skipped, $"No branch \"{name}\".")));
    }

    [RelayCommand]
    private async Task CreateBranchAllAsync()
    {
        var name = await _services.Dialogs.PromptAsync("Create branch in all repositories",
            "Create this branch from the current HEAD of every repository, and switch to it.\n\nBranch name:");
        if (string.IsNullOrWhiteSpace(name) || Repos.Count == 0)
            return;
        name = name.Trim();

        if (!await _services.Git.IsValidBranchNameAsync(Repos[0].Path, name))
        {
            await _services.Dialogs.ShowMessageAsync("Create branch", $"\"{name}\" is not a valid branch name.");
            return;
        }
        await CreateBranchAllAsync(name);
    }

    /// <summary>Creates a branch from the current HEAD in every repository that lacks it, and switches to it.</summary>
    public async Task<IReadOnlyList<RepoOpResult>> CreateBranchAllAsync(string name)
    {
        await RefreshAllAsync();
        var (available, existing) = Partition(Repos, r => !r.LocalBranches.Any(b => b.Name == name));
        return await RunBulkAsync($"Create {name}", available, r => r.CreateBranchAsync(name, null, checkout: true),
            existing.Select(r => new RepoOpResult(r.Name, RepoOpOutcome.Skipped, $"Branch \"{name}\" already exists.")));
    }

    private static (List<T> Matching, List<T> Others) Partition<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var matching = new List<T>();
        var rest = new List<T>();
        foreach (var item in items)
            (predicate(item) ? matching : rest).Add(item);
        return (matching, rest);
    }

    /// <summary>Runs an action on several repositories in parallel and logs a one-line summary.</summary>
    private async Task<IReadOnlyList<RepoOpResult>> RunBulkAsync(string title, IReadOnlyList<RepoViewModel> repos,
        Func<RepoViewModel, Task<GitResult>> action, IEnumerable<RepoOpResult>? skipped = null)
    {
        var gitResults = await Task.WhenAll(repos.Select(action));
        var results = repos
            .Select((r, i) => new RepoOpResult(r.Name, gitResults[i].Success ? RepoOpOutcome.Succeeded : RepoOpOutcome.Failed,
                gitResults[i].Message))
            .Concat(skipped ?? [])
            .ToList();

        foreach (var skip in results.Where(r => r.Outcome == RepoOpOutcome.Skipped))
            _services.Log.Info($"[{skip.Repo}] {skip.Message} Skipped.");

        var failed = results.Where(r => r.Outcome == RepoOpOutcome.Failed).Select(r => r.Repo).ToList();
        var skippedCount = results.Count(r => r.Outcome == RepoOpOutcome.Skipped);
        var summary = $"{title}: {results.Count(r => r.Outcome == RepoOpOutcome.Succeeded)} succeeded";
        if (skippedCount > 0)
            summary += $", {skippedCount} skipped";
        if (failed.Count > 0)
            _services.Log.Error(summary + $", {failed.Count} failed ({string.Join(", ", failed)}). See the log for details.");
        else
            _services.Log.Info(summary + ".");
        return results;
    }
}

public enum RepoOpOutcome
{
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>The outcome of a project-wide action for one repository.</summary>
public sealed record RepoOpResult(string Repo, RepoOpOutcome Outcome, string? Message);
