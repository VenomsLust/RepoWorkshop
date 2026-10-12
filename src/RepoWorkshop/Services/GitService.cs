using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RepoWorkshop.Models;

namespace RepoWorkshop.Services;

/// <summary>Runs the git command line. All paths passed in are working-tree directories.</summary>
public sealed class GitService(LogService log)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Commands that only read state are not echoed to the log, to keep it focused on actions.
    private static readonly HashSet<string> QuietCommands =
        ["status", "for-each-ref", "rev-parse", "diff", "cat-file", "merge-base", "check-ref-format", "remote", "log"];

    /// <summary>The well-known id of git's empty tree, used as the "parent" of a root commit.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    private static bool IsQuiet(IReadOnlyList<string> args) =>
        args.Count > 0 && (QuietCommands.Contains(args[0]) || (args[0] == "stash" && args.Count > 1 && args[1] == "list"));

    public Task<GitResult> RunAsync(string repoPath, params string[] args) => RunAsync(repoPath, args, CancellationToken.None);

    public async Task<GitResult> RunAsync(string repoPath, IReadOnlyList<string> args, CancellationToken ct)
    {
        var quiet = IsQuiet(args);
        if (!quiet)
            log.Command($"[{Path.GetFileName(repoPath)}] git {string.Join(' ', args.Select(QuoteForDisplay))}");

        using var process = Start(repoPath, args);
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var result = new GitResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        if (!result.Success && !quiet)
            log.Error(result.Message.Length > 0 ? result.Message : $"git exited with code {result.ExitCode}");
        else if (!quiet && result.Message.Length > 0)
            log.Info(result.Message);
        return result;
    }

    /// <summary>Writes the raw stdout of a git command to a file (used for extracting blobs).</summary>
    public async Task<GitResult> RunToFileAsync(string repoPath, IReadOnlyList<string> args, string outputFile, CancellationToken ct = default)
    {
        using var process = Start(repoPath, args);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await using (var file = File.Create(outputFile))
            await process.StandardOutput.BaseStream.CopyToAsync(file, ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return new GitResult(process.ExitCode, "", await stderr.ConfigureAwait(false));
    }

    private static Process Start(string repoPath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        // Never block waiting for a terminal credential prompt; GUI credential helpers still work.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=off");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
            process.StandardInput.Close();
            return process;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Could not run git. Make sure Git for Windows is installed and on PATH.", ex);
        }
    }

    private static string QuoteForDisplay(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    // ---- Queries -------------------------------------------------------------------------------

    /// <summary>Returns the repository's top-level directory, or null if the path is not inside a work tree.</summary>
    public async Task<string?> GetTopLevelAsync(string path)
    {
        if (!Directory.Exists(path))
            return null;
        var result = await RunAsync(path, ["rev-parse", "--show-toplevel"], CancellationToken.None).ConfigureAwait(false);
        return result.Success ? Path.GetFullPath(result.StdOut.Trim()) : null;
    }

    public async Task<RepoStatus> GetStatusAsync(string repoPath)
    {
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Folder not found: {repoPath}");
        var result = await RunAsync(repoPath, ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new GitException(result);
        return ParseStatus(result.StdOut);
    }

    internal static RepoStatus ParseStatus(string output)
    {
        string? branch = null, oid = null, upstream = null;
        var detached = false;
        int ahead = 0, behind = 0;
        var staged = new List<FileChange>();
        var unstaged = new List<FileChange>();

        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length == 0)
                continue;

            switch (entry[0])
            {
                case '#':
                    var header = entry.Split(' ', 3);
                    if (header.Length < 3)
                        break;
                    switch (header[1])
                    {
                        case "branch.oid":
                            oid = header[2] == "(initial)" ? null : header[2];
                            break;
                        case "branch.head":
                            detached = header[2] == "(detached)";
                            branch = detached ? null : header[2];
                            break;
                        case "branch.upstream":
                            upstream = header[2];
                            break;
                        case "branch.ab":
                            var ab = header[2].Split(' ');
                            ahead = int.Parse(ab[0].TrimStart('+'));
                            behind = int.Parse(ab[1].TrimStart('-'));
                            break;
                    }
                    break;

                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var f = entry.Split(' ', 9);
                    AddXY(f[1], f[8], null, staged, unstaged);
                    break;
                }
                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path \0 origPath
                    var f = entry.Split(' ', 10);
                    var orig = i + 1 < entries.Length ? entries[++i] : null;
                    AddXY(f[1], f[9], orig, staged, unstaged);
                    break;
                }
                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    var f = entry.Split(' ', 11);
                    unstaged.Add(new FileChange(f[10], null, ChangeKind.Unmerged, f[1]));
                    break;
                }
                case '?':
                    unstaged.Add(new FileChange(entry[2..], null, ChangeKind.Untracked, "??"));
                    break;
            }
        }

        return new RepoStatus(branch, detached, oid, upstream, ahead, behind, staged, unstaged);
    }

    /// <summary>
    /// Splits an XY status into its index (staged) and work-tree (unstaged) halves.
    /// A rename is recorded on the index side; the work-tree side compares against the new path.
    /// </summary>
    private static void AddXY(string xy, string path, string? orig, List<FileChange> staged, List<FileChange> unstaged)
    {
        if (xy[0] != '.')
            staged.Add(new FileChange(path, orig, FileChange.KindFromCode(xy[0]), xy[0].ToString()));
        if (xy[1] != '.')
            unstaged.Add(new FileChange(path, null, FileChange.KindFromCode(xy[1]), xy[1].ToString()));
    }

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string repoPath)
    {
        const string format = "%(refname)%00%(refname:short)%00%(HEAD)%00%(upstream:short)%00%(upstream:track,nobracket)"
                              + "%00%(objectname:short)%00%(committerdate:iso-strict)%00%(contents:subject)";
        var result = await RunAsync(repoPath, ["for-each-ref", "--sort=-committerdate", $"--format={format}", "refs/heads", "refs/remotes"],
            CancellationToken.None).ConfigureAwait(false);
        if (!result.Success)
            throw new GitException(result);

        var branches = new List<BranchInfo>();
        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\0');
            if (f.Length < 8 || f[0].EndsWith("/HEAD", StringComparison.Ordinal))
                continue;

            var (ahead, behind, gone) = ParseTrack(f[4]);
            branches.Add(new BranchInfo(
                Name: f[1],
                FullRef: f[0],
                IsRemote: f[0].StartsWith("refs/remotes/", StringComparison.Ordinal),
                IsHead: f[2] == "*",
                Upstream: f[3].Length > 0 ? f[3] : null,
                Ahead: ahead,
                Behind: behind,
                UpstreamGone: gone,
                CommitId: f[5],
                CommitDate: DateTimeOffset.TryParse(f[6], out var date) ? date : null,
                Subject: f[7]));
        }
        return branches;
    }

    private static (int Ahead, int Behind, bool Gone) ParseTrack(string track)
    {
        if (track == "gone")
            return (0, 0, true);
        int ahead = 0, behind = 0;
        foreach (var part in track.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(' ');
            if (kv.Length == 2 && int.TryParse(kv[1], out var n))
            {
                if (kv[0] == "ahead") ahead = n;
                else if (kv[0] == "behind") behind = n;
            }
        }
        return (ahead, behind, false);
    }

    public async Task<IReadOnlyList<string>> GetRemotesAsync(string repoPath)
    {
        var result = await RunAsync(repoPath, ["remote"], CancellationToken.None).ConfigureAwait(false);
        return result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Files that differ between two revisions.</summary>
    public async Task<IReadOnlyList<FileChange>> GetChangesBetweenAsync(string repoPath, string from, string to)
    {
        var result = await RunAsync(repoPath, ["diff", "--name-status", "-z", "-M", from, to, "--"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new GitException(result);

        var changes = new List<FileChange>();
        var f = result.StdOut.Split('\0');
        for (var i = 0; i + 1 < f.Length; i++)
        {
            var code = f[i];
            if (code.Length == 0)
                continue;
            if (code[0] is 'R' or 'C' && i + 2 < f.Length)
            {
                changes.Add(new FileChange(f[i + 2], f[i + 1], FileChange.KindFromCode(code[0]), code[..1]));
                i += 2;
            }
            else
            {
                changes.Add(new FileChange(f[i + 1], null, FileChange.KindFromCode(code[0]), code[..1]));
                i += 1;
            }
        }
        return changes;
    }

    public async Task<string?> GetMergeBaseAsync(string repoPath, string a, string b)
    {
        var result = await RunAsync(repoPath, ["merge-base", a, b], CancellationToken.None).ConfigureAwait(false);
        return result.Success ? result.StdOut.Trim() : null;
    }

    public async Task<bool> IsValidBranchNameAsync(string repoPath, string name)
    {
        var result = await RunAsync(repoPath, ["check-ref-format", "--branch", name], CancellationToken.None).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<bool> LocalBranchExistsAsync(string repoPath, string name)
    {
        var result = await RunAsync(repoPath, ["rev-parse", "--verify", "--quiet", $"refs/heads/{name}"], CancellationToken.None)
            .ConfigureAwait(false);
        return result.Success;
    }

    /// <summary>Writes the contents of <paramref name="path"/> at <paramref name="revision"/> to a file, applying checkout filters.</summary>
    public async Task ExtractFileAsync(string repoPath, string revision, string path, string outputFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        var result = await RunToFileAsync(repoPath, ["cat-file", "--filters", $"{revision}:{path}"], outputFile).ConfigureAwait(false);
        if (!result.Success)
            throw new GitException(result);
    }

    public async Task<IReadOnlyList<CommitInfo>> GetLogAsync(string repoPath, string revision, int maxCount)
    {
        var result = await RunAsync(repoPath,
            ["log", $"--max-count={maxCount}", "--format=%H%x00%h%x00%an%x00%aI%x00%P%x00%s", revision, "--"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new GitException(result);

        var commits = new List<CommitInfo>();
        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\0');
            if (f.Length < 6)
                continue;
            commits.Add(new CommitInfo(f[0], f[1], f[2], DateTimeOffset.Parse(f[3]), f[5],
                f[4].Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }
        return commits;
    }

    /// <summary>The full message of the HEAD commit (used to pre-fill an amend).</summary>
    public async Task<string?> GetHeadMessageAsync(string repoPath)
    {
        var result = await RunAsync(repoPath, ["log", "-1", "--format=%B"], CancellationToken.None).ConfigureAwait(false);
        return result.Success ? result.StdOut.TrimEnd() : null;
    }

    public async Task<IReadOnlyList<StashInfo>> GetStashesAsync(string repoPath)
    {
        var result = await RunAsync(repoPath, ["stash", "list", "--format=%gd%x00%cI%x00%gs"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new GitException(result);

        var stashes = new List<StashInfo>();
        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\0');
            if (f.Length >= 3)
                stashes.Add(new StashInfo(f[0], f[2], DateTimeOffset.Parse(f[1])));
        }
        return stashes;
    }

    // ---- Actions -------------------------------------------------------------------------------

    /// <summary>Stages paths (including deletions). Paths are sent in batches to stay under the command-line limit.</summary>
    public Task<GitResult> StageAsync(string repoPath, IReadOnlyList<string> paths) =>
        RunBatchedAsync(repoPath, ["add", "-A", "--"], paths);

    public Task<GitResult> StageAllAsync(string repoPath) => RunAsync(repoPath, "add", "-A");

    public Task<GitResult> UnstageAsync(string repoPath, IReadOnlyList<string> paths, bool hasHead) =>
        hasHead
            ? RunBatchedAsync(repoPath, ["restore", "--staged", "--"], paths)
            : RunBatchedAsync(repoPath, ["rm", "--cached", "-r", "-q", "--"], paths);

    public Task<GitResult> UnstageAllAsync(string repoPath, bool hasHead) =>
        hasHead ? RunAsync(repoPath, "reset", "-q") : RunAsync(repoPath, "rm", "--cached", "-r", "-q", ".");

    private async Task<GitResult> RunBatchedAsync(string repoPath, string[] command, IReadOnlyList<string> paths)
    {
        GitResult result = new(0, "", "");
        foreach (var batch in paths.Chunk(100))
        {
            result = await RunAsync(repoPath, [.. command, .. batch], CancellationToken.None).ConfigureAwait(false);
            if (!result.Success)
                break;
        }
        return result;
    }

    public Task<GitResult> CommitAsync(string repoPath, string message, bool amend)
    {
        var args = new List<string> { "commit", "-m", message };
        if (amend)
            args.Add("--amend");
        return RunAsync(repoPath, args, CancellationToken.None);
    }

    public Task<GitResult> StashPushAsync(string repoPath, string? message, bool includeUntracked)
    {
        var args = new List<string> { "stash", "push" };
        if (includeUntracked)
            args.Add("--include-untracked");
        if (!string.IsNullOrWhiteSpace(message))
            args.AddRange(["-m", message]);
        return RunAsync(repoPath, args, CancellationToken.None);
    }

    public Task<GitResult> StashApplyAsync(string repoPath, string stashRef) => RunAsync(repoPath, "stash", "apply", stashRef);

    public Task<GitResult> StashPopAsync(string repoPath, string stashRef) => RunAsync(repoPath, "stash", "pop", stashRef);

    public Task<GitResult> StashDropAsync(string repoPath, string stashRef) => RunAsync(repoPath, "stash", "drop", stashRef);

    public Task<GitResult> FetchAsync(string repoPath) => RunAsync(repoPath, "fetch", "--all", "--prune");

    public Task<GitResult> PullAsync(string repoPath) => RunAsync(repoPath, "pull", "--ff-only");

    public async Task<GitResult> PushAsync(string repoPath, string branch, bool hasUpstream)
    {
        if (hasUpstream)
            return await RunAsync(repoPath, "push").ConfigureAwait(false);

        var remotes = await GetRemotesAsync(repoPath).ConfigureAwait(false);
        var remote = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
        if (remote is null)
            return new GitResult(1, "", "This repository has no remotes to push to.");
        return await RunAsync(repoPath, "push", "--set-upstream", remote, branch).ConfigureAwait(false);
    }

    /// <summary>
    /// Switches to a branch. For a remote branch, switches to the matching local branch,
    /// creating a tracking branch if one does not exist yet.
    /// </summary>
    public async Task<GitResult> CheckoutAsync(string repoPath, BranchInfo branch)
    {
        if (!branch.IsRemote)
            return await RunAsync(repoPath, "switch", branch.Name).ConfigureAwait(false);

        return await LocalBranchExistsAsync(repoPath, branch.ShortName).ConfigureAwait(false)
            ? await RunAsync(repoPath, "switch", branch.ShortName).ConfigureAwait(false)
            : await RunAsync(repoPath, "switch", "--track", branch.Name).ConfigureAwait(false);
    }

    /// <summary>
    /// Switches to a branch by name. git creates a tracking branch automatically when only
    /// a single remote has a branch of that name.
    /// </summary>
    public Task<GitResult> SwitchAsync(string repoPath, string branchName) => RunAsync(repoPath, "switch", branchName);

    public Task<GitResult> CreateBranchAsync(string repoPath, string name, string? startPoint, bool checkout)
    {
        var args = new List<string>();
        if (checkout)
            args.AddRange(["switch", "-c", name]);
        else
            args.AddRange(["branch", name]);
        if (!string.IsNullOrEmpty(startPoint))
            args.Add(startPoint);
        return RunAsync(repoPath, args, CancellationToken.None);
    }

    public Task<GitResult> DeleteBranchAsync(string repoPath, string name, bool force) =>
        RunAsync(repoPath, "branch", force ? "-D" : "-d", name);

    public Task<GitResult> MergeAsync(string repoPath, string branch) => RunAsync(repoPath, "merge", "--no-edit", branch);
}

public sealed class GitException(GitResult result) : Exception(result.Message)
{
    public GitResult Result { get; } = result;
}
