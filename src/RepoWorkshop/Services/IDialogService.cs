using System.Threading.Tasks;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Services;

/// <summary>Dialogs and pickers, so view models can talk to the user without depending on windows.</summary>
public interface IDialogService {
    /// <summary>Asks the user for a line of text; returns null if cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string initialValue = "");

    /// <summary>Shows a message with an OK button.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Asks the user to confirm; returns true if they chose <paramref name="confirmText"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK");

    /// <summary>Lets the user pick a folder; returns null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Lets the user pick a file matching <paramref name="patterns"/>; returns null if cancelled.</summary>
    Task<string?> PickFileAsync(string title, string filterName, params string[] patterns);

    /// <summary>Shows the settings window; returns true if the user saved.</summary>
    Task<bool> ShowSettingsAsync(SettingsViewModel settings);

    /// <summary>Restores and activates the main window (e.g. before asking to confirm an API request).</summary>
    void BringToFront();
}
