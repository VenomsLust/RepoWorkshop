using System.Threading.Tasks;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Services;

public interface IDialogService
{
    /// <summary>Asks the user for a line of text; returns null if cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string initialValue = "");

    Task ShowMessageAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK");

    Task<string?> PickFolderAsync(string title);

    Task<string?> PickFileAsync(string title, string filterName, params string[] patterns);

    Task<bool> ShowSettingsAsync(SettingsViewModel settings);

    /// <summary>Restores and activates the main window (e.g. before asking to confirm an API request).</summary>
    void BringToFront();
}
