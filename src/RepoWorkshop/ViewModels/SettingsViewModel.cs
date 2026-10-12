using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    public SettingsViewModel(AppSettings settings, IDialogService dialogs)
    {
        _dialogs = dialogs;
        WinMergePath = settings.WinMergePath ?? "";
        CompareFromMergeBase = settings.CompareFromMergeBase;
        Theme = settings.Theme;
        ApiEnabled = settings.ApiEnabled;
        DetectedWinMergePath = WinMergeService.Detect() ?? "(not found)";
    }

    [ObservableProperty]
    public partial string WinMergePath { get; set; }

    [ObservableProperty]
    public partial bool CompareFromMergeBase { get; set; }

    public string DetectedWinMergePath { get; }

    public string[] Themes { get; } = ["Dark", "Light", "System"];

    [ObservableProperty]
    public partial string Theme { get; set; }

    [ObservableProperty]
    public partial bool ApiEnabled { get; set; }

    [RelayCommand]
    private async Task BrowseWinMergeAsync()
    {
        var path = await _dialogs.PickFileAsync("Locate WinMergeU.exe", "WinMerge", "WinMergeU.exe", "*.exe");
        if (path is not null)
            WinMergePath = path;
    }

    public void Apply(AppSettings settings)
    {
        settings.WinMergePath = string.IsNullOrWhiteSpace(WinMergePath) ? null : WinMergePath.Trim();
        settings.CompareFromMergeBase = CompareFromMergeBase;
        settings.Theme = Theme;
        settings.ApiEnabled = ApiEnabled;
    }
}
