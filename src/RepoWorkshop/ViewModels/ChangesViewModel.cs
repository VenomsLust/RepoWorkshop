using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
public partial class ChangesViewModel : ViewModelBase
{
    private readonly RepoViewModel _repo;
    private readonly AppServices _services;

    public ChangesViewModel(RepoViewModel repo, AppServices services)
    {
        _repo = repo;
        _services = services;
        Staged.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasStaged));
            CommitCommand.NotifyCanExecuteChanged();
        };
        Unstaged.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasUnstaged));
    }

    public ObservableCollection<FileChange> Staged { get; } = [];

    public ObservableCollection<FileChange> Unstaged { get; } = [];

    public bool HasStaged => Staged.Count > 0;

    public bool HasUnstaged => Unstaged.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial string CommitMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial bool Amend { get; set; }

    public void Update(RepoStatus status)
    {
        RepoViewModel.Replace(Staged, status.Staged.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase));
        RepoViewModel.Replace(Unstaged, status.Unstaged.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        Staged.Clear();
        Unstaged.Clear();
    }

    /// <summary>Pre-fills the message with the last commit's when amending (if nothing has been typed).</summary>
    partial void OnAmendChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(CommitMessage))
            _ = PrefillAmendMessageAsync();
    }

    private async Task PrefillAmendMessageAsync()
    {
        var message = await _services.Git.GetHeadMessageAsync(_repo.Path);
        if (Amend && string.IsNullOrWhiteSpace(CommitMessage) && message is not null)
            CommitMessage = message;
    }

    private static List<string> PathsOf(IList? items) => items?.OfType<FileChange>().Select(c => c.Path).Distinct().ToList() ?? [];

    // ---- Staging ------------------------------------------------------------------------------

    [RelayCommand]
    private async Task StageAsync(IList? selected)
    {
        var paths = PathsOf(selected);
        if (paths.Count > 0)
            await _repo.RunActionAsync("Stage", () => _services.Git.StageAsync(_repo.Path, paths), interactive: true);
    }

    [RelayCommand]
    private Task StageAll() =>
        _repo.RunActionAsync("Stage all", () => _services.Git.StageAllAsync(_repo.Path), interactive: true);

    [RelayCommand]
    private async Task UnstageAsync(IList? selected)
    {
        var paths = PathsOf(selected);
        var hasHead = _repo.HasHead;
        if (paths.Count > 0)
            await _repo.RunActionAsync("Unstage", () => _services.Git.UnstageAsync(_repo.Path, paths, hasHead), interactive: true);
    }

    [RelayCommand]
    private Task UnstageAll()
    {
        var hasHead = _repo.HasHead;
        return _repo.RunActionAsync("Unstage all", () => _services.Git.UnstageAllAsync(_repo.Path, hasHead), interactive: true);
    }

    // ---- Diffs --------------------------------------------------------------------------------

    [RelayCommand]
    private Task DiffStaged(FileChange? change) => change is null ? Task.CompletedTask : ReportErrorsAsync(OpenStagedDiffAsync(change));

    [RelayCommand]
    private Task DiffUnstaged(FileChange? change) => change is null ? Task.CompletedTask : ReportErrorsAsync(OpenUnstagedDiffAsync(change));

    /// <summary>Staged change: the last commit vs the staged version.</summary>
    public Task OpenStagedDiffAsync(FileChange change)
    {
        var left = change.Kind is ChangeKind.Added || !_repo.HasHead
            ? DiffSide.Empty("(new file)")
            : DiffSide.At("HEAD", "HEAD", change.OriginalPath ?? change.Path);
        var right = change.Kind is ChangeKind.Deleted ? DiffSide.Empty("(deleted)") : DiffSide.Index(change.Path);
        return _services.Diff.DiffFileAsync(_repo.Path, left, right);
    }

    /// <summary>Unstaged change: the staged version vs the (editable) working copy.</summary>
    public Task OpenUnstagedDiffAsync(FileChange change)
    {
        var left = change.Kind switch
        {
            ChangeKind.Untracked => DiffSide.Empty("(untracked)"),
            // A conflicted file has no single staged version; show ours (HEAD) vs the file with conflict markers.
            ChangeKind.Unmerged => DiffSide.At("HEAD", "HEAD", change.Path),
            _ => DiffSide.Index(change.Path),
        };
        var right = change.Kind is ChangeKind.Deleted ? DiffSide.Empty("(deleted)") : DiffSide.Working(change.Path);
        return _services.Diff.DiffFileAsync(_repo.Path, left, right);
    }

    private async Task ReportErrorsAsync(Task diff)
    {
        try
        {
            await diff;
        }
        catch (Exception ex)
        {
            await _services.Dialogs.ShowMessageAsync("Diff", ex.Message);
        }
    }

    [RelayCommand]
    private void ShowInExplorer(FileChange? change)
    {
        if (change is null)
            return;
        var file = Path.Combine(_repo.Path, change.Path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(file))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false })?.Dispose();
    }

    // ---- Commit -------------------------------------------------------------------------------

    private bool CanCommit => !string.IsNullOrWhiteSpace(CommitMessage) && (HasStaged || Amend);

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        var message = CommitMessage.Trim();
        var amend = Amend;
        if (amend && _repo.Upstream is not null && _repo.Ahead == 0)
        {
            var confirmed = await _services.Dialogs.ConfirmAsync("Amend commit",
                "The last commit has already been pushed. Amending it rewrites history, and the next push will need --force.\n\nAmend anyway?",
                "Amend");
            if (!confirmed)
                return;
        }

        if (await _repo.RunActionAsync(amend ? "Amend" : "Commit", () => _services.Git.CommitAsync(_repo.Path, message, amend), interactive: true))
        {
            CommitMessage = "";
            Amend = false;
        }
    }
}
