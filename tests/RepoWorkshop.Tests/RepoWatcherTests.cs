using RepoWorkshop.Services;

namespace RepoWorkshop.Tests;

/// <summary>The file watcher: which paths matter, and that it fires once changes settle.</summary>
public sealed class RepoWatcherTests : IDisposable {
    /// <summary>A temporary folder to watch.</summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RepoWorkshopTests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the folder (with a fake .git directory).</summary>
    public RepoWatcherTests() {
        Directory.CreateDirectory(Path.Combine(_folder, ".git", "objects"));
    }

    /// <summary>Deletes the folder.</summary>
    public void Dispose() {
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Working-tree files matter; inside .git only HEAD, the index and refs do.</summary>
    [Theory]
    [InlineData("src/Login.cs", true)]
    [InlineData("README.md", true)]
    [InlineData(".gitignore", true)]
    [InlineData(".git/HEAD", true)]
    [InlineData(".git/index", true)]
    [InlineData(".git\\refs\\heads\\main", true)]
    [InlineData(".git/packed-refs", true)]
    [InlineData(".git/index.lock", false)]
    [InlineData(".git/objects/ab/cdef", false)]
    [InlineData(".git/logs/HEAD", false)]
    [InlineData(".git/FETCH_HEAD", false)]
    public void KnowsWhichPathsAreRelevant(string path, bool relevant) {
        Assert.Equal(relevant, RepoWatcher.IsRelevant(path));
    }

    /// <summary>A burst of file changes raises one event; object-store churn raises none.</summary>
    [Fact]
    public async Task RaisesOnceAfterABurstAndIgnoresGitObjects() {
        int raised = 0;
        using RepoWatcher watcher = new(_folder);
        watcher.Changed += () => Interlocked.Increment(ref raised);

        await File.WriteAllTextAsync(Path.Combine(_folder, ".git", "objects", "blob"), "x");
        await Task.Delay(1000);
        Assert.Equal(0, raised);

        for (int i = 0; i < 5; i++) {
            await File.WriteAllTextAsync(Path.Combine(_folder, $"file{i}.txt"), "x");
        }
        await Task.Delay(1500);
        Assert.Equal(1, raised);
    }
}
