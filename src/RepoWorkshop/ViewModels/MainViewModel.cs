using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

/// <summary>The main window: the project list, status bar and output log.</summary>
public partial class MainViewModel : ViewModelBase {
    /// <summary>Shared app services.</summary>
    private readonly AppServices _services;

    /// <summary>Raised after settings are saved, so the app can start or stop the API server.</summary>
    public event Action? SettingsApplied;

    /// <summary>Loads the projects and restores the last selection.</summary>
    public MainViewModel(AppServices services) {
        _services = services;
        Projects = new ObservableCollection<ProjectViewModel>(
            services.Settings.Projects.Select(p => new ProjectViewModel(p, services)));
        SelectedProject = Projects.FirstOrDefault(p => p.Model.Id == services.Settings.LastProjectId) ?? Projects.FirstOrDefault();

        services.Log.EntryAdded += OnLogEntryAdded;
        UpdateWinMergeStatus();
    }

    /// <summary>All projects, in display order.</summary>
    public ObservableCollection<ProjectViewModel> Projects { get; }

    /// <summary>The output log.</summary>
    public ObservableCollection<LogEntry> LogEntries => _services.Log.Entries;

    /// <summary>The project shown on the right.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameProjectCommand), nameof(RemoveProjectCommand))]
    public partial ProjectViewModel? SelectedProject { get; set; }

    /// <summary>The latest status message, shown in the status bar.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    /// <summary>Whether <see cref="StatusText"/> is an error.</summary>
    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    /// <summary>Which WinMerge is in use, shown in the status bar.</summary>
    [ObservableProperty]
    public partial string WinMergeStatus { get; set; } = "";

    /// <summary>Whether the output log panel is open.</summary>
    [ObservableProperty]
    public partial bool IsLogVisible { get; set; }

    /// <summary>Shown in the status bar, e.g. "API: on".</summary>
    [ObservableProperty]
    public partial string ApiStatus { get; set; } = "";

    /// <summary>Whether a project is selected (enables rename/remove).</summary>
    private bool HasSelectedProject => SelectedProject is not null;

    /// <summary>Creates and saves a new, empty project.</summary>
    public ProjectViewModel CreateProject(string name) {
        ProjectModel model = new() { Name = name };
        _services.Settings.Projects.Add(model);
        _services.SaveSettings();

        ProjectViewModel project = new(model, _services);
        Projects.Add(project);
        return project;
    }

    /// <summary>Removes a project from the app (its repositories on disk are not touched).</summary>
    public void RemoveProject(ProjectViewModel project) {
        int index = Projects.IndexOf(project);
        Projects.Remove(project);
        project.DisposeRepos();
        _services.Settings.Projects.Remove(project.Model);
        _services.SaveSettings();
        if (SelectedProject is null || SelectedProject == project) {
            SelectedProject = Projects.Count == 0 ? null : Projects[Math.Min(index, Projects.Count - 1)];
        }
    }

    /// <summary>Asks for a name and creates a project.</summary>
    [RelayCommand]
    private async Task AddProjectAsync() {
        string? name = await _services.Dialogs.PromptAsync("New project", "Project name:");
        if (!string.IsNullOrWhiteSpace(name)) {
            SelectedProject = CreateProject(name.Trim());
        }
    }

    /// <summary>Renames the selected project.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedProject))]
    private async Task RenameProjectAsync() {
        ProjectViewModel project = SelectedProject!;
        string? name = await _services.Dialogs.PromptAsync("Rename project", "Project name:", project.Name);
        if (string.IsNullOrWhiteSpace(name)) {
            return;
        }
        project.Name = name.Trim();
        _services.SaveSettings();
    }

    /// <summary>Removes the selected project after confirming.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedProject))]
    private async Task RemoveProjectAsync() {
        ProjectViewModel project = SelectedProject!;
        bool confirmed = await _services.Dialogs.ConfirmAsync("Remove project",
            $"Remove the project \"{project.Name}\"?\n\nThe repositories on disk are not affected.", "Remove");
        if (confirmed) {
            RemoveProject(project);
        }
    }

    /// <summary>Shows the settings window and applies the changes if saved.</summary>
    [RelayCommand]
    private async Task OpenSettingsAsync() {
        SettingsViewModel vm = new(_services.Settings, _services.Dialogs);
        if (!await _services.Dialogs.ShowSettingsAsync(vm)) {
            return;
        }
        vm.Apply(_services.Settings);
        App.ApplyTheme(_services.Settings.Theme);
        _services.SaveSettings();
        UpdateWinMergeStatus();
        SettingsApplied?.Invoke();
    }

    /// <summary>Empties the output log.</summary>
    [RelayCommand]
    private void ClearLog() => _services.Log.Entries.Clear();

    /// <summary>Refreshes <see cref="WinMergeStatus"/>.</summary>
    private void UpdateWinMergeStatus() {
        string? exe = _services.WinMerge.ResolveExecutable();
        WinMergeStatus = exe is null ? "WinMerge: not found" : $"WinMerge: {exe}";
    }

    /// <summary>Shows the latest non-command log entry in the status bar.</summary>
    private void OnLogEntryAdded(LogEntry entry) {
        if (entry.Level != LogLevel.Command) {
            StatusText = entry.Text.Split('\n')[0];
            StatusIsError = entry.IsError;
        }
    }

    /// <summary>Remembers the selection and loads the project's repositories the first time it is shown.</summary>
    partial void OnSelectedProjectChanged(ProjectViewModel? value) {
        _services.Settings.LastProjectId = value?.Model.Id;
        _services.SaveSettings();
        _ = value?.ActivateAsync();
    }
}
