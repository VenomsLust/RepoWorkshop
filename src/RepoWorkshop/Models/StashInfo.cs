using System;

namespace RepoWorkshop.Models;

/// <summary>An entry in the stash list.</summary>
/// <param name="Ref">The stash ref, e.g. stash@{0}.</param>
/// <param name="Message">The stash description.</param>
/// <param name="Date">When the stash was created.</param>
public sealed record StashInfo(string Ref, string Message, DateTimeOffset Date) {
    /// <summary>The date, for display.</summary>
    public string DateText => Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}
