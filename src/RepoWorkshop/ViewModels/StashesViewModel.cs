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
public partial class StashesViewModel : ViewModelBase
{
    private readonly RepoViewModel _repo;
    private readonly AppServices _services;

    public StashesViewModel(RepoViewModel repo, AppServices services)
    {
        _repo = repo;
        _services = services;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStashes));
    }

    public ObservableCollection<StashInfo> Items { get; } = [];

    public bool HasStashes => Items.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(PopCommand), nameof(DropCommand), nameof(CompareCommand))]
    public partial StashInfo? SelectedStash { get; set; }

    public void Update(IReadOnlyList<StashInfo> stashes)
    {
        var selected = SelectedStash?.Ref;
        RepoViewModel.Replace(Items, stashes);
        SelectedStash = Items.FirstOrDefault(s => s.Ref == selected);
    }

    public void Clear() => Items.Clear();

    [RelayCommand]
    private async Task StashChangesAsync()
    {
        if (!_repo.IsDirty)
        {
            await _services.Dialogs.ShowMessageAsync("Stash", "There are no changes to stash.");
            return;
        }
        var message = await _services.Dialogs.PromptAsync("Stash changes",
            "Stash all changes, including untracked files, and reset the working tree.\n\nDescription (optional):");
        if (message is null)
            return;
        await _repo.RunActionAsync("Stash", () => _services.Git.StashPushAsync(_repo.Path, message.Trim(), includeUntracked: true),
            interactive: true);
    }

    private bool HasSelection => SelectedStash is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task Apply()
    {
        var stash = SelectedStash!;
        return _repo.RunActionAsync("Apply stash", () => _services.Git.StashApplyAsync(_repo.Path, stash.Ref), interactive: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task Pop()
    {
        var stash = SelectedStash!;
        return _repo.RunActionAsync("Pop stash", () => _services.Git.StashPopAsync(_repo.Path, stash.Ref), interactive: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DropAsync()
    {
        var stash = SelectedStash!;
        var confirmed = await _services.Dialogs.ConfirmAsync("Drop stash",
            $"Permanently delete {stash.Ref}?\n\n{stash.Message}", "Drop");
        if (confirmed)
            await _repo.RunActionAsync("Drop stash", () => _services.Git.StashDropAsync(_repo.Path, stash.Ref), interactive: true);
    }

    /// <summary>Opens the stash's tracked-file changes (vs the commit it was based on) in WinMerge.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CompareAsync()
    {
        var stash = SelectedStash!;
        try
        {
            var count = await _services.Diff.CompareRevisionsAsync(_repo.Path, $"{stash.Ref}^1", $"Base of {stash.Ref}",
                stash.Ref, $"{stash.Ref}: {stash.Message}");
            if (count == 0)
                _services.Log.Info($"[{_repo.Name}] {stash.Ref} has no tracked-file changes (it may hold only untracked files).");
        }
        catch (Exception ex)
        {
            await _services.Dialogs.ShowMessageAsync("Compare stash", ex.Message);
        }
    }
}
