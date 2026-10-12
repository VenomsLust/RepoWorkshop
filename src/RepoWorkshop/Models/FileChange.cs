namespace RepoWorkshop.Models;

/// <summary>A changed path, either in the working tree or between two revisions.</summary>
/// <param name="Path">Repository-relative path, with forward slashes.</param>
/// <param name="OriginalPath">The previous path, for renames and copies.</param>
/// <param name="Kind">How the file changed.</param>
/// <param name="Code">git's short status code, e.g. M or ??.</param>
public sealed record FileChange(string Path, string? OriginalPath, ChangeKind Kind, string Code) {
    /// <summary>The path, showing "old → new" for renames.</summary>
    public string DisplayPath => OriginalPath is null ? Path : $"{OriginalPath} → {Path}";

    /// <summary>Maps a git status letter to a <see cref="ChangeKind"/>.</summary>
    public static ChangeKind KindFromCode(char c) => c switch {
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'T' => ChangeKind.TypeChanged,
        'U' => ChangeKind.Unmerged,
        '?' => ChangeKind.Untracked,
        _ => ChangeKind.Modified,
    };
}
