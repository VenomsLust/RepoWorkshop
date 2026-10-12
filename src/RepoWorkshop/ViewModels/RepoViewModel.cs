using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;

namespace RepoWorkshop.ViewModels;

/// <summary>One repository within a project: its status, branches, changes, history and stashes.</summary>
public partial class RepoViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private Task? _refreshTask;

    public RepoViewModel(RepoModel model, AppServices services)
    {
        Model = model;
        _services = services;
        Changes = new ChangesViewModel(this, services);
        History = new HistoryViewModel(this, services);
        Stashes = new StashesViewModel(this, services);
    }

    public const int BranchesTab = 0;
    public const int ChangesTab = 1;
    public const int HistoryTab = 2;
    public const int StashesTab = 3;

    public RepoModel Model { get; }

    public string Path => Model.Path;

    public string Name => string.IsNullOrWhiteSpace(Model.Alias)
        ? System.IO.Path.GetFileName(Model.Path.TrimEnd('\\', '/'))
        : Model.Alias;

    public ObservableCollection<BranchInfo> LocalBranches { get; } = [];

    public ObservableCollection<BranchInfo> RemoteBranches { get; } = [];

    public ChangesViewModel Changes { get; }

    public HistoryViewModel History { get; }

    public StashesViewModel Stashes { get; }

    /// <summary>The selected tab: Branches, Changes, History or Stashes.</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial string HeadText { get; set; } = "…";

    /// <summary>The checked-out branch, or null when HEAD is detached or not yet loaded.</summary>
    [ObservableProperty]
    public partial string? CurrentBranch { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHead))]
    public partial string? HeadCommit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial string? Upstream { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial int Ahead { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial int Behind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangesText), nameof(IsDirty))]
    public partial int ChangeCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveBranch))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand), nameof(DeleteBranchCommand), nameof(MergeIntoCurrentCommand),
        nameof(CompareWithCurrentCommand))]
    public partial BranchInfo? SelectedLocalBranch { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveBranch))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand), nameof(DeleteBranchCommand), nameof(MergeIntoCurrentCommand),
        nameof(CompareWithCurrentCommand))]
    public partial BranchInfo? SelectedRemoteBranch { get; set; }

    /// <summary>0 = local branches tab, 1 = remote branches tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveBranch))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand), nameof(DeleteBranchCommand), nameof(MergeIntoCurrentCommand),
        nameof(CompareWithCurrentCommand))]
    public partial int BranchTabIndex { get; set; }

    /// <summary>The branch selected on whichever branch tab is showing.</summary>
    public BranchInfo? ActiveBranch => BranchTabIndex == 0 ? SelectedLocalBranch : SelectedRemoteBranch;

    public bool HasError => Error is not null;

    /// <summary>False in a new repository with no commits yet.</summary>
    public bool HasHead => HeadCommit is not null;

    public bool IsDirty => ChangeCount > 0;

    public string ChangesText => ChangeCount switch
    {
        0 => "clean",
        1 => "1 change",
        var n => $"{n} changes",
    };

    public string SyncText
    {
        get
        {
            if (Upstream is null)
                return CurrentBranch is null ? "" : "no upstream";
            var parts = new List<string>();
            if (Ahead > 0) parts.Add($"↑{Ahead}");
            if (Behind > 0) parts.Add($"↓{Behind}");
            return parts.Count == 0 ? "up to date" : string.Join(' ', parts);
        }
    }

    public void NotifyNameChanged() => OnPropertyChanged(nameof(Name));

    partial void OnSelectedTabIndexChanged(int value) => LoadHistoryIfVisible();

    partial void OnSelectedLocalBranchChanged(BranchInfo? value) => LoadHistoryIfVisible();

    partial void OnSelectedRemoteBranchChanged(BranchInfo? value) => LoadHistoryIfVisible();

    partial void OnBranchTabIndexChanged(int value) => LoadHistoryIfVisible();

    private void LoadHistoryIfVisible()
    {
        if (SelectedTabIndex == HistoryTab)
            _ = History.ShowAsync(ActiveBranch?.Name ?? "HEAD", ActiveBranch?.Name ?? HeadText);
    }

    /// <summary>Refreshes status and branches. Concurrent callers share the same refresh.</summary>
    public Task RefreshAsync()
    {
        if (_refreshTask is { IsCompleted: false })
            return _refreshTask;
        return _refreshTask = RefreshCoreAsync();
    }

    public Task EnsureLoadedAsync() => _refreshTask ?? RefreshAsync();

    private async Task RefreshCoreAsync()
    {
        try
        {
            var statusTask = _services.Git.GetStatusAsync(Path);
            var branchesTask = _services.Git.GetBranchesAsync(Path);
            var stashesTask = _services.Git.GetStashesAsync(Path);
            var status = await statusTask;
            var branches = await branchesTask;
            var stashes = await stashesTask;

            CurrentBranch = status.Branch;
            HeadCommit = status.HeadCommit;
            HeadText = status.IsDetached
                ? $"(detached at {status.HeadCommit?[..Math.Min(7, status.HeadCommit.Length)]})"
                : status.Branch ?? "(no branch)";
            Upstream = status.Upstream;
            Ahead = status.Ahead;
            Behind = status.Behind;
            ChangeCount = status.ChangedPathCount;
            Error = null;

            Changes.Update(status);
            Stashes.Update(stashes);
            History.MarkStale();

            var selectedLocal = SelectedLocalBranch?.Name;
            var selectedRemote = SelectedRemoteBranch?.Name;
            Replace(LocalBranches, branches.Where(b => !b.IsRemote));
            Replace(RemoteBranches, branches.Where(b => b.IsRemote));
            SelectedLocalBranch = LocalBranches.FirstOrDefault(b => b.Name == selectedLocal);
            SelectedRemoteBranch = RemoteBranches.FirstOrDefault(b => b.Name == selectedRemote);
            LoadHistoryIfVisible();
        }
        catch (Exception ex)
        {
            Error = ex.Message.Split('\n')[0];
            HeadText = "(unavailable)";
            CurrentBranch = null;
            Upstream = null;
            ChangeCount = 0;
            Changes.Clear();
            Stashes.Clear();
            LocalBranches.Clear();
            RemoteBranches.Clear();
        }
    }

    internal static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    /// <summary>Runs a git action, optionally reporting failure in a dialog, then refreshes.</summary>
    internal async Task<bool> RunActionAsync(string title, Func<Task<GitResult>> action, bool interactive)
    {
        if (IsBusy)
            return false;
        IsBusy = true;
        try
        {
            var result = await action();
            if (!result.Success && interactive)
                await _services.Dialogs.ShowMessageAsync($"{title} failed — {Name}", result.Message);
            return result.Success;
        }
        catch (Exception ex)
        {
            _services.Log.Error($"[{Name}] {ex.Message}");
            if (interactive)
                await _services.Dialogs.ShowMessageAsync($"{title} failed — {Name}", ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    // ---- Operations also used for project-wide (bulk) actions ---------------------------------

    public Task<bool> FetchAsync(bool interactive) =>
        RunActionAsync("Fetch", () => _services.Git.FetchAsync(Path), interactive);

    public Task<bool> PullAsync(bool interactive) =>
        RunActionAsync("Pull", () => _services.Git.PullAsync(Path), interactive);

    public Task<bool> SwitchToAsync(string branchName, bool interactive) =>
        RunActionAsync("Switch branch", () => _services.Git.SwitchAsync(Path, branchName), interactive);

    public Task<bool> CreateBranchAsync(string name, string? startPoint, bool checkout, bool interactive) =>
        RunActionAsync("Create branch", () => _services.Git.CreateBranchAsync(Path, name, startPoint, checkout), interactive);

    /// <summary>Whether a local branch, or a remote branch of the same short name, exists (as of the last refresh).</summary>
    public bool HasBranch(string name) =>
        LocalBranches.Any(b => b.Name == name) || RemoteBranches.Any(b => b.ShortName == name);

    // ---- Commands -----------------------------------------------------------------------------

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private Task Fetch() => FetchAsync(interactive: true);

    [RelayCommand]
    private Task Pull() => PullAsync(interactive: true);

    [RelayCommand]
    private async Task PushAsync()
    {
        if (CurrentBranch is null)
        {
            await _services.Dialogs.ShowMessageAsync("Push", "HEAD is detached; switch to a branch before pushing.");
            return;
        }
        var branch = CurrentBranch;
        var hasUpstream = Upstream is not null;
        await RunActionAsync("Push", () => _services.Git.PushAsync(Path, branch, hasUpstream), interactive: true);
    }

    private bool CanCheckout => ActiveBranch is { IsHead: false };

    [RelayCommand(CanExecute = nameof(CanCheckout))]
    private Task Checkout()
    {
        var branch = ActiveBranch!;
        return RunActionAsync("Checkout", () => _services.Git.CheckoutAsync(Path, branch), interactive: true);
    }

    [RelayCommand]
    private async Task NewBranchAsync()
    {
        var startPoint = ActiveBranch?.Name;
        var from = startPoint ?? HeadText;
        var name = await _services.Dialogs.PromptAsync("New branch", $"Create a branch from {from} and switch to it.\n\nBranch name:");
        if (string.IsNullOrWhiteSpace(name))
            return;
        name = name.Trim();
        if (!await _services.Git.IsValidBranchNameAsync(Path, name))
        {
            await _services.Dialogs.ShowMessageAsync("New branch", $"\"{name}\" is not a valid branch name.");
            return;
        }
        await CreateBranchAsync(name, startPoint, checkout: true, interactive: true);
    }

    private bool CanDeleteBranch => BranchTabIndex == 0 && SelectedLocalBranch is { IsHead: false };

    [RelayCommand(CanExecute = nameof(CanDeleteBranch))]
    private async Task DeleteBranchAsync()
    {
        var branch = SelectedLocalBranch!;
        if (!await _services.Dialogs.ConfirmAsync("Delete branch", $"Delete the local branch \"{branch.Name}\" in {Name}?", "Delete"))
            return;

        var result = await _services.Git.DeleteBranchAsync(Path, branch.Name, force: false);
        if (!result.Success)
        {
            var force = await _services.Dialogs.ConfirmAsync("Delete branch",
                $"{result.Message}\n\nForce delete \"{branch.Name}\"? Unmerged commits on it will be lost.", "Force delete");
            if (force)
                await RunActionAsync("Delete branch", () => _services.Git.DeleteBranchAsync(Path, branch.Name, force: true), interactive: true);
        }
        await RefreshAsync();
    }

    private bool CanMergeIntoCurrent => ActiveBranch is { IsHead: false } && CurrentBranch is not null;

    [RelayCommand(CanExecute = nameof(CanMergeIntoCurrent))]
    private async Task MergeIntoCurrentAsync()
    {
        var branch = ActiveBranch!;
        if (!await _services.Dialogs.ConfirmAsync("Merge", $"Merge \"{branch.Name}\" into \"{CurrentBranch}\" in {Name}?", "Merge"))
            return;
        await RunActionAsync("Merge", () => _services.Git.MergeAsync(Path, branch.Name), interactive: true);
    }

    private bool CanCompareWithCurrent => ActiveBranch is { IsHead: false } && HeadCommit is not null;

    /// <summary>
    /// Opens WinMerge with HEAD on the left and the selected branch on the right, showing what
    /// that branch would bring in (from the merge base, if enabled in settings).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompareWithCurrent))]
    private async Task CompareWithCurrentAsync()
    {
        var branch = ActiveBranch!;
        IsBusy = true;
        try
        {
            var left = "HEAD";
            var leftTitle = $"{HeadText} (current)";
            if (_services.Settings.CompareFromMergeBase)
            {
                var mergeBase = await _services.Git.GetMergeBaseAsync(Path, "HEAD", branch.Name);
                if (mergeBase is not null)
                {
                    left = mergeBase;
                    leftTitle = $"merge base of {HeadText} and {branch.Name} ({mergeBase[..7]})";
                }
            }

            var count = await _services.Diff.CompareRevisionsAsync(Path, left, leftTitle, branch.Name, branch.Name);
            if (count == 0)
                _services.Log.Info($"[{Name}] No differences between {leftTitle} and {branch.Name}.");
            else
                _services.Log.Info($"[{Name}] Comparing {count} file(s): {leftTitle} ↔ {branch.Name}");
        }
        catch (Exception ex)
        {
            await _services.Dialogs.ShowMessageAsync("Compare", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (Directory.Exists(Path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path}\"") { UseShellExecute = false })?.Dispose();
    }
}
