namespace RepoWorkshop.Models;

/// <summary>How a file changed.</summary>
public enum ChangeKind {
    /// <summary>Contents changed.</summary>
    Modified,

    /// <summary>New file.</summary>
    Added,

    /// <summary>File removed.</summary>
    Deleted,

    /// <summary>Moved or renamed.</summary>
    Renamed,

    /// <summary>Copied from another file.</summary>
    Copied,

    /// <summary>File type changed (e.g. file to symlink).</summary>
    TypeChanged,

    /// <summary>Merge conflict.</summary>
    Unmerged,

    /// <summary>Not tracked by git.</summary>
    Untracked,
}
