using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.Tests;

/// <summary>
/// Runs <see cref="GitService"/> against a real throwaway repository: main has a.txt and b.txt;
/// feature (checked out) modifies a.txt, deletes b.txt and adds c.txt.
/// </summary>
public sealed class GitServiceIntegrationTests : IAsyncLifetime {
    /// <summary>The temporary repository.</summary>
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "RepoWorkshopTests", Guid.NewGuid().ToString("N"));

    /// <summary>The service under test.</summary>
    private readonly GitService _git = new(new LogService());

    /// <summary>Creates the repository with its two branches.</summary>
    public async Task InitializeAsync() {
        Directory.CreateDirectory(_repo);
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "one\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "b.txt"), "bee\n");
        await Git("add", ".");
        await Git("commit", "-m", "first");
        await Git("switch", "-c", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "one\ntwo\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "c.txt"), "sea\n");
        await Git("rm", "-q", "b.txt");
        await Git("add", ".");
        await Git("commit", "-m", "feature work");
    }

    /// <summary>Deletes the repository (git marks some files read-only).</summary>
    public Task DisposeAsync() {
        foreach (string file in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories)) {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_repo, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>Both branches are listed, with the checked-out one marked.</summary>
    [Fact]
    public async Task ListsBranchesWithHead() {
        IReadOnlyList<BranchInfo> branches = await _git.GetBranchesAsync(_repo);
        Assert.Equal(["feature", "main"], branches.Select(b => b.Name).Order());
        Assert.True(branches.Single(b => b.Name == "feature").IsHead);
        Assert.Equal("feature work", branches.Single(b => b.Name == "feature").Subject);
    }

    /// <summary>Modified, deleted and added files between two branches.</summary>
    [Fact]
    public async Task ReportsChangesBetweenBranches() {
        IReadOnlyList<FileChange> changes = await _git.GetChangesBetweenAsync(_repo, "main", "feature");
        Assert.Equal(ChangeKind.Modified, changes.Single(c => c.Path == "a.txt").Kind);
        Assert.Equal(ChangeKind.Deleted, changes.Single(c => c.Path == "b.txt").Kind);
        Assert.Equal(ChangeKind.Added, changes.Single(c => c.Path == "c.txt").Kind);
    }

    /// <summary>A file's contents at another branch.</summary>
    [Fact]
    public async Task ExtractsFileAtRevision() {
        string output = Path.Combine(_repo + "-out", "a.txt");
        try {
            await _git.ExtractFileAsync(_repo, "main", "a.txt", output);
            Assert.Equal("one", (await File.ReadAllTextAsync(output)).Trim());
        } finally {
            Directory.Delete(Path.GetDirectoryName(output)!, recursive: true);
        }
    }

    /// <summary>Untracked and modified files appear as unstaged.</summary>
    [Fact]
    public async Task ReportsWorkingTreeStatus() {
        await File.WriteAllTextAsync(Path.Combine(_repo, "new file.txt"), "x");
        await File.AppendAllTextAsync(Path.Combine(_repo, "a.txt"), "three\n");

        RepoStatus status = await _git.GetStatusAsync(_repo);

        Assert.Equal("feature", status.Branch);
        Assert.Contains(status.Unstaged, c => c.Path == "new file.txt" && c.Kind == ChangeKind.Untracked);
        Assert.Contains(status.Unstaged, c => c.Path == "a.txt" && c.Kind == ChangeKind.Modified);
        Assert.Empty(status.Staged);
    }

    /// <summary>Branch create, switch and delete; and the merge base of the two branches.</summary>
    [Fact]
    public async Task CreatesSwitchesAndDeletesBranches() {
        Assert.True((await _git.CreateBranchAsync(_repo, "topic", "main", checkout: true)).Success);
        Assert.Equal("topic", (await _git.GetStatusAsync(_repo)).Branch);
        Assert.True((await _git.SwitchAsync(_repo, "feature")).Success);
        Assert.True((await _git.DeleteBranchAsync(_repo, "topic", force: false)).Success);
        Assert.False(await _git.LocalBranchExistsAsync(_repo, "topic"));

        string mainCommit = (await _git.RunAsync(_repo, "rev-parse", "main")).StdOut.Trim();
        Assert.Equal(mainCommit, await _git.GetMergeBaseAsync(_repo, "main", "feature"));
    }

    /// <summary>Stage, unstage, stage all, commit; and reading back a multi-line message.</summary>
    [Fact]
    public async Task StagesUnstagesAndCommits() {
        await File.WriteAllTextAsync(Path.Combine(_repo, "new file.txt"), "x");
        await File.AppendAllTextAsync(Path.Combine(_repo, "a.txt"), "three\n");

        Assert.True((await _git.StageAsync(_repo, ["new file.txt"])).Success);
        RepoStatus status = await _git.GetStatusAsync(_repo);
        Assert.Contains(status.Staged, c => c.Path == "new file.txt" && c.Kind == ChangeKind.Added);
        Assert.Contains(status.Unstaged, c => c.Path == "a.txt");

        Assert.True((await _git.UnstageAsync(_repo, ["new file.txt"], hasHead: true)).Success);
        Assert.Empty((await _git.GetStatusAsync(_repo)).Staged);

        Assert.True((await _git.StageAllAsync(_repo)).Success);
        Assert.True((await Git("commit", "-m", "Multi\n\nline message")).Success);
        status = await _git.GetStatusAsync(_repo);
        Assert.Empty(status.Staged);
        Assert.Empty(status.Unstaged);
        Assert.Equal("Multi\n\nline message", await _git.GetHeadMessageAsync(_repo));
    }

    /// <summary>Restoring to HEAD discards both staged and unstaged changes, and brings back a deleted file.</summary>
    [Fact]
    public async Task RestoresFilesToHead() {
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "staged edit\n");
        await _git.StageAsync(_repo, ["a.txt"]);
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "more edits\n");
        File.Delete(Path.Combine(_repo, "c.txt"));

        Assert.True((await _git.RestoreToHeadAsync(_repo, ["a.txt", "c.txt"])).Success);

        RepoStatus status = await _git.GetStatusAsync(_repo);
        Assert.Empty(status.Staged);
        Assert.Empty(status.Unstaged);
        Assert.Equal("one\ntwo", (await File.ReadAllTextAsync(Path.Combine(_repo, "a.txt"))).Trim().ReplaceLineEndings("\n"));
        Assert.True(File.Exists(Path.Combine(_repo, "c.txt")));
    }

    /// <summary>Log order and parents; a root commit's files come from the empty tree.</summary>
    [Fact]
    public async Task ReadsLogWithParents() {
        IReadOnlyList<CommitInfo> log = await _git.GetLogAsync(_repo, "feature", 10);
        Assert.Equal(["feature work", "first"], log.Select(c => c.Subject));
        Assert.Equal(log[1].Hash, log[0].FirstParent);
        Assert.Null(log[1].FirstParent);

        IReadOnlyList<FileChange> rootFiles = await _git.GetChangesBetweenAsync(_repo, GitService.EmptyTree, log[1].Hash);
        Assert.Equal(["a.txt", "b.txt"], rootFiles.Select(f => f.Path).Order());
    }

    /// <summary>The empty revision reads the staged version, not the working copy.</summary>
    [Fact]
    public async Task ExtractsStagedVersionFromIndex() {
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "staged\n");
        await _git.StageAsync(_repo, ["a.txt"]);
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "working\n");

        string output = Path.Combine(_repo + "-out", "a.txt");
        try {
            await _git.ExtractFileAsync(_repo, "", "a.txt", output);
            Assert.Equal("staged", (await File.ReadAllTextAsync(output)).Trim());
        } finally {
            Directory.Delete(Path.GetDirectoryName(output)!, recursive: true);
        }
    }

    /// <summary>Stash (with untracked files), list, apply and drop.</summary>
    [Fact]
    public async Task PushesAppliesAndDropsStashes() {
        await File.AppendAllTextAsync(Path.Combine(_repo, "a.txt"), "stashed\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "untracked.txt"), "u");

        Assert.True((await Git("stash", "push", "--include-untracked", "-m", "my work")).Success);
        StashInfo stash = Assert.Single(await _git.GetStashesAsync(_repo));
        Assert.Equal("stash@{0}", stash.Ref);
        Assert.Contains("my work", stash.Message);
        Assert.Empty((await _git.GetStatusAsync(_repo)).Unstaged);

        Assert.True((await _git.StashApplyAsync(_repo, stash.Ref)).Success);
        Assert.Contains((await _git.GetStatusAsync(_repo)).Unstaged, c => c.Path == "untracked.txt");
        Assert.True((await _git.StashDropAsync(_repo, stash.Ref)).Success);
        Assert.Empty(await _git.GetStashesAsync(_repo));
    }

    /// <summary>Runs git in the test repository with a fixed identity.</summary>
    private Task<GitResult> Git(params string[] args) =>
        _git.RunAsync(_repo, ["-c", "user.name=Test", "-c", "user.email=test@example.com", .. args], CancellationToken.None);
}
