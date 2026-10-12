using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>Commit history of a branch, and the files each commit changed.</summary>
public partial class HistoryViewModel : ViewModelBase
{
    private const int MaxCommits = 300;

    private readonly RepoViewModel _repo;
    private readonly AppServices _services;
    private string? _loadedRevision;
    private string _loadedLabel = "HEAD";
    private bool _stale = true;

    public HistoryViewModel(RepoViewModel repo, AppServices services)
    {
        _repo = repo;
        _services = services;
    }

    public ObservableCollection<CommitInfo> Commits { get; } = [];

    public ObservableCollection<FileChange> Files { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommitCommand))]
    public partial CommitInfo? SelectedCommit { get; set; }

    /// <summary>Marks the loaded history as out of date, e.g. after a commit or fetch.</summary>
    public void MarkStale() => _stale = true;

    /// <summary>Shows the history of a revision, reloading only if it changed or is stale.</summary>
    public async Task ShowAsync(string revision, string label)
    {
        Title = $"History of {label}";
        if (!_stale && revision == _loadedRevision)
            return;

        _stale = false;
        _loadedRevision = revision;
        _loadedLabel = label;
        IsLoading = true;
        try
        {
            var selected = SelectedCommit?.Hash;
            var commits = await _services.Git.GetLogAsync(_repo.Path, revision, MaxCommits);
            if (revision != _loadedRevision)
                return; // A newer request superseded this one.
            RepoViewModel.Replace(Commits, commits);
            SelectedCommit = selected is null ? null : Commits.FirstOrDefault(c => c.Hash == selected);
            if (commits.Count == MaxCommits)
                Title += $" (latest {MaxCommits})";
        }
        catch (Exception ex)
        {
            Commits.Clear();
            Title = $"History of {label}: {ex.Message.Split('\n')[0]}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedCommitChanged(CommitInfo? value) => _ = LoadFilesAsync(value);

    private async Task LoadFilesAsync(CommitInfo? commit)
    {
        Files.Clear();
        if (commit is null)
            return;
        try
        {
            var files = await _services.Git.GetChangesBetweenAsync(_repo.Path, commit.FirstParent ?? GitService.EmptyTree, commit.Hash);
            if (SelectedCommit == commit)
                RepoViewModel.Replace(Files, files);
        }
        catch (Exception ex)
        {
            _services.Log.Error($"[{_repo.Name}] {ex.Message}");
        }
    }

    [RelayCommand]
    private Task Reload()
    {
        _stale = true;
        return ShowAsync(_loadedRevision ?? "HEAD", _loadedLabel);
    }

    private bool HasSelectedCommit => SelectedCommit is not null;

    /// <summary>Opens every file the selected commit changed (vs its first parent) in WinMerge.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedCommit))]
    private async Task CompareCommitAsync()
    {
        var commit = SelectedCommit!;
        var parent = commit.FirstParent ?? GitService.EmptyTree;
        var parentLabel = commit.FirstParent is null ? "(empty)" : commit.FirstParent[..7];
        try
        {
            await _services.Diff.CompareRevisionsAsync(_repo.Path, parent, $"Before {commit.ShortHash} ({parentLabel})",
                commit.Hash, $"{commit.ShortHash}: {commit.Subject}");
        }
        catch (Exception ex)
        {
            await _services.Dialogs.ShowMessageAsync("Compare commit", ex.Message);
        }
    }

    /// <summary>Compares one file as it was before and after the selected commit.</summary>
    [RelayCommand]
    private async Task DiffFileAsync(FileChange? file)
    {
        var commit = SelectedCommit;
        if (commit is null || file is null)
            return;

        var parentLabel = commit.FirstParent is null ? "(empty)" : commit.FirstParent[..7];
        var left = file.Kind is ChangeKind.Added || commit.FirstParent is null
            ? DiffSide.Empty("(new file)")
            : DiffSide.At(commit.FirstParent, parentLabel, file.OriginalPath ?? file.Path);
        var right = file.Kind is ChangeKind.Deleted
            ? DiffSide.Empty("(deleted)")
            : DiffSide.At(commit.Hash, commit.ShortHash, file.Path);
        try
        {
            await _services.Diff.DiffFileAsync(_repo.Path, left, right);
        }
        catch (Exception ex)
        {
            await _services.Dialogs.ShowMessageAsync("Diff", ex.Message);
        }
    }
}
