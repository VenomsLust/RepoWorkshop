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

/// <summary>
/// The working tree: staged and unstaged changes (the Changes tab), every changed file in one list
/// (the always-visible changes panel), and committing.
/// </summary>
public partial class ChangesViewModel : ViewModelBase {
    /// <summary>The most files Diff opens at once.</summary>
    private const int MaxDiffs = 10;

    /// <summary>The most file names listed in a confirmation.</summary>
    private const int MaxListedFiles = 10;

    /// <summary>The repository these changes belong to.</summary>
    private readonly RepoViewModel _repo;

    /// <summary>Shared app services.</summary>
    private readonly AppServices _services;

    /// <summary>Creates the view model for <paramref name="repo"/>; its lists are filled by <see cref="Update"/>.</summary>
    public ChangesViewModel(RepoViewModel repo, AppServices services) {
        _repo = repo;
        _services = services;
        Staged.CollectionChanged += OnStagedChanged;
        Unstaged.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasUnstaged));
        Files.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFiles));
    }

    /// <summary>Changes in the index, sorted by path.</summary>
    public ObservableCollection<FileChange> Staged { get; } = [];

    /// <summary>Working-tree changes (including untracked and conflicted files), sorted by path.</summary>
    public ObservableCollection<FileChange> Unstaged { get; } = [];

    /// <summary>Every changed file, staged or not, sorted by path (the changes panel).</summary>
    public ObservableCollection<WorkingFile> Files { get; } = [];

    /// <summary>Whether anything is staged.</summary>
    public bool HasStaged => Staged.Count > 0;

    /// <summary>Whether anything is unstaged.</summary>
    public bool HasUnstaged => Unstaged.Count > 0;

    /// <summary>Whether there are any changes at all.</summary>
    public bool HasFiles => Files.Count > 0;

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

    /// <summary>Updates the lists from a fresh status, keeping unchanged items (and so the user's selection).</summary>
    public void Update(RepoStatus status) {
        RepoViewModel.Sync(Staged, status.Staged.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList());
        RepoViewModel.Sync(Unstaged, status.Unstaged.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList());
        RepoViewModel.Sync(Files, BuildWorkingFiles(status));
    }

    /// <summary>Empties all lists (e.g. when the repository can't be read).</summary>
    public void Clear() {
        Staged.Clear();
        Unstaged.Clear();
        Files.Clear();
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

    /// <summary>Opens a file's whole change in WinMerge: the last commit vs the (editable) working copy.</summary>
    public Task OpenWorkingDiffAsync(WorkingFile file) {
        bool isNew = !_repo.HasHead || file.Kind is ChangeKind.Untracked or ChangeKind.Added;
        DiffSide left = isNew ? DiffSide.Empty("(new file)") : DiffSide.At("HEAD", "HEAD", file.OriginalPath ?? file.Path);
        DiffSide right = file.Kind is ChangeKind.Deleted ? DiffSide.Empty("(deleted)") : DiffSide.Working(file.Path);
        return _services.Diff.DiffFileAsync(_repo.Path, left, right);
    }

    /// <summary>Merges the staged and unstaged halves of a status into one row per path, sorted by path.</summary>
    internal static List<WorkingFile> BuildWorkingFiles(RepoStatus status) {
        Dictionary<string, FileChange> staged = status.Staged.ToDictionary(c => c.Path);
        Dictionary<string, FileChange> unstaged = status.Unstaged.ToDictionary(c => c.Path);

        List<WorkingFile> files = [];
        foreach (string path in staged.Keys.Union(unstaged.Keys).Order(StringComparer.OrdinalIgnoreCase)) {
            FileChange? s = staged.GetValueOrDefault(path);
            FileChange? u = unstaged.GetValueOrDefault(path);

            // Staged additions, deletions and renames describe the file best; otherwise show the working-tree change.
            ChangeKind kind = s?.Kind is ChangeKind.Added or ChangeKind.Deleted or ChangeKind.Renamed
                ? s.Kind
                : u?.Kind ?? s!.Kind;
            string code = u?.Kind is ChangeKind.Untracked or ChangeKind.Unmerged
                ? u.Code
                : $"{s?.Code ?? " "}{u?.Code ?? " "}";
            files.Add(new WorkingFile(path, s?.OriginalPath, kind, code, IsStaged: s is not null, HasUnstaged: u is not null));
        }
        return files;
    }

    // ---- Changes tab --------------------------------------------------------------------------

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
        string file = FullPath(change.Path);
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

    // ---- Changes panel ------------------------------------------------------------------------

    /// <summary>Stages the selected files.</summary>
    [RelayCommand]
    private async Task StageFilesAsync(IList? selected) {
        List<string> paths = FilesOf(selected).Select(f => f.Path).ToList();
        if (paths.Count > 0) {
            await _repo.RunActionAsync("Stage", () => _services.Git.StageAsync(_repo.Path, paths), interactive: true);
        }
    }

    /// <summary>Unstages the staged files among the selection, keeping their working-tree changes.</summary>
    [RelayCommand(CanExecute = nameof(HasStaged))]
    private async Task UnstageFilesAsync(IList? selected) {
        List<string> paths = FilesOf(selected).Where(f => f.IsStaged).Select(f => f.Path).ToList();
        bool hasHead = _repo.HasHead;
        if (paths.Count > 0) {
            await _repo.RunActionAsync("Unstage", () => _services.Git.UnstageAsync(_repo.Path, paths, hasHead), interactive: true);
        }
    }

    /// <summary>
    /// After confirming, puts the selected files back to the last commit. Newly added files are only
    /// unstaged (kept on disk); untracked files are left alone (use Delete).
    /// </summary>
    [RelayCommand]
    private async Task RevertFilesAsync(IList? selected) {
        List<WorkingFile> files = FilesOf(selected);
        List<WorkingFile> revertible = files.Where(f => f.Kind != ChangeKind.Untracked).ToList();
        if (files.Count == 0) {
            return;
        }
        if (revertible.Count == 0) {
            await _services.Dialogs.ShowMessageAsync("Revert", "Untracked files have no committed version to revert to. Use Delete to remove them.");
            return;
        }

        string note = revertible.Count < files.Count ? "\n\nUntracked files are left alone; use Delete for those." : "";
        bool confirmed = await _services.Dialogs.ConfirmAsync("Revert",
            $"Revert {Plural(revertible.Count, "file")} to the last commit?\n\n{ListFiles(revertible)}\n\n" +
            $"{(revertible.Count == 1 ? "Its" : "Their")} uncommitted changes will be lost.{note}", "Revert");
        if (!confirmed) {
            return;
        }

        // A file that isn't in HEAD can only be unstaged; a rename restores the old path and unstages the new one.
        bool hasHead = _repo.HasHead;
        List<string> unstage = revertible
            .Where(f => !hasHead || f.Kind is ChangeKind.Added or ChangeKind.Renamed)
            .Where(f => f.IsStaged)
            .Select(f => f.Path)
            .ToList();
        List<string> restore = hasHead
            ? revertible
                .Where(f => f.Kind != ChangeKind.Added)
                .Select(f => f.Kind == ChangeKind.Renamed && f.OriginalPath is not null ? f.OriginalPath : f.Path)
                .ToList()
            : [];

        await _repo.RunActionAsync("Revert", async () => {
            if (unstage.Count > 0) {
                GitResult unstaged = await _services.Git.UnstageAsync(_repo.Path, unstage, hasHead);
                if (!unstaged.Success || restore.Count == 0) {
                    return unstaged;
                }
            }
            return await _services.Git.RestoreToHeadAsync(_repo.Path, restore);
        }, interactive: true);
    }

    /// <summary>Opens each selected file's change in WinMerge (up to <see cref="MaxDiffs"/>).</summary>
    [RelayCommand]
    private async Task DiffFilesAsync(IList? selected) {
        List<WorkingFile> files = FilesOf(selected);
        if (files.Count > MaxDiffs) {
            _services.Log.Info($"[{_repo.Name}] Opening the first {MaxDiffs} of {files.Count} selected files.");
        }
        foreach (WorkingFile file in files.Take(MaxDiffs)) {
            await ReportErrorsAsync(OpenWorkingDiffAsync(file));
        }
    }

    /// <summary>Opens one file's change in WinMerge (double-click in the panel).</summary>
    [RelayCommand]
    private Task DiffFile(WorkingFile? file) => file is null ? Task.CompletedTask : ReportErrorsAsync(OpenWorkingDiffAsync(file));

    /// <summary>After confirming, moves the selected files to the Recycle Bin.</summary>
    [RelayCommand]
    private async Task DeleteFilesAsync(IList? selected) {
        List<WorkingFile> files = FilesOf(selected).Where(f => File.Exists(FullPath(f.Path))).ToList();
        if (files.Count == 0) {
            return;
        }

        bool confirmed = await _services.Dialogs.ConfirmAsync("Delete",
            $"Move {Plural(files.Count, "file")} to the Recycle Bin?\n\n{ListFiles(files)}", "Delete");
        if (!confirmed) {
            return;
        }

        List<string> paths = files.Select(f => FullPath(f.Path)).ToList();
        await _repo.RunActionAsync("Delete", () => {
            RecycleBin.Send(paths);
            return Task.FromResult(new GitResult(0, $"Moved {Plural(paths.Count, "file")} to the Recycle Bin.", ""));
        }, interactive: true);
    }

    // ---- Helpers ------------------------------------------------------------------------------

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

    /// <summary>The full local path of a repository-relative path.</summary>
    private string FullPath(string repoRelativePath) =>
        Path.Combine(_repo.Path, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The distinct paths of the selected Changes-tab items.</summary>
    private static List<string> PathsOf(IList? items) => items?.OfType<FileChange>().Select(c => c.Path).Distinct().ToList() ?? [];

    /// <summary>The selected panel items, in list order.</summary>
    private static List<WorkingFile> FilesOf(IList? items) =>
        items?.OfType<WorkingFile>().OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList() ?? [];

    /// <summary>Up to <see cref="MaxListedFiles"/> paths, one per line, for a confirmation.</summary>
    private static string ListFiles(IReadOnlyList<WorkingFile> files) {
        string list = string.Join('\n', files.Take(MaxListedFiles).Select(f => f.Path));
        return files.Count > MaxListedFiles ? $"{list}\n…and {files.Count - MaxListedFiles} more" : list;
    }

    /// <summary>E.g. "1 file" or "3 files".</summary>
    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Updates what depends on whether anything is staged.</summary>
    private void OnStagedChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        OnPropertyChanged(nameof(HasStaged));
        CommitCommand.NotifyCanExecuteChanged();
        UnstageFilesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Pre-fills the message with the last commit's when amending (if nothing has been typed).</summary>
    partial void OnAmendChanged(bool value) {
        if (value && string.IsNullOrWhiteSpace(CommitMessage)) {
            _ = PrefillAmendMessageAsync();
        }
    }
}
