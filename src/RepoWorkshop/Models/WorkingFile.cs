namespace RepoWorkshop.Models;

/// <summary>
/// A changed file in the working tree, combining its staged and unstaged state into one row.
/// </summary>
/// <param name="Path">Repository-relative path, with forward slashes.</param>
/// <param name="OriginalPath">The previous path, for a staged rename.</param>
/// <param name="Kind">The most significant change: Added, Deleted or Renamed when staged, otherwise the working-tree change.</param>
/// <param name="Code">Two-character git status, index then working tree, e.g. "M ", " M", "MM", "??".</param>
/// <param name="IsStaged">Whether any part of the change is staged.</param>
/// <param name="HasUnstaged">Whether there are changes not yet staged.</param>
public sealed record WorkingFile(
    string Path,
    string? OriginalPath,
    ChangeKind Kind,
    string Code,
    bool IsStaged,
    bool HasUnstaged) {
    /// <summary>The path, showing "old → new" for renames.</summary>
    public string DisplayPath => OriginalPath is null ? Path : $"{OriginalPath} → {Path}";

    /// <summary>Whether the file has an unresolved merge conflict.</summary>
    public bool IsConflict => Kind == ChangeKind.Unmerged;

    /// <summary>Whether part of the file is staged and part is not.</summary>
    public bool IsPartiallyStaged => IsStaged && HasUnstaged;

    /// <summary>A tooltip describing the state.</summary>
    public string StateText => Kind switch {
        ChangeKind.Untracked => "Untracked",
        ChangeKind.Unmerged => "Merge conflict",
        _ when IsPartiallyStaged => $"{Kind}, partially staged",
        _ when IsStaged => $"{Kind}, staged",
        _ => Kind.ToString(),
    };
}
