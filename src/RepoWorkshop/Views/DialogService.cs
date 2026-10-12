using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RepoWorkshop.Services;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Views;

/// <summary>Shows dialogs and pickers over the main window.</summary>
public sealed class DialogService : IDialogService {
    /// <summary>Returns the window dialogs are shown over.</summary>
    private readonly Func<Window> _owner;

    /// <summary>Creates the service; <paramref name="owner"/> returns the main window.</summary>
    public DialogService(Func<Window> owner) {
        _owner = owner;
    }

    /// <inheritdoc />
    public async Task<string?> PromptAsync(string title, string message, string initialValue = "") {
        MessageDialog dialog = new(title, message, initialValue, "OK", "Cancel");
        return await dialog.ShowDialog<bool>(_owner()) ? dialog.InputText : null;
    }

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message) =>
        new MessageDialog(title, message, null, "OK", null).ShowDialog<bool>(_owner());

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK") =>
        new MessageDialog(title, message, null, confirmText, "Cancel").ShowDialog<bool>(_owner());

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title) {
        IReadOnlyList<IStorageFolder> folders = await _owner().StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = title,
            AllowMultiple = false,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<string?> PickFileAsync(string title, string filterName, params string[] patterns) {
        IReadOnlyList<IStorageFile> files = await _owner().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public Task<bool> ShowSettingsAsync(SettingsViewModel settings) =>
        new SettingsWindow { DataContext = settings }.ShowDialog<bool>(_owner());

    /// <inheritdoc />
    public void BringToFront() {
        Window window = _owner();
        if (window.WindowState == WindowState.Minimized) {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }
}
