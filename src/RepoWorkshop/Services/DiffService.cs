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
public sealed class DiffService {
    /// <summary>Parent folder for each comparison's temporary files.</summary>
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "RepoWorkshop");

    /// <summary>Extracts file versions.</summary>
    private readonly GitService _git;

    /// <summary>Shows the comparisons.</summary>
    private readonly WinMergeService _winMerge;

    /// <summary>Creates the service.</summary>
    public DiffService(GitService git, WinMergeService winMerge) {
        _git = git;
        _winMerge = winMerge;
    }

    /// <summary>Removes temporary comparison folders left over from earlier sessions.</summary>
    public static void CleanupOldTempFiles() {
        if (!Directory.Exists(TempRoot)) {
            return;
        }

        foreach (string dir in Directory.EnumerateDirectories(TempRoot)) {
            try {
                if (Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddDays(-1)) {
                    Directory.Delete(dir, recursive: true);
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // Still in use (e.g. open in WinMerge); try again next time.
            }
        }
    }

    /// <summary>Compares two versions of a single file. Only a working-tree file on the right is editable.</summary>
    public async Task DiffFileAsync(string repoPath, DiffSide left, DiffSide right) {
        string temp = NewTempFolder();
        (string leftPath, _) = await MaterializeAsync(repoPath, left, Path.Combine(temp, "left"));
        (string rightPath, bool rightEditable) = await MaterializeAsync(repoPath, right, Path.Combine(temp, "right"));
        _winMerge.CompareFiles(leftPath, rightPath, left.Title, right.Title, leftReadOnly: true, rightReadOnly: !rightEditable);
    }

    /// <summary>
    /// Opens a folder comparison of every file that differs between two revisions.
    /// Returns the number of differing files (WinMerge is not opened when there are none).
    /// </summary>
    public async Task<int> CompareRevisionsAsync(string repoPath, string leftRev, string leftTitle, string rightRev, string rightTitle) {
        IReadOnlyList<FileChange> changes = await _git.GetChangesBetweenAsync(repoPath, leftRev, rightRev);
        if (changes.Count == 0) {
            return 0;
        }

        string temp = NewTempFolder();
        string leftDir = Path.Combine(temp, "left");
        string rightDir = Path.Combine(temp, "right");
        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);

        List<(string Rev, string Path, string Dir)> jobs = [];
        foreach (FileChange c in changes) {
            if (c.Kind != ChangeKind.Added) {
                jobs.Add((leftRev, c.OriginalPath ?? c.Path, leftDir));
            }
            if (c.Kind != ChangeKind.Deleted) {
                jobs.Add((rightRev, c.Path, rightDir));
            }
        }

        // Extract in parallel; each extraction is a separate git process.
        using SemaphoreSlim throttle = new(Math.Clamp(Environment.ProcessorCount, 2, 8));
        await Task.WhenAll(jobs.Select(async job => {
            await throttle.WaitAsync();
            try {
                await _git.ExtractFileAsync(repoPath, job.Rev, job.Path, Path.Combine(job.Dir, ToLocal(job.Path)));
            } finally {
                throttle.Release();
            }
        }));

        _winMerge.CompareFolders(leftDir, rightDir, leftTitle, rightTitle);
        return changes.Count;
    }

    /// <summary>
    /// Opens a merge conflict in WinMerge's three-way view: ours (stage 2), the common base (stage 1)
    /// and theirs (stage 3). Saving the middle pane writes the resolved file into the working tree.
    /// A side that doesn't exist (e.g. a file added on both branches has no base) is shown empty.
    /// </summary>
    public async Task ResolveConflictAsync(string repoPath, string path) {
        string temp = NewTempFolder();
        string ours = await ExtractStageOrEmptyAsync(repoPath, 2, path, Path.Combine(temp, "ours"));
        string baseFile = await ExtractStageOrEmptyAsync(repoPath, 1, path, Path.Combine(temp, "base"));
        string theirs = await ExtractStageOrEmptyAsync(repoPath, 3, path, Path.Combine(temp, "theirs"));
        string output = Path.Combine(repoPath, ToLocal(path));
        _winMerge.MergeThreeWay(ours, baseFile, theirs, output, $"Ours: {path}", $"Base (save here): {path}", $"Theirs: {path}");
    }

    /// <summary>Extracts one index stage of a conflicted file, or writes an empty file if that stage doesn't exist.</summary>
    private async Task<string> ExtractStageOrEmptyAsync(string repoPath, int stage, string path, string tempDir) {
        string file = Path.Combine(tempDir, Path.GetFileName(ToLocal(path)));
        try {
            await _git.ExtractFileAsync(repoPath, $":{stage}", path, file);
        } catch (GitException) {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllBytesAsync(file, []);
        }
        return file;
    }

    /// <summary>Returns a file on disk holding the side's content, and whether it is the live working-tree file.</summary>
    private async Task<(string Path, bool IsWorkingFile)> MaterializeAsync(string repoPath, DiffSide side, string tempDir) {
        string fileName = side.Path is null ? "empty" : Path.GetFileName(ToLocal(side.Path));
        string tempFile = Path.Combine(tempDir, fileName);

        if (side.Path is not null && side.Revision is null) {
            string working = Path.Combine(repoPath, ToLocal(side.Path));
            if (File.Exists(working)) {
                return (working, true);
            }
        } else if (side.Path is not null) {
            await _git.ExtractFileAsync(repoPath, side.Revision!, side.Path, tempFile);
            return (tempFile, false);
        }

        // Empty side: a file that does not exist on this side of the comparison.
        Directory.CreateDirectory(tempDir);
        await File.WriteAllBytesAsync(tempFile, []);
        return (tempFile, false);
    }

    /// <summary>Creates a new, uniquely named temporary folder for one comparison.</summary>
    private static string NewTempFolder() {
        string dir = Path.Combine(TempRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Converts a repository-relative path to the local path format.</summary>
    private static string ToLocal(string repoRelativePath) => repoRelativePath.Replace('/', Path.DirectorySeparatorChar);
}
