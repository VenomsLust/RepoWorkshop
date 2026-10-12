using System;
using System.Collections.Generic;
using System.Linq;

namespace RepoWorkshop.Models;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    /// <summary>The most useful message to show the user for this result.</summary>
    public string Message => (string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr).Trim();
}

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
    string Subject)
{
    /// <summary>For remote branches, the name without the remote prefix (origin/foo -> foo).</summary>
    public string ShortName => IsRemote && Name.IndexOf('/') is var i and > 0 ? Name[(i + 1)..] : Name;

    public string TrackingText =>
        Upstream is null ? "" :
        UpstreamGone ? $"{Upstream} (gone)" :
        (Ahead, Behind) switch
        {
            (0, 0) => Upstream,
            _ => $"{Upstream}  {(Ahead > 0 ? $"↑{Ahead} " : "")}{(Behind > 0 ? $"↓{Behind}" : "")}".TrimEnd(),
        };

    public string DateText => CommitDate?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "";
}

public enum ChangeKind
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Unmerged,
    Untracked,
}

/// <summary>A changed path, either in the working tree or between two revisions.</summary>
public sealed record FileChange(string Path, string? OriginalPath, ChangeKind Kind, string Code)
{
    public string DisplayPath => OriginalPath is null ? Path : $"{OriginalPath} → {Path}";

    public static ChangeKind KindFromCode(char c) => c switch
    {
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

/// <summary>Snapshot of <c>git status</c> for a working tree.</summary>
public sealed record RepoStatus(
    string? Branch,
    bool IsDetached,
    string? HeadCommit,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Staged,
    IReadOnlyList<FileChange> Unstaged)
{
    /// <summary>Number of distinct paths with staged or unstaged changes.</summary>
    public int ChangedPathCount => Staged.Select(c => c.Path).Union(Unstaged.Select(c => c.Path)).Count();
}

public sealed record CommitInfo(
    string Hash,
    string ShortHash,
    string Author,
    DateTimeOffset Date,
    string Subject,
    IReadOnlyList<string> Parents)
{
    public string DateText => Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string? FirstParent => Parents.Count > 0 ? Parents[0] : null;

    public bool IsMerge => Parents.Count > 1;
}

public sealed record StashInfo(string Ref, string Message, DateTimeOffset Date)
{
    public string DateText => Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}
