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

        var topLevel = await _services.Git.GetTopLevelAsync(folder);
        if (topLevel is null)
        {
            await _services.Dialogs.ShowMessageAsync("Add repository", $"{folder}\n\nis not inside a git repository.");
            return;
        }
        if (!TryAddRepo(topLevel))
            await _services.Dialogs.ShowMessageAsync("Add repository", $"{topLevel}\n\nis already in this project.");
        _services.SaveSettings();
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
        if (!confirmed)
            return;

        var index = Repos.IndexOf(repo);
        Repos.Remove(repo);
        Model.Repos.Remove(repo.Model);
        _services.SaveSettings();
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
    private Task RefreshAll() => Task.WhenAll(Repos.Select(r => r.RefreshAsync()));

    [RelayCommand]
    private Task FetchAll() => RunBulkAsync("Fetch", r => r.FetchAsync(interactive: false));

    [RelayCommand]
    private Task PullAll() => RunBulkAsync("Pull", r => r.PullAsync(interactive: false));

    /// <summary>Switches every repository that has the given branch (locally or on a remote) to it.</summary>
    [RelayCommand]
    private async Task SwitchAllAsync()
    {
        var name = await _services.Dialogs.PromptAsync("Switch all repositories",
            "Branch to switch to in every repository that has it:", SelectedRepo?.CurrentBranch ?? "");
        if (string.IsNullOrWhiteSpace(name))
            return;
        name = name.Trim();

        await RefreshAll();
        var (withBranch, without) = Partition(Repos, r => r.HasBranch(name));
        foreach (var repo in without)
            _services.Log.Info($"[{repo.Name}] has no branch \"{name}\"; skipped.");

        await RunBulkAsync($"Switch to {name}", r => r.CurrentBranch == name ? Task.FromResult(true) : r.SwitchToAsync(name, interactive: false),
            withBranch, skipped: without.Count);
    }

    /// <summary>Creates a branch from the current HEAD in every repository and switches to it.</summary>
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

        await RefreshAll();
        var (available, existing) = Partition(Repos, r => !r.LocalBranches.Any(b => b.Name == name));
        foreach (var repo in existing)
            _services.Log.Info($"[{repo.Name}] already has a branch \"{name}\"; skipped.");

        await RunBulkAsync($"Create {name}", r => r.CreateBranchAsync(name, null, checkout: true, interactive: false),
            available, skipped: existing.Count);
    }

    private static (List<T> Matching, List<T> Others) Partition<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var matching = new List<T>();
        var rest = new List<T>();
        foreach (var item in items)
            (predicate(item) ? matching : rest).Add(item);
        return (matching, rest);
    }

    private async Task RunBulkAsync(string title, Func<RepoViewModel, Task<bool>> action,
        IReadOnlyList<RepoViewModel>? repos = null, int skipped = 0)
    {
        repos ??= Repos.ToList();
        var results = await Task.WhenAll(repos.Select(action));
        var failed = repos.Where((_, i) => !results[i]).Select(r => r.Name).ToList();

        var summary = $"{title}: {results.Length - failed.Count} succeeded";
        if (skipped > 0)
            summary += $", {skipped} skipped";
        if (failed.Count > 0)
        {
            summary += $", {failed.Count} failed ({string.Join(", ", failed)})";
            _services.Log.Error(summary + ". See the log for details.");
        }
        else
        {
            _services.Log.Info(summary + ".");
        }
    }
}
