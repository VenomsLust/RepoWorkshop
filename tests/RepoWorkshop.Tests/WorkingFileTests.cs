using System.Collections.ObjectModel;
using RepoWorkshop.Models;
using RepoWorkshop.Services;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Tests;

/// <summary>Building the changes panel's rows, and updating lists in place.</summary>
public class WorkingFileTests {
    /// <summary>Staged and unstaged halves merge into one row per path, with the right kind, code and flags.</summary>
    [Fact]
    public void MergesStagedAndUnstagedIntoOneRowPerFile() {
        RepoStatus status = GitService.ParseStatus(string.Join('\0',
            "# branch.head main",
            "1 .M N... 100644 100644 100644 a b src/only-unstaged.cs",
            "1 M. N... 100644 100644 100644 a b src/only-staged.cs",
            "1 MM N... 100644 100644 100644 a b src/both.cs",
            "1 AM N... 000000 100644 100644 0 b src/added-then-edited.cs",
            "2 R. N... 100644 100644 100644 a a R100 src/new name.cs",
            "src/old name.cs",
            "? notes.txt",
            ""));

        List<WorkingFile> files = ChangesViewModel.BuildWorkingFiles(status);

        Assert.Equal(["notes.txt", "src/added-then-edited.cs", "src/both.cs", "src/new name.cs", "src/only-staged.cs", "src/only-unstaged.cs"],
            files.Select(f => f.Path));
        WorkingFile Get(string path) => files.Single(f => f.Path == path);

        Assert.Equal((" M", false, true), (Get("src/only-unstaged.cs").Code, Get("src/only-unstaged.cs").IsStaged, Get("src/only-unstaged.cs").HasUnstaged));
        Assert.Equal(("M ", true, false), (Get("src/only-staged.cs").Code, Get("src/only-staged.cs").IsStaged, Get("src/only-staged.cs").HasUnstaged));
        Assert.True(Get("src/both.cs").IsPartiallyStaged);
        Assert.Equal(ChangeKind.Added, Get("src/added-then-edited.cs").Kind);
        Assert.Equal("src/old name.cs", Get("src/new name.cs").OriginalPath);
        Assert.Equal(("??", ChangeKind.Untracked, false), (Get("notes.txt").Code, Get("notes.txt").Kind, Get("notes.txt").IsStaged));
    }

    /// <summary>Unchanged items keep their identity (so list selections survive a refresh).</summary>
    [Fact]
    public void SyncKeepsUnchangedItemsAndMatchesOrder() {
        WorkingFile a = new("a.txt", null, ChangeKind.Modified, " M", false, true);
        WorkingFile b = new("b.txt", null, ChangeKind.Modified, " M", false, true);
        WorkingFile c = new("c.txt", null, ChangeKind.Modified, " M", false, true);
        ObservableCollection<WorkingFile> list = [a, b, c];

        WorkingFile bStaged = b with { Code = "M ", IsStaged = true, HasUnstaged = false };
        WorkingFile d = new("d.txt", null, ChangeKind.Untracked, "??", false, true);
        RepoViewModel.Sync(list, [a with { }, bStaged, d]);

        Assert.Equal([a, bStaged, d], list);
        Assert.Same(a, list[0]);
    }
}
