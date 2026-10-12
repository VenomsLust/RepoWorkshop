namespace RepoWorkshop.ViewModels;

/// <summary>The outcome of a project-wide action for one repository.</summary>
/// <param name="Repo">The repository's display name.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">git's output, or why it was skipped.</param>
public sealed record RepoOpResult(string Repo, RepoOpOutcome Outcome, string? Message);
