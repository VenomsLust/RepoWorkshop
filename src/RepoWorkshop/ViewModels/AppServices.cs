using System;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>The shared services handed to every view model.</summary>
public sealed class AppServices {
    /// <summary>Persists <see cref="Settings"/>.</summary>
    private readonly SettingsStore _store;

    /// <summary>Bundles the app's services.</summary>
    public AppServices(
        SettingsStore store,
        AppSettings settings,
        GitService git,
        WinMergeService winMerge,
        DiffService diff,
        LogService log,
        IDialogService dialogs) {
        _store = store;
        Settings = settings;
        Git = git;
        WinMerge = winMerge;
        Diff = diff;
        Log = log;
        Dialogs = dialogs;
    }

    /// <summary>The app's settings and projects; call <see cref="SaveSettings"/> after changing them.</summary>
    public AppSettings Settings { get; }

    /// <summary>Runs git.</summary>
    public GitService Git { get; }

    /// <summary>Locates and launches WinMerge.</summary>
    public WinMergeService WinMerge { get; }

    /// <summary>Opens file and revision comparisons in WinMerge.</summary>
    public DiffService Diff { get; }

    /// <summary>The output log.</summary>
    public LogService Log { get; }

    /// <summary>Dialogs and pickers.</summary>
    public IDialogService Dialogs { get; }

    /// <summary>Saves <see cref="Settings"/>, logging (rather than throwing) on failure.</summary>
    public void SaveSettings() {
        try {
            _store.Save(Settings);
        } catch (Exception ex) {
            Log.Error($"Could not save settings to {_store.FilePath}: {ex.Message}");
        }
    }
}
