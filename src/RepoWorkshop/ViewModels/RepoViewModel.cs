using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>One repository within a project: its status, branches, changes, history and stashes.</summary>
public partial class RepoViewModel : ViewModelBase, IDisposable {
    /// <summary>Index of the Branches tab.</summary>
    public const int BranchesTab = 0;

    /// <summary>Index of the Changes tab.</summary>
    public const int ChangesTab = 1;

    /// <summary>Index of the History tab.</summary>
    public const int HistoryTab = 2;

    /// <summary>Index of the Stashes tab.</summary>
    public const int StashesTab = 3;

    /// <summary>Shared app services.</summary>
    private readonly AppServices _services;

    /// <summary>The refresh in progress (or the last one), shared by concurrent callers.</summary>
    private Task? _refreshTask;

    /// <summary>Set when a refresh is requested while one is running, so it runs once more afterwards.</summary>
    private bool _refreshAgain;

    /// <summary>Refreshes when files change on disk; null if the folder couldn't be watched.</summary>
    private RepoWatcher? _watcher;

    /// <summary>Creates the repository and its tab view models, and starts watching its folder; call <see cref="RefreshAsync"/> to load it.</summary>
    public RepoViewModel(RepoModel model, AppServices services) {
        Model = model;
        _services = services;
        Changes = new ChangesViewModel(this, services);
        History = new HistoryViewModel(this, services);
        Stashes = new StashesViewModel(this, services);
        StartWatching();
    }

    /// <summary>The saved repository reference.</summary>
    public RepoModel Model { get; }

    /// <summary>The working tree's top-level folder.</summary>
    public string Path => Model.Path;

    /// <summary>The alias, or the folder name.</summary>
    public string Name => string.IsNullOrWhiteSpace(Model.Alias)
        ? System.IO.Path.GetFileName(Model.Path.TrimEnd('\\', '/'))
        : Model.Alias;

    /// <summary>Local branches, most recently committed first.</summary>
    public ObservableCollection<BranchInfo> LocalBranches { get; } = [];

    /// <summary>Remote-tracking branches, most recently committed first.</summary>
    public ObservableCollection<BranchInfo> RemoteBranches { get; } = [];

    /// <summary>The Changes tab.</summary>
    public ChangesViewModel Changes { get; }

    /// <summary>The History tab.</summary>
    public HistoryViewModel History { get; }

    /// <summary>The Stashes tab.</summary>
    public StashesViewModel Stashes { get; }

    /// <summary>The selected tab: Branches, Changes, History or Stashes.</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>The branch name, or a description of a detached HEAD.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial string HeadText { get; set; } = "…";

    /// <summary>The checked-out branch, or null when HEAD is detached or not yet loaded.</summary>
    [ObservableProperty]
    public partial string? CurrentBranch { get; set; }

    /// <summary>The HEAD commit id; null in a repository with no commits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHead))]
    public partial string? HeadCommit { get; set; }

    /// <summary>The current branch's upstream, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial string? Upstream { get; set; }

    /// <summary>Commits not yet pushed to the upstream.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial int Ahead { get; set; }

    /// <summary>Upstream commits not yet pulled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncText))]
    public partial int Behind { get; set; }

    /// <summary>Number of paths with staged or unstaged changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangesText), nameof(IsDirty))]
    public partial int ChangeCount { get; set; }

