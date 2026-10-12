using System.Collections.Generic;
using System.Linq;

namespace RepoWorkshop.Models;

/// <summary>Snapshot of <c>git status</c> for a working tree.</summary>
/// <param name="Branch">The checked-out branch; null when detached.</param>
/// <param name="IsDetached">Whether HEAD is detached.</param>
/// <param name="HeadCommit">The HEAD commit; null in a repository with no commits.</param>
/// <param name="Upstream">The branch's upstream, if any.</param>
/// <param name="Ahead">Commits not yet on the upstream.</param>
/// <param name="Behind">Upstream commits not yet on the branch.</param>
/// <param name="Staged">Changes in the index.</param>
/// <param name="Unstaged">Changes in the working tree, including untracked and conflicted files.</param>
public sealed record RepoStatus(
    string? Branch,
    bool IsDetached,
    string? HeadCommit,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Staged,
    IReadOnlyList<FileChange> Unstaged) {
    /// <summary>Number of distinct paths with staged or unstaged changes.</summary>
    public int ChangedPathCount => Staged.Select(c => c.Path).Union(Unstaged.Select(c => c.Path)).Count();
}
