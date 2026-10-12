using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RepoWorkshop.Models;

namespace RepoWorkshop.Services;

/// <summary>
/// Prepares the inputs for WinMerge: extracts historical file versions from git into a
/// temporary folder and launches the comparison.
/// </summary>
public sealed class DiffService(GitService git, WinMergeService winMerge)
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "RepoWorkshop");

    /// <summary>Removes temporary comparison folders left over from earlier sessions.</summary>
    public static void CleanupOldTempFiles()
    {
        if (!Directory.Exists(TempRoot))
            return;
        foreach (var dir in Directory.EnumerateDirectories(TempRoot))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                    Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string NewTempFolder()
    {
        var dir = Path.Combine(TempRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ToLocal(string repoRelativePath) => repoRelativePath.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Compares two versions of a single file. Only a working-tree file on the right is editable.</summary>
    public async Task DiffFileAsync(string repoPath, DiffSide left, DiffSide right)
    {
        var temp = NewTempFolder();
        var (leftPath, _) = await MaterializeAsync(repoPath, left, Path.Combine(temp, "left"));
        var (rightPath, rightEditable) = await MaterializeAsync(repoPath, right, Path.Combine(temp, "right"));
        winMerge.CompareFiles(leftPath, rightPath, left.Title, right.Title, leftReadOnly: true, rightReadOnly: !rightEditable);
    }

    /// <summary>Returns a file on disk holding the side's content, and whether it is the live working-tree file.</summary>
    private async Task<(string Path, bool IsWorkingFile)> MaterializeAsync(string repoPath, DiffSide side, string tempDir)
    {
        var fileName = side.Path is null ? "empty" : Path.GetFileName(ToLocal(side.Path));
        var tempFile = Path.Combine(tempDir, fileName);

        if (side.Path is not null && side.Revision is null)
        {
            var working = Path.Combine(repoPath, ToLocal(side.Path));
            if (File.Exists(working))
                return (working, true);
        }
        else if (side.Path is not null)
        {
            await git.ExtractFileAsync(repoPath, side.Revision!, side.Path, tempFile);
            return (tempFile, false);
        }

        // Empty side: a file that does not exist on this side of the comparison.
        Directory.CreateDirectory(tempDir);
        await File.WriteAllBytesAsync(tempFile, []);
        return (tempFile, false);
    }

    /// <summary>
    /// Opens a folder comparison of every file that differs between two revisions.
    /// Returns the number of differing files (WinMerge is not opened when there are none).
    /// </summary>
    public async Task<int> CompareRevisionsAsync(string repoPath, string leftRev, string leftTitle, string rightRev, string rightTitle)
    {
        var changes = await git.GetChangesBetweenAsync(repoPath, leftRev, rightRev);
        if (changes.Count == 0)
            return 0;

        var temp = NewTempFolder();
        var leftDir = Path.Combine(temp, "left");
        var rightDir = Path.Combine(temp, "right");
        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);

        // Extract in parallel; each extraction is a separate git process.
        using var throttle = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount, 2, 8));
        var jobs = new List<(string Rev, string Path, string Dir)>();
        foreach (var c in changes)
        {
            if (c.Kind != ChangeKind.Added)
                jobs.Add((leftRev, c.OriginalPath ?? c.Path, leftDir));
            if (c.Kind != ChangeKind.Deleted)
                jobs.Add((rightRev, c.Path, rightDir));
        }

        await Task.WhenAll(jobs.Select(async job =>
        {
            await throttle.WaitAsync();
            try
            {
                await git.ExtractFileAsync(repoPath, job.Rev, job.Path, Path.Combine(job.Dir, ToLocal(job.Path)));
            }
            finally
            {
                throttle.Release();
            }
        }));

        winMerge.CompareFolders(leftDir, rightDir, leftTitle, rightTitle);
        return changes.Count;
    }
}

/// <summary>
/// One side of a file comparison: a file at a revision, in the index, in the working tree, or nothing.
/// </summary>
public sealed record DiffSide(string Title, string? Revision, string? Path)
{
    public static DiffSide Empty(string title) => new(title, null, null);

    public static DiffSide Working(string path) => new($"Working tree: {path}", null, path);

    /// <summary>The staged version (git's ":path" syntax uses an empty revision for the index).</summary>
    public static DiffSide Index(string path) => new($"Staged: {path}", "", path);

    public static DiffSide At(string revision, string label, string path) => new($"{label}: {path}", revision, path);
}
