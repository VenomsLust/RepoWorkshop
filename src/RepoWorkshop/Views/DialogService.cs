using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RepoWorkshop.Services;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Views;

public sealed class DialogService(Func<Window> owner) : IDialogService
{
    public async Task<string?> PromptAsync(string title, string message, string initialValue = "")
    {
        var dialog = new MessageDialog(title, message, initialValue, "OK", "Cancel");
        return await dialog.ShowDialog<bool>(owner()) ? dialog.InputText : null;
    }

    public Task ShowMessageAsync(string title, string message) =>
        new MessageDialog(title, message, null, "OK", null).ShowDialog<bool>(owner());

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK") =>
        new MessageDialog(title, message, null, confirmText, "Cancel").ShowDialog<bool>(owner());

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner().StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(string title, string filterName, params string[] patterns)
    {
        var files = await owner().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public Task<bool> ShowSettingsAsync(SettingsViewModel settings) =>
        new SettingsWindow { DataContext = settings }.ShowDialog<bool>(owner());
}
