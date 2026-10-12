namespace RepoWorkshop.Models;

/// <summary>A reference to a git working tree on disk.</summary>
public sealed class RepoModel
{
    /// <summary>Absolute path to the repository's top-level working directory.</summary>
    public string Path { get; set; } = "";

    /// <summary>Optional display name; the folder name is used when empty.</summary>
    public string? Alias { get; set; }
}
