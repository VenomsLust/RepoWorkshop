namespace RepoWorkshop.Services;

/// <summary>
/// One side of a file comparison: a file at a revision, in the index, in the working tree, or nothing.
/// </summary>
/// <param name="Title">Caption shown above this side in WinMerge.</param>
/// <param name="Revision">The revision to read from; "" for the index, null for the working tree.</param>
/// <param name="Path">Repository-relative path; null for an empty side.</param>
public sealed record DiffSide(string Title, string? Revision, string? Path) {
    /// <summary>A side with no file, e.g. before a file was added.</summary>
    public static DiffSide Empty(string title) => new(title, null, null);

    /// <summary>The live file in the working tree.</summary>
    public static DiffSide Working(string path) => new($"Working tree: {path}", null, path);

    /// <summary>The staged version (git's ":path" syntax uses an empty revision for the index).</summary>
    public static DiffSide Index(string path) => new($"Staged: {path}", "", path);

    /// <summary>The file as of a revision, captioned with <paramref name="label"/>.</summary>
    public static DiffSide At(string revision, string label, string path) => new($"{label}: {path}", revision, path);
}
