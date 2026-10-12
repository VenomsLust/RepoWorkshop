using RepoWorkshop.Models;
using RepoWorkshop.Services;

namespace RepoWorkshop.Tests;

public class GitStatusParsingTests
{
    [Fact]
    public void ParsesBranchHeadersAndEntries()
    {
        var output = string.Join('\0',
            "# branch.oid 1234567890abcdef",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -3",
            "1 .M N... 100644 100644 100644 aaa bbb src/file with space.cs",
            "1 A. N... 000000 100644 100644 000 ccc added.txt",
            "2 R. N... 100644 100644 100644 ddd ddd R100 new name.txt",
            "old name.txt",
            "u UU N... 100644 100644 100644 100644 e1 e2 e3 conflict.txt",
            "? untracked/file.txt",
            "");

        var status = GitService.ParseStatus(output);

        Assert.Equal("feature/x", status.Branch);
        Assert.False(status.IsDetached);
        Assert.Equal("origin/feature/x", status.Upstream);
        Assert.Equal(2, status.Ahead);
        Assert.Equal(3, status.Behind);
        Assert.Collection(status.Changes,
            c => { Assert.Equal("src/file with space.cs", c.Path); Assert.Equal(ChangeKind.Modified, c.Kind); },
            c => { Assert.Equal("added.txt", c.Path); Assert.Equal(ChangeKind.Added, c.Kind); },
            c => { Assert.Equal("new name.txt", c.Path); Assert.Equal("old name.txt", c.OriginalPath); Assert.Equal(ChangeKind.Renamed, c.Kind); },
            c => { Assert.Equal("conflict.txt", c.Path); Assert.Equal(ChangeKind.Unmerged, c.Kind); },
            c => { Assert.Equal("untracked/file.txt", c.Path); Assert.Equal(ChangeKind.Untracked, c.Kind); });
    }

    [Fact]
    public void ParsesDetachedHead()
    {
        var status = GitService.ParseStatus("# branch.oid abcdef1234\0# branch.head (detached)\0");
        Assert.Null(status.Branch);
        Assert.True(status.IsDetached);
        Assert.Equal("abcdef1234", status.HeadCommit);
    }
}

/// <summary>Runs GitService against a real throwaway repository.</summary>
public sealed class GitServiceIntegrationTests : IAsyncLifetime
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "RepoWorkshopTests", Guid.NewGuid().ToString("N"));
    private readonly GitService _git = new(new LogService());

    private Task<GitResult> Git(params string[] args) =>
        _git.RunAsync(_repo, ["-c", "user.name=Test", "-c", "user.email=test@example.com", .. args], CancellationToken.None);

    public async Task InitializeAsync()
    {
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

    public Task DisposeAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_repo, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ListsBranchesWithHead()
    {
        var branches = await _git.GetBranchesAsync(_repo);
        Assert.Equal(["feature", "main"], branches.Select(b => b.Name).Order());
        Assert.True(branches.Single(b => b.Name == "feature").IsHead);
        Assert.Equal("feature work", branches.Single(b => b.Name == "feature").Subject);
    }

    [Fact]
    public async Task ReportsChangesBetweenBranches()
    {
        var changes = await _git.GetChangesBetweenAsync(_repo, "main", "feature");
        Assert.Equal(ChangeKind.Modified, changes.Single(c => c.Path == "a.txt").Kind);
        Assert.Equal(ChangeKind.Deleted, changes.Single(c => c.Path == "b.txt").Kind);
        Assert.Equal(ChangeKind.Added, changes.Single(c => c.Path == "c.txt").Kind);
    }

    [Fact]
    public async Task ExtractsFileAtRevision()
    {
        var output = Path.Combine(_repo + "-out", "a.txt");
        try
        {
            await _git.ExtractFileAsync(_repo, "main", "a.txt", output);
            Assert.Equal("one", (await File.ReadAllTextAsync(output)).Trim());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(output)!, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsWorkingTreeStatus()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "new file.txt"), "x");
        await File.AppendAllTextAsync(Path.Combine(_repo, "a.txt"), "three\n");

        var status = await _git.GetStatusAsync(_repo);

        Assert.Equal("feature", status.Branch);
        Assert.Contains(status.Changes, c => c.Path == "new file.txt" && c.Kind == ChangeKind.Untracked);
        Assert.Contains(status.Changes, c => c.Path == "a.txt" && c.Kind == ChangeKind.Modified);
    }

    [Fact]
    public async Task CreatesSwitchesAndDeletesBranches()
    {
        Assert.True((await _git.CreateBranchAsync(_repo, "topic", "main", checkout: true)).Success);
        Assert.Equal("topic", (await _git.GetStatusAsync(_repo)).Branch);
        Assert.True((await _git.SwitchAsync(_repo, "feature")).Success);
        Assert.True((await _git.DeleteBranchAsync(_repo, "topic", force: false)).Success);
        Assert.False(await _git.LocalBranchExistsAsync(_repo, "topic"));
        var mainCommit = (await _git.RunAsync(_repo, "rev-parse", "main")).StdOut.Trim();
        Assert.Equal(mainCommit, await _git.GetMergeBaseAsync(_repo, "main", "feature"));
    }
}
