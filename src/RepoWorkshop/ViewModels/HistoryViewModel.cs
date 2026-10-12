using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>Commit history of a branch, and the files each commit changed.</summary>
public partial class HistoryViewModel : ViewModelBase {
    /// <summary>The most commits loaded at once.</summary>
    private const int MaxCommits = 300;

    /// <summary>The repository whose history this is.</summary>
    private readonly RepoViewModel _repo;

    /// <summary>Shared app services.</summary>
    private readonly AppServices _services;

    /// <summary>The revision currently shown.</summary>
    private string? _loadedRevision;

    /// <summary>Display name of the revision currently shown.</summary>
    private string _loadedLabel = "HEAD";

    /// <summary>Whether the shown history may be out of date.</summary>
    private bool _stale = true;

    /// <summary>Creates the tab for <paramref name="repo"/>; history loads when the tab is shown.</summary>
    public HistoryViewModel(RepoViewModel repo, AppServices services) {
        _repo = repo;
        _services = services;
    }

    /// <summary>The commits, newest first.</summary>
    public ObservableCollection<CommitInfo> Commits { get; } = [];

    /// <summary>Files changed by the selected commit.</summary>
    public ObservableCollection<FileChange> Files { get; } = [];

    /// <summary>E.g. "History of main".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    /// <summary>Whether history is being loaded.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>The commit whose files are listed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommitCommand))]
    public partial CommitInfo? SelectedCommit { get; set; }

    /// <summary>Whether a commit is selected.</summary>
    private bool HasSelectedCommit => SelectedCommit is not null;

    /// <summary>Marks the loaded history as out of date, e.g. after a commit or fetch.</summary>
    public void MarkStale() => _stale = true;

    /// <summary>Shows the history of a revision, reloading only if it changed or is stale.</summary>
    public async Task ShowAsync(string revision, string label) {
        Title = $"History of {label}";
        if (!_stale && revision == _loadedRevision) {
            return;
        }

        _stale = false;
        _loadedRevision = revision;
        _loadedLabel = label;
        IsLoading = true;
        try {
            string? selected = SelectedCommit?.Hash;
            IReadOnlyList<CommitInfo> commits = await _services.Git.GetLogAsync(_repo.Path, revision, MaxCommits);
            if (revision != _loadedRevision) {
                return; // A newer request superseded this one.
            }

            RepoViewModel.Replace(Commits, commits);
            SelectedCommit = selected is null ? null : Commits.FirstOrDefault(c => c.Hash == selected);
            if (commits.Count == MaxCommits) {
                Title += $" (latest {MaxCommits})";
            }
        } catch (Exception ex) {
            Commits.Clear();
            Title = $"History of {label}: {ex.Message.Split('\n')[0]}";
        } finally {
            IsLoading = false;
        }
    }

    /// <summary>Opens every file a commit changed, vs its first parent, in WinMerge. Returns the file count.</summary>
    public Task<int> OpenCommitComparisonAsync(CommitInfo commit) {
        string parent = commit.FirstParent ?? GitService.EmptyTree;
        string parentLabel = commit.FirstParent is null ? "(empty)" : commit.FirstParent[..7];
        return _services.Diff.CompareRevisionsAsync(_repo.Path, parent, $"Before {commit.ShortHash} ({parentLabel})",
            commit.Hash, $"{commit.ShortHash}: {commit.Subject}");
    }

    /// <summary>Reloads the history shown.</summary>
    [RelayCommand]
    private Task Reload() {
        _stale = true;
        return ShowAsync(_loadedRevision ?? "HEAD", _loadedLabel);
    }

    /// <summary>Opens everything the selected commit changed in WinMerge.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedCommit))]
    private async Task CompareCommitAsync() {
        try {
            await OpenCommitComparisonAsync(SelectedCommit!);
        } catch (Exception ex) {
            await _services.Dialogs.ShowMessageAsync("Compare commit", ex.Message);
        }
    }

    /// <summary>Compares one file as it was before and after the selected commit.</summary>
    [RelayCommand]
    private async Task DiffFileAsync(FileChange? file) {
        CommitInfo? commit = SelectedCommit;
        if (commit is null || file is null) {
            return;
        }

        string parentLabel = commit.FirstParent is null ? "(empty)" : commit.FirstParent[..7];
        DiffSide left = file.Kind is ChangeKind.Added || commit.FirstParent is null
            ? DiffSide.Empty("(new file)")
            : DiffSide.At(commit.FirstParent, parentLabel, file.OriginalPath ?? file.Path);
        DiffSide right = file.Kind is ChangeKind.Deleted
            ? DiffSide.Empty("(deleted)")
            : DiffSide.At(commit.Hash, commit.ShortHash, file.Path);
        try {
            await _services.Diff.DiffFileAsync(_repo.Path, left, right);
        } catch (Exception ex) {
            await _services.Dialogs.ShowMessageAsync("Diff", ex.Message);
        }
    }

    /// <summary>Lists the files a commit changed.</summary>
    private async Task LoadFilesAsync(CommitInfo? commit) {
        Files.Clear();
        if (commit is null) {
            return;
        }

        try {
            IReadOnlyList<FileChange> files = await _services.Git.GetChangesBetweenAsync(_repo.Path, commit.FirstParent ?? GitService.EmptyTree, commit.Hash);
            if (SelectedCommit == commit) {
                RepoViewModel.Replace(Files, files);
            }
        } catch (Exception ex) {
            _services.Log.Error($"[{_repo.Name}] {ex.Message}");
        }
    }

    /// <summary>Lists the newly selected commit's files.</summary>
    partial void OnSelectedCommitChanged(CommitInfo? value) => _ = LoadFilesAsync(value);
}
