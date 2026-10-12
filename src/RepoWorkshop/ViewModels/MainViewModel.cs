using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Projects = new ObservableCollection<ProjectViewModel>(
            services.Settings.Projects.Select(p => new ProjectViewModel(p, services)));
        SelectedProject = Projects.FirstOrDefault(p => p.Model.Id == services.Settings.LastProjectId) ?? Projects.FirstOrDefault();

        services.Log.EntryAdded += entry =>
        {
            if (entry.Level != LogLevel.Command)
            {
                StatusText = entry.Text.Split('\n')[0];
                StatusIsError = entry.IsError;
            }
        };
        UpdateWinMergeStatus();
    }

    public ObservableCollection<ProjectViewModel> Projects { get; }

    public ObservableCollection<LogEntry> LogEntries => _services.Log.Entries;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameProjectCommand), nameof(RemoveProjectCommand))]
    public partial ProjectViewModel? SelectedProject { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    public partial string WinMergeStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLogVisible { get; set; }

    /// <summary>Shown in the status bar, e.g. "API: on".</summary>
    [ObservableProperty]
    public partial string ApiStatus { get; set; } = "";

    /// <summary>Raised after settings are saved, so the app can start or stop the API server.</summary>
    public event System.Action? SettingsApplied;

    partial void OnSelectedProjectChanged(ProjectViewModel? value)
    {
        _services.Settings.LastProjectId = value?.Model.Id;
        _services.SaveSettings();
        value?.ActivateAsync();
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        var name = await _services.Dialogs.PromptAsync("New project", "Project name:");
        if (string.IsNullOrWhiteSpace(name))
            return;

        SelectedProject = CreateProject(name.Trim());
    }

    public ProjectViewModel CreateProject(string name)
    {
        var model = new ProjectModel { Name = name };
        _services.Settings.Projects.Add(model);
        _services.SaveSettings();

        var project = new ProjectViewModel(model, _services);
        Projects.Add(project);
        return project;
    }

    private bool HasSelectedProject => SelectedProject is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedProject))]
    private async Task RenameProjectAsync()
    {
        var project = SelectedProject!;
        var name = await _services.Dialogs.PromptAsync("Rename project", "Project name:", project.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;
        project.Name = name.Trim();
        _services.SaveSettings();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProject))]
    private async Task RemoveProjectAsync()
    {
        var project = SelectedProject!;
        var confirmed = await _services.Dialogs.ConfirmAsync("Remove project",
            $"Remove the project \"{project.Name}\"?\n\nThe repositories on disk are not affected.", "Remove");
        if (confirmed)
            RemoveProject(project);
    }

    /// <summary>Removes a project from the app (its repositories on disk are not touched).</summary>
    public void RemoveProject(ProjectViewModel project)
    {
        var index = Projects.IndexOf(project);
        Projects.Remove(project);
        _services.Settings.Projects.Remove(project.Model);
        _services.SaveSettings();
        if (SelectedProject is null || SelectedProject == project)
            SelectedProject = Projects.Count == 0 ? null : Projects[System.Math.Min(index, Projects.Count - 1)];
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var vm = new SettingsViewModel(_services.Settings, _services.Dialogs);
        if (await _services.Dialogs.ShowSettingsAsync(vm))
        {
            vm.Apply(_services.Settings);
            App.ApplyTheme(_services.Settings.Theme);
            _services.SaveSettings();
            UpdateWinMergeStatus();
            SettingsApplied?.Invoke();
        }
    }

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

    [RelayCommand]
    private void ClearLog() => _services.Log.Entries.Clear();

    private void UpdateWinMergeStatus()
    {
        var exe = _services.WinMerge.ResolveExecutable();
        WinMergeStatus = exe is null ? "WinMerge: not found" : $"WinMerge: {exe}";
    }
}
