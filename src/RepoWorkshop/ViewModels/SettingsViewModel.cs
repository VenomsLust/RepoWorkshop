using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>The settings window: an editable copy of the settings, applied only when saved.</summary>
public partial class SettingsViewModel : ViewModelBase {
    /// <summary>Shows the file picker.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>Copies the current values from <paramref name="settings"/>.</summary>
    public SettingsViewModel(AppSettings settings, IDialogService dialogs) {
        _dialogs = dialogs;
        WinMergePath = settings.WinMergePath ?? "";
        CompareFromMergeBase = settings.CompareFromMergeBase;
        Theme = settings.Theme;
        ApiEnabled = settings.ApiEnabled;
        DetectedWinMergePath = WinMergeService.Detect() ?? "(not found)";
    }

    /// <summary>Explicit WinMerge path; empty to auto-detect.</summary>
    [ObservableProperty]
    public partial string WinMergePath { get; set; }

    /// <summary>Compare branches from their merge base.</summary>
    [ObservableProperty]
    public partial bool CompareFromMergeBase { get; set; }

    /// <summary>The selected theme.</summary>
    [ObservableProperty]
    public partial string Theme { get; set; }

    /// <summary>Whether the named-pipe API is enabled.</summary>
    [ObservableProperty]
    public partial bool ApiEnabled { get; set; }

    /// <summary>The auto-detected WinMerge path, shown as a hint.</summary>
    public string DetectedWinMergePath { get; }

    /// <summary>The theme choices.</summary>
    public string[] Themes { get; } = ["Dark", "Light", "System"];

    /// <summary>Copies the edited values back into <paramref name="settings"/>.</summary>
    public void Apply(AppSettings settings) {
        settings.WinMergePath = string.IsNullOrWhiteSpace(WinMergePath) ? null : WinMergePath.Trim();
        settings.CompareFromMergeBase = CompareFromMergeBase;
        settings.Theme = Theme;
        settings.ApiEnabled = ApiEnabled;
    }

    /// <summary>Lets the user pick WinMergeU.exe.</summary>
    [RelayCommand]
    private async Task BrowseWinMergeAsync() {
        string? path = await _dialogs.PickFileAsync("Locate WinMergeU.exe", "WinMerge", "WinMergeU.exe", "*.exe");
        if (path is not null) {
            WinMergePath = path;
        }
    }
}
