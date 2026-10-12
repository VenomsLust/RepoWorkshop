using Avalonia.Controls;

namespace RepoWorkshop.Views;

/// <summary>The always-visible list of changed files beside the repository tabs, with Stage/Revert/Diff/Delete.</summary>
public partial class WorkingFilesPanel : UserControl {
    /// <summary>Builds the panel.</summary>
    public WorkingFilesPanel() {
        InitializeComponent();
    }
}
