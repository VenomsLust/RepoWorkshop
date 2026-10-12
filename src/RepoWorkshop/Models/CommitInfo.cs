using System;
using System.Collections.Generic;

namespace RepoWorkshop.Models;

/// <summary>A commit in the history.</summary>
/// <param name="Hash">Full commit id.</param>
/// <param name="ShortHash">Abbreviated commit id.</param>
/// <param name="Author">Author name.</param>
/// <param name="Date">Author date.</param>
/// <param name="Subject">First line of the message.</param>
/// <param name="Parents">Parent commit ids; empty for a root commit.</param>
public sealed record CommitInfo(
    string Hash,
    string ShortHash,
    string Author,
    DateTimeOffset Date,
    string Subject,
    IReadOnlyList<string> Parents) {
    /// <summary>The date, for display.</summary>
    public string DateText => Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    /// <summary>The first parent; null for a root commit.</summary>
    public string? FirstParent => Parents.Count > 0 ? Parents[0] : null;

    /// <summary>Whether this is a merge commit.</summary>
    public bool IsMerge => Parents.Count > 1;
}
