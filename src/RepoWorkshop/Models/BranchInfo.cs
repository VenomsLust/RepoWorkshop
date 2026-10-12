using System;

namespace RepoWorkshop.Models;

/// <summary>A local or remote branch.</summary>
/// <param name="Name">Short name, e.g. main or origin/main.</param>
/// <param name="FullRef">Full ref, e.g. refs/heads/main.</param>
/// <param name="IsRemote">Whether this is a remote-tracking branch.</param>
/// <param name="IsHead">Whether this is the checked-out branch.</param>
/// <param name="Upstream">The branch it tracks, if any.</param>
/// <param name="Ahead">Commits not yet on the upstream.</param>
/// <param name="Behind">Upstream commits not yet on this branch.</param>
/// <param name="UpstreamGone">The upstream was deleted on the remote.</param>
/// <param name="CommitId">Abbreviated id of the tip commit.</param>
/// <param name="CommitDate">Committer date of the tip commit.</param>
/// <param name="Subject">Subject line of the tip commit.</param>
public sealed record BranchInfo(
    string Name,
    string FullRef,
    bool IsRemote,
    bool IsHead,
    string? Upstream,
    int Ahead,
    int Behind,
    bool UpstreamGone,
    string CommitId,
    DateTimeOffset? CommitDate,
    string Subject) {
    /// <summary>For remote branches, the name without the remote prefix (origin/foo -> foo).</summary>
    public string ShortName => IsRemote && Name.IndexOf('/') is int i and > 0 ? Name[(i + 1)..] : Name;

    /// <summary>The upstream with ahead/behind arrows, for display.</summary>
    public string TrackingText =>
        Upstream is null ? "" :
        UpstreamGone ? $"{Upstream} (gone)" :
        (Ahead, Behind) switch {
            (0, 0) => Upstream,
            _ => $"{Upstream}  {(Ahead > 0 ? $"↑{Ahead} " : "")}{(Behind > 0 ? $"↓{Behind}" : "")}".TrimEnd(),
        };

    /// <summary>The tip commit's date, for display.</summary>
    public string DateText => CommitDate?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "";
}