    /// <summary>Why the repository could not be read, if it couldn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>Whether a git action is running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The selection on the Local branches tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveBranch))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand), nameof(DeleteBranchCommand), nameof(MergeIntoCurrentCommand),
        nameof(CompareWithCurrentCommand))]
    public partial BranchInfo? SelectedLocalBranch { get; set; }

    /// <summary>The selection on the Remote branches tab.</summary>
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

    /// <summary>Whether <see cref="Error"/> is set.</summary>
    public bool HasError => Error is not null;

    /// <summary>False in a new repository with no commits yet.</summary>
    public bool HasHead => HeadCommit is not null;

    /// <summary>Whether there are uncommitted changes.</summary>
    public bool IsDirty => ChangeCount > 0;

    /// <summary>E.g. "clean" or "3 changes".</summary>
    public string ChangesText => ChangeCount switch {
        0 => "clean",
        1 => "1 change",
        int n => $"{n} changes",
    };

    /// <summary>E.g. "↑1 ↓2", "up to date" or "no upstream".</summary>
    public string SyncText {
        get {
            if (Upstream is null) {
                return CurrentBranch is null ? "" : "no upstream";
            }
            List<string> parts = [];
            if (Ahead > 0) {
                parts.Add($"↑{Ahead}");
            }
            if (Behind > 0) {
                parts.Add($"↓{Behind}");
            }
            return parts.Count == 0 ? "up to date" : string.Join(' ', parts);
        }
    }

    /// <summary>Whether Checkout applies to the selected branch.</summary>
    private bool CanCheckout => ActiveBranch is { IsHead: false };

    /// <summary>Whether Delete applies to the selected branch.</summary>
    private bool CanDeleteBranch => BranchTabIndex == 0 && SelectedLocalBranch is { IsHead: false };

    /// <summary>Whether Merge applies to the selected branch.</summary>
    private bool CanMergeIntoCurrent => ActiveBranch is { IsHead: false } && CurrentBranch is not null;

    /// <summary>Whether Compare applies to the selected branch.</summary>
    private bool CanCompareWithCurrent => ActiveBranch is { IsHead: false } && HeadCommit is not null;

    /// <summary>Raises a change notification for <see cref="Name"/> after the alias changes.</summary>
    public void NotifyNameChanged() => OnPropertyChanged(nameof(Name));

    /// <summary>
    /// Refreshes status, branches and stashes. A call during a refresh joins it, and makes it
    /// run once more at the end so changes made meanwhile are not missed.
    /// </summary>
    public Task RefreshAsync() {
        if (_refreshTask is { IsCompleted: false }) {
            _refreshAgain = true;
            return _refreshTask;
        }
        return _refreshTask = RefreshLoopAsync();
    }

    /// <summary>Loads the repository if it has never been loaded.</summary>
    public Task EnsureLoadedAsync() => _refreshTask ?? RefreshAsync();

    /// <summary>
    /// Runs a git action with the busy indicator showing, then refreshes. Never shows dialogs;
    /// failures (including exceptions) come back as an unsuccessful result.
    /// </summary>
    public async Task<GitResult> ExecuteAsync(Func<Task<GitResult>> action) {
        if (IsBusy) {
            return new GitResult(-1, "", $"{Name} is busy with another operation; try again shortly.");
        }

        IsBusy = true;
        try {
            return await action();
        } catch (Exception ex) {
            _services.Log.Error($"[{Name}] {ex.Message}");
            return new GitResult(-1, "", ex.Message);
        } finally {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    /// <summary>Fetches all remotes.</summary>
    public Task<GitResult> FetchAsync() => ExecuteAsync(() => _services.Git.FetchAsync(Path));

    /// <summary>Pulls the current branch (fast-forward only).</summary>
    public Task<GitResult> PullAsync() => ExecuteAsync(() => _services.Git.PullAsync(Path));

    /// <summary>Switches to a branch by name.</summary>
    public Task<GitResult> SwitchToAsync(string branchName) => ExecuteAsync(() => _services.Git.SwitchAsync(Path, branchName));

    /// <summary>Creates a branch, optionally switching to it.</summary>
    public Task<GitResult> CreateBranchAsync(string name, string? startPoint, bool checkout) =>
        ExecuteAsync(() => _services.Git.CreateBranchAsync(Path, name, startPoint, checkout));

    /// <summary>
    /// Opens WinMerge with HEAD (or its merge base with the branch) on the left and the branch on the right.
    /// Returns the number of differing files; WinMerge is not opened when there are none.
    /// </summary>
    public async Task<int> CompareWithBranchAsync(string branch, bool fromMergeBase) {
        string left = "HEAD";
        string leftTitle = $"{HeadText} (current)";
        if (fromMergeBase && await _services.Git.GetMergeBaseAsync(Path, "HEAD", branch) is { } mergeBase) {
            left = mergeBase;
            leftTitle = $"merge base of {HeadText} and {branch} ({mergeBase[..7]})";
        }

        int count = await _services.Diff.CompareRevisionsAsync(Path, left, leftTitle, branch, branch);
        _services.Log.Info(count == 0
            ? $"[{Name}] No differences between {leftTitle} and {branch}."
            : $"[{Name}] Comparing {count} file(s): {leftTitle} ↔ {branch}");
        return count;
    }

    /// <summary>Whether a local branch, or a remote branch of the same short name, exists (as of the last refresh).</summary>
    public bool HasBranch(string name) =>
        LocalBranches.Any(b => b.Name == name) || RemoteBranches.Any(b => b.ShortName == name);

    /// <summary>Like <see cref="ExecuteAsync"/>, but reports a failure in a dialog when <paramref name="interactive"/>.</summary>
    internal async Task<bool> RunActionAsync(string title, Func<Task<GitResult>> action, bool interactive) {
        GitResult result = await ExecuteAsync(action);
        if (!result.Success && interactive) {
            await _services.Dialogs.ShowMessageAsync($"{title} failed — {Name}", result.Message);
        }
        return result.Success;
    }

    /// <summary>Stops watching the folder.</summary>
    public void Dispose() {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Makes <paramref name="target"/> equal to <paramref name="items"/> (same order) with the fewest
    /// changes, so unchanged items keep their identity and list selections survive a refresh.
    /// </summary>
    internal static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items) {
        HashSet<T> wanted = [.. items];
        for (int i = target.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(target[i])) {
                target.RemoveAt(i);
            }
        }
        for (int i = 0; i < items.Count; i++) {
            if (i < target.Count && EqualityComparer<T>.Default.Equals(target[i], items[i])) {
                continue;
            }
            int existing = target.IndexOf(items[i]);
            if (existing > i) {
                target.Move(existing, i);
            } else {
                target.Insert(i, items[i]);
            }
        }
    }

    /// <summary>Replaces a collection's contents.</summary>
    internal static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items) {
        target.Clear();
        foreach (T item in items) {
            target.Add(item);
        }
    }

    /// <summary>Refreshes until no further refresh was requested while one was running.</summary>
    private async Task RefreshLoopAsync() {
        do {
            _refreshAgain = false;
            await RefreshCoreAsync();
        } while (_refreshAgain);
    }

    /// <summary>Starts the file watcher, if the folder exists.</summary>
    private void StartWatching() {
        try {
            _watcher = new RepoWatcher(Path);
            _watcher.Changed += OnFilesChanged;
        } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) {
            // Missing or inaccessible folder: the repository shows an error on refresh instead.
            _watcher = null;
        }
    }

    /// <summary>Reads the repository's state into the view model, or records the error.</summary>
    private async Task RefreshCoreAsync() {
        try {
            Task<RepoStatus> statusTask = _services.Git.GetStatusAsync(Path);
            Task<IReadOnlyList<BranchInfo>> branchesTask = _services.Git.GetBranchesAsync(Path);
            Task<IReadOnlyList<StashInfo>> stashesTask = _services.Git.GetStashesAsync(Path);
            RepoStatus status = await statusTask;
            IReadOnlyList<BranchInfo> branches = await branchesTask;
            IReadOnlyList<StashInfo> stashes = await stashesTask;

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

            string? selectedLocal = SelectedLocalBranch?.Name;
            string? selectedRemote = SelectedRemoteBranch?.Name;
            Replace(LocalBranches, branches.Where(b => !b.IsRemote));
            Replace(RemoteBranches, branches.Where(b => b.IsRemote));
            SelectedLocalBranch = LocalBranches.FirstOrDefault(b => b.Name == selectedLocal);
            SelectedRemoteBranch = RemoteBranches.FirstOrDefault(b => b.Name == selectedRemote);
            LoadHistoryIfVisible();
        } catch (Exception ex) {
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

    /// <summary>Shows the selected branch's history if the History tab is open.</summary>
    private void LoadHistoryIfVisible() {
        if (SelectedTabIndex == HistoryTab) {
            _ = History.ShowAsync(ActiveBranch?.Name ?? "HEAD", ActiveBranch?.Name ?? HeadText);
        }
    }

    /// <summary>Reloads the repository's state.</summary>
    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    /// <summary>Fetches, reporting failures in a dialog.</summary>
    [RelayCommand]
    private Task Fetch() => RunActionAsync("Fetch", () => _services.Git.FetchAsync(Path), interactive: true);

    /// <summary>Pulls, reporting failures in a dialog.</summary>
    [RelayCommand]
    private Task Pull() => RunActionAsync("Pull", () => _services.Git.PullAsync(Path), interactive: true);

    /// <summary>Pushes the current branch, setting its upstream on the first push.</summary>
    [RelayCommand]
    private async Task PushAsync() {
        if (CurrentBranch is null) {
            await _services.Dialogs.ShowMessageAsync("Push", "HEAD is detached; switch to a branch before pushing.");
            return;
        }
        string branch = CurrentBranch;
        bool hasUpstream = Upstream is not null;
        await RunActionAsync("Push", () => _services.Git.PushAsync(Path, branch, hasUpstream), interactive: true);
    }

    /// <summary>Switches to the selected branch.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckout))]
    private Task Checkout() {
        BranchInfo branch = ActiveBranch!;
        return RunActionAsync("Checkout", () => _services.Git.CheckoutAsync(Path, branch), interactive: true);
    }

    /// <summary>Asks for a name and creates a branch from the selected branch (or HEAD).</summary>
    [RelayCommand]
    private async Task NewBranchAsync() {
        string? startPoint = ActiveBranch?.Name;
        string from = startPoint ?? HeadText;
        string? name = await _services.Dialogs.PromptAsync("New branch", $"Create a branch from {from} and switch to it.\n\nBranch name:");
        if (string.IsNullOrWhiteSpace(name)) {
            return;
        }

        name = name.Trim();
        if (!await _services.Git.IsValidBranchNameAsync(Path, name)) {
            await _services.Dialogs.ShowMessageAsync("New branch", $"\"{name}\" is not a valid branch name.");
            return;
        }
        await RunActionAsync("Create branch", () => _services.Git.CreateBranchAsync(Path, name, startPoint, checkout: true), interactive: true);
    }

    /// <summary>Deletes the selected local branch after confirming, offering to force it if unmerged.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteBranch))]
    private async Task DeleteBranchAsync() {
        BranchInfo branch = SelectedLocalBranch!;
        if (!await _services.Dialogs.ConfirmAsync("Delete branch", $"Delete the local branch \"{branch.Name}\" in {Name}?", "Delete")) {
            return;
        }

        GitResult result = await _services.Git.DeleteBranchAsync(Path, branch.Name, force: false);
        if (!result.Success) {
            bool force = await _services.Dialogs.ConfirmAsync("Delete branch",
                $"{result.Message}\n\nForce delete \"{branch.Name}\"? Unmerged commits on it will be lost.", "Force delete");
            if (force) {
                await RunActionAsync("Delete branch", () => _services.Git.DeleteBranchAsync(Path, branch.Name, force: true), interactive: true);
            }
        }
        await RefreshAsync();
    }

    /// <summary>Merges the selected branch into the current one after confirming.</summary>
    [RelayCommand(CanExecute = nameof(CanMergeIntoCurrent))]
    private async Task MergeIntoCurrentAsync() {
        BranchInfo branch = ActiveBranch!;
        if (await _services.Dialogs.ConfirmAsync("Merge", $"Merge \"{branch.Name}\" into \"{CurrentBranch}\" in {Name}?", "Merge")) {
            await RunActionAsync("Merge", () => _services.Git.MergeAsync(Path, branch.Name), interactive: true);
        }
    }

    /// <summary>
    /// Opens WinMerge with HEAD on the left and the selected branch on the right, showing what
    /// that branch would bring in (from the merge base, if enabled in settings).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompareWithCurrent))]
    private async Task CompareWithCurrentAsync() {
        BranchInfo branch = ActiveBranch!;
        IsBusy = true;
        try {
            await CompareWithBranchAsync(branch.Name, _services.Settings.CompareFromMergeBase);
        } catch (Exception ex) {
            await _services.Dialogs.ShowMessageAsync("Compare", ex.Message);
        } finally {
            IsBusy = false;
        }
    }

    /// <summary>Opens the repository folder in Explorer.</summary>
    [RelayCommand]
    private void OpenInExplorer() {
        if (Directory.Exists(Path)) {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path}\"") { UseShellExecute = false })?.Dispose();
        }
    }

    /// <summary>Refreshes (on the UI thread) after files change on disk.</summary>
    private void OnFilesChanged() => Dispatcher.UIThread.Post(() => _ = RefreshAsync());

    /// <summary>Loads history when the History tab is opened.</summary>
    partial void OnSelectedTabIndexChanged(int value) => LoadHistoryIfVisible();

    /// <summary>History follows the selected local branch.</summary>
    partial void OnSelectedLocalBranchChanged(BranchInfo? value) => LoadHistoryIfVisible();

    /// <summary>History follows the selected remote branch.</summary>
    partial void OnSelectedRemoteBranchChanged(BranchInfo? value) => LoadHistoryIfVisible();

    /// <summary>History follows the branch tab being shown.</summary>
    partial void OnBranchTabIndexChanged(int value) => LoadHistoryIfVisible();
}
