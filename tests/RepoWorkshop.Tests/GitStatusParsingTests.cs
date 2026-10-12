using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.Tests;

/// <summary>Parsing of <c>git status --porcelain=v2</c> output.</summary>
public class GitStatusParsingTests {
    /// <summary>Branch headers, and each entry kind split into its staged and unstaged halves.</summary>
    [Fact]
    public void ParsesBranchHeadersAndEntries() {
        string output = string.Join('\0',
            "# branch.oid 1234567890abcdef",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -3",
            "1 .M N... 100644 100644 100644 aaa bbb src/file with space.cs",
            "1 A. N... 000000 100644 100644 000 ccc added.txt",
            "1 MM N... 100644 100644 100644 aaa bbb both.txt",
            "2 R. N... 100644 100644 100644 ddd ddd R100 new name.txt",
            "old name.txt",
            "u UU N... 100644 100644 100644 100644 e1 e2 e3 conflict.txt",
            "? untracked/file.txt",
            "");

        RepoStatus status = GitService.ParseStatus(output);

        Assert.Equal("feature/x", status.Branch);
        Assert.False(status.IsDetached);
        Assert.Equal("origin/feature/x", status.Upstream);
        Assert.Equal(2, status.Ahead);
        Assert.Equal(3, status.Behind);
        Assert.Collection(status.Staged,
            c => { Assert.Equal("added.txt", c.Path); Assert.Equal(ChangeKind.Added, c.Kind); },
            c => { Assert.Equal("both.txt", c.Path); Assert.Equal(ChangeKind.Modified, c.Kind); },
            c => { Assert.Equal("new name.txt", c.Path); Assert.Equal("old name.txt", c.OriginalPath); Assert.Equal(ChangeKind.Renamed, c.Kind); });
        Assert.Collection(status.Unstaged,
            c => { Assert.Equal("src/file with space.cs", c.Path); Assert.Equal(ChangeKind.Modified, c.Kind); },
            c => { Assert.Equal("both.txt", c.Path); Assert.Equal(ChangeKind.Modified, c.Kind); },
            c => { Assert.Equal("conflict.txt", c.Path); Assert.Equal(ChangeKind.Unmerged, c.Kind); },
            c => { Assert.Equal("untracked/file.txt", c.Path); Assert.Equal(ChangeKind.Untracked, c.Kind); });
        Assert.Equal(6, status.ChangedPathCount);
    }

    /// <summary>A detached HEAD has no branch but keeps its commit.</summary>
    [Fact]
    public void ParsesDetachedHead() {
        RepoStatus status = GitService.ParseStatus("# branch.oid abcdef1234\0# branch.head (detached)\0");
        Assert.Null(status.Branch);
        Assert.True(status.IsDetached);
        Assert.Equal("abcdef1234", status.HeadCommit);
    }
}
