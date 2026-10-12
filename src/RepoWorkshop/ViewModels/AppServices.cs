using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>The shared services handed to every view model.</summary>
public sealed class AppServices(
    SettingsStore store,
    AppSettings settings,
    GitService git,
    WinMergeService winMerge,
    DiffService diff,
    LogService log,
    IDialogService dialogs)
{
    public AppSettings Settings { get; } = settings;

    public GitService Git { get; } = git;

    public WinMergeService WinMerge { get; } = winMerge;

    public DiffService Diff { get; } = diff;

    public LogService Log { get; } = log;

    public IDialogService Dialogs { get; } = dialogs;

    public void SaveSettings()
    {
        try
        {
            store.Save(Settings);
        }
        catch (System.Exception ex)
        {
            Log.Error($"Could not save settings to {store.FilePath}: {ex.Message}");
        }
    }
}
