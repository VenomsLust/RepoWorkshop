using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;

namespace RepoWorkshop.ViewModels;

/// <summary>The repository's stash list.</summary>
public partial class StashesViewModel : ViewModelBase {
    /// <summary>The repository these stashes belong to.</summary>
    private readonly RepoViewModel _repo;

    /// <summary>Shared app services.</summary>
    private readonly AppServices _services;

    /// <summary>Creates the tab for <paramref name="repo"/>; its list is filled by <see cref="Update"/>.</summary>
    public StashesViewModel(RepoViewModel repo, AppServices services) {
        _repo = repo;
        _services = services;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStashes));
    }

    /// <summary>The stashes, newest first.</summary>
    public ObservableCollection<StashInfo> Items { get; } = [];

    /// <summary>Whether there are any stashes.</summary>
    public bool HasStashes => Items.Count > 0;

    /// <summary>The stash the buttons act on.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(PopCommand), nameof(DropCommand), nameof(CompareCommand))]
    public partial StashInfo? SelectedStash { get; set; }

    /// <summary>Whether a stash is selected.</summary>
    private bool HasSelection => SelectedStash is not null;

    /// <summary>Fills the list from a fresh read, keeping the selection where possible.</summary>
    public void Update(IReadOnlyList<StashInfo> stashes) {
        string? selected = SelectedStash?.Ref;
        RepoViewModel.Replace(Items, stashes);
        SelectedStash = Items.FirstOrDefault(s => s.Ref == selected);
    }

    /// <summary>Empties the list (e.g. when the repository can't be read).</summary>
    public void Clear() => Items.Clear();

    /// <summary>Asks for a description and stashes all changes, including untracked files.</summary>
    [RelayCommand]
    private async Task StashChangesAsync() {
        if (!_repo.IsDirty) {
            await _services.Dialogs.ShowMessageAsync("Stash", "There are no changes to stash.");
            return;
        }

        string? message = await _services.Dialogs.PromptAsync("Stash changes",
            "Stash all changes, including untracked files, and reset the working tree.\n\nDescription (optional):");
        if (message is null) {
            return;
        }
        await _repo.RunActionAsync("Stash", () => _services.Git.StashPushAsync(_repo.Path, message.Trim(), includeUntracked: true),
            interactive: true);
    }

    /// <summary>Applies the selected stash and keeps it.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task Apply() {
        StashInfo stash = SelectedStash!;
        return _repo.RunActionAsync("Apply stash", () => _services.Git.StashApplyAsync(_repo.Path, stash.Ref), interactive: true);
    }

    /// <summary>Applies the selected stash and removes it.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task Pop() {
        StashInfo stash = SelectedStash!;
        return _repo.RunActionAsync("Pop stash", () => _services.Git.StashPopAsync(_repo.Path, stash.Ref), interactive: true);
    }

    /// <summary>Deletes the selected stash after confirming.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DropAsync() {
        StashInfo stash = SelectedStash!;
        bool confirmed = await _services.Dialogs.ConfirmAsync("Drop stash",
            $"Permanently delete {stash.Ref}?\n\n{stash.Message}", "Drop");
        if (confirmed) {
            await _repo.RunActionAsync("Drop stash", () => _services.Git.StashDropAsync(_repo.Path, stash.Ref), interactive: true);
        }
    }

    /// <summary>Opens the stash's tracked-file changes (vs the commit it was based on) in WinMerge.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CompareAsync() {
        StashInfo stash = SelectedStash!;
        try {
            int count = await _services.Diff.CompareRevisionsAsync(_repo.Path, $"{stash.Ref}^1", $"Base of {stash.Ref}",
                stash.Ref, $"{stash.Ref}: {stash.Message}");
            if (count == 0) {
                _services.Log.Info($"[{_repo.Name}] {stash.Ref} has no tracked-file changes (it may hold only untracked files).");
            }
        } catch (Exception ex) {
            await _services.Dialogs.ShowMessageAsync("Compare stash", ex.Message);
        }
    }
}
