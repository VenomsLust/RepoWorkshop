namespace RepoWorkshop.ViewModels;

/// <summary>What happened to one repository during a project-wide action.</summary>
public enum RepoOpOutcome {
    /// <summary>The action succeeded.</summary>
    Succeeded,

    /// <summary>The action ran and failed.</summary>
    Failed,

    /// <summary>The action did not apply to this repository.</summary>
    Skipped,
}
