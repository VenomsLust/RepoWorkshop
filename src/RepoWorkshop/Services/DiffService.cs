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

    /// <summary>Compares the HEAD version of a changed file with the (editable) working copy.</summary>
    public async Task DiffWorkingChangeAsync(string repoPath, FileChange change, bool hasHead)
    {
        var temp = NewTempFolder();
        var fileName = Path.GetFileName(ToLocal(change.Path));

        string left;
        if (!hasHead || change.Kind is ChangeKind.Untracked or ChangeKind.Added)
        {
            left = Path.Combine(temp, "empty", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(left)!);
            await File.WriteAllBytesAsync(left, []);
        }
        else
        {
            left = Path.Combine(temp, "HEAD", fileName);
            await git.ExtractFileAsync(repoPath, "HEAD", change.OriginalPath ?? change.Path, left);
        }

        var right = Path.Combine(repoPath, ToLocal(change.Path));
        var rightReadOnly = false;
        if (!File.Exists(right))
        {
            right = Path.Combine(temp, "deleted", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(right)!);
            await File.WriteAllBytesAsync(right, []);
            rightReadOnly = true;
        }

        winMerge.CompareFiles(left, right, $"HEAD: {change.OriginalPath ?? change.Path}", $"Working tree: {change.Path}",
            leftReadOnly: true, rightReadOnly);
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
