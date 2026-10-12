using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>The working tree: staged and unstaged changes, and committing them.</summary>
public partial class ChangesViewModel : ViewModelBase {
    /// <summary>The repository these changes belong to.</summary>
    private readonly RepoViewModel _repo;

    /// <summary>Shared app services.</summary>
    private readonly AppServices _services;

    /// <summary>Creates the tab for <paramref name="repo"/>; its lists are filled by <see cref="Update"/>.</summary>
    public ChangesViewModel(RepoViewModel repo, AppServices services) {
        _repo = repo;
        _services = services;
        Staged.CollectionChanged += OnStagedChanged;
        Unstaged.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasUnstaged));
    }

    /// <summary>Changes in the index, sorted by path.</summary>
    public ObservableCollection<FileChange> Staged { get; } = [];

    /// <summary>Working-tree changes (including untracked and conflicted files), sorted by path.</summary>
    public ObservableCollection<FileChange> Unstaged { get; } = [];

    /// <summary>Whether anything is staged.</summary>
    public bool HasStaged => Staged.Count > 0;

    /// <summary>Whether anything is unstaged.</summary>
    public bool HasUnstaged => Unstaged.Count > 0;

    /// <summary>The message for the next commit.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial string CommitMessage { get; set; } = "";

    /// <summary>Whether Commit replaces the last commit.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial bool Amend { get; set; }

    /// <summary>Whether there is a message and something to commit.</summary>
    private bool CanCommit => !string.IsNullOrWhiteSpace(CommitMessage) && (HasStaged || Amend);

    /// <summary>Fills the lists from a fresh status.</summary>
    public void Update(RepoStatus status) {
        RepoViewModel.Replace(Staged, status.Staged.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase));
        RepoViewModel.Replace(Unstaged, status.Unstaged.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Empties both lists (e.g. when the repository can't be read).</summary>
    public void Clear() {
        Staged.Clear();
        Unstaged.Clear();
    }

    /// <summary>Opens a staged change in WinMerge: the last commit vs the staged version.</summary>
    public Task OpenStagedDiffAsync(FileChange change) {
        DiffSide left = change.Kind is ChangeKind.Added || !_repo.HasHead
            ? DiffSide.Empty("(new file)")
            : DiffSide.At("HEAD", "HEAD", change.OriginalPath ?? change.Path);
        DiffSide right = change.Kind is ChangeKind.Deleted ? DiffSide.Empty("(deleted)") : DiffSide.Index(change.Path);
        return _services.Diff.DiffFileAsync(_repo.Path, left, right);
    }

    /// <summary>Opens an unstaged change in WinMerge: the staged version vs the (editable) working copy.</summary>
    public Task OpenUnstagedDiffAsync(FileChange change) {
        DiffSide left = change.Kind switch {
            ChangeKind.Untracked => DiffSide.Empty("(untracked)"),
            // A conflicted file has no single staged version; show ours (HEAD) vs the file with conflict markers.
            ChangeKind.Unmerged => DiffSide.At("HEAD", "HEAD", change.Path),
            _ => DiffSide.Index(change.Path),
        };
        DiffSide right = change.Kind is ChangeKind.Deleted ? DiffSide.Empty("(deleted)") : DiffSide.Working(change.Path);
        return _services.Diff.DiffFileAsync(_repo.Path, left, right);
    }

    /// <summary>Stages the selected files.</summary>
    [RelayCommand]
    private async Task StageAsync(IList? selected) {
        List<string> paths = PathsOf(selected);
        if (paths.Count > 0) {
            await _repo.RunActionAsync("Stage", () => _services.Git.StageAsync(_repo.Path, paths), interactive: true);
        }
    }

    /// <summary>Stages everything.</summary>
    [RelayCommand]
    private Task StageAll() =>
        _repo.RunActionAsync("Stage all", () => _services.Git.StageAllAsync(_repo.Path), interactive: true);

    /// <summary>Unstages the selected files.</summary>
    [RelayCommand]
    private async Task UnstageAsync(IList? selected) {
        List<string> paths = PathsOf(selected);
        bool hasHead = _repo.HasHead;
        if (paths.Count > 0) {
            await _repo.RunActionAsync("Unstage", () => _services.Git.UnstageAsync(_repo.Path, paths, hasHead), interactive: true);
        }
    }

    /// <summary>Unstages everything.</summary>
    [RelayCommand]
    private Task UnstageAll() {
        bool hasHead = _repo.HasHead;
        return _repo.RunActionAsync("Unstage all", () => _services.Git.UnstageAllAsync(_repo.Path, hasHead), interactive: true);
    }

    /// <summary>Diffs a staged file, reporting errors in a dialog.</summary>
    [RelayCommand]
    private Task DiffStaged(FileChange? change) => change is null ? Task.CompletedTask : ReportErrorsAsync(OpenStagedDiffAsync(change));

    /// <summary>Diffs an unstaged file, reporting errors in a dialog.</summary>
    [RelayCommand]
    private Task DiffUnstaged(FileChange? change) => change is null ? Task.CompletedTask : ReportErrorsAsync(OpenUnstagedDiffAsync(change));

    /// <summary>Selects the changed file in Explorer.</summary>
    [RelayCommand]
    private void ShowInExplorer(FileChange? change) {
        if (change is null) {
            return;
        }
        string file = Path.Combine(_repo.Path, change.Path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(file)) {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false })?.Dispose();
        }
    }

    /// <summary>Commits (or amends), confirming first if the commit being amended was already pushed.</summary>
    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync() {
        string message = CommitMessage.Trim();
        bool amend = Amend;
        if (amend && _repo.Upstream is not null && _repo.Ahead == 0) {
            bool confirmed = await _services.Dialogs.ConfirmAsync("Amend commit",
                "The last commit has already been pushed. Amending it rewrites history, and the next push will need --force.\n\nAmend anyway?",
                "Amend");
            if (!confirmed) {
                return;
            }
        }

        if (await _repo.RunActionAsync(amend ? "Amend" : "Commit", () => _services.Git.CommitAsync(_repo.Path, message, amend), interactive: true)) {
            CommitMessage = "";
            Amend = false;
        }
    }

    /// <summary>Fills the message with the last commit's, unless the user has typed one meanwhile.</summary>
    private async Task PrefillAmendMessageAsync() {
        string? message = await _services.Git.GetHeadMessageAsync(_repo.Path);
        if (Amend && string.IsNullOrWhiteSpace(CommitMessage) && message is not null) {
            CommitMessage = message;
        }
    }

    /// <summary>Awaits a diff, showing any error in a dialog.</summary>
    private async Task ReportErrorsAsync(Task diff) {
        try {
            await diff;
        } catch (Exception ex) {
            await _services.Dialogs.ShowMessageAsync("Diff", ex.Message);
        }
    }

    /// <summary>The distinct paths of the selected list items.</summary>
    private static List<string> PathsOf(IList? items) => items?.OfType<FileChange>().Select(c => c.Path).Distinct().ToList() ?? [];

    /// <summary>Updates what depends on whether anything is staged.</summary>
    private void OnStagedChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        OnPropertyChanged(nameof(HasStaged));
        CommitCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Pre-fills the message with the last commit's when amending (if nothing has been typed).</summary>
    partial void OnAmendChanged(bool value) {
        if (value && string.IsNullOrWhiteSpace(CommitMessage)) {
            _ = PrefillAmendMessageAsync();
        }
    }
}
