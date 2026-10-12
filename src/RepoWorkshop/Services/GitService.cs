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
public sealed class GitService {
    /// <summary>The well-known id of git's empty tree, used as the "parent" of a root commit.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>Paths are sent to git in batches of this size to stay under the command-line limit.</summary>
    private const int PathBatchSize = 100;

    /// <summary>git's output encoding (UTF-8 without BOM).</summary>
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Read-only commands, not echoed to the log so it stays focused on actions.</summary>
    private static readonly HashSet<string> QuietCommands =
        ["status", "for-each-ref", "rev-parse", "diff", "cat-file", "merge-base", "check-ref-format", "remote", "log"];

    /// <summary>Receives the commands run and their output.</summary>
    private readonly LogService _log;

    /// <summary>Creates the service, logging commands to <paramref name="log"/>.</summary>
    public GitService(LogService log) {
        _log = log;
    }

    // ---- Running git ---------------------------------------------------------------------------

    /// <summary>Runs git with the given arguments in <paramref name="repoPath"/>.</summary>
    public Task<GitResult> RunAsync(string repoPath, params string[] args) => RunAsync(repoPath, args, CancellationToken.None);

    /// <summary>Runs git and captures its output; state-changing commands are echoed to the log.</summary>
    public async Task<GitResult> RunAsync(string repoPath, IReadOnlyList<string> args, CancellationToken ct) {
        bool quiet = IsQuiet(args);
        if (!quiet) {
            _log.Command($"[{Path.GetFileName(repoPath)}] git {string.Join(' ', args.Select(QuoteForDisplay))}");
        }

        using Process process = Start(repoPath, args);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        GitResult result = new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        if (!result.Success && !quiet) {
            _log.Error(result.Message.Length > 0 ? result.Message : $"git exited with code {result.ExitCode}");
        } else if (!quiet && result.Message.Length > 0) {
            _log.Info(result.Message);
        }
        return result;
    }

    /// <summary>Writes the raw stdout of a git command to a file (used for extracting blobs).</summary>
    public async Task<GitResult> RunToFileAsync(string repoPath, IReadOnlyList<string> args, string outputFile, CancellationToken ct = default) {
        using Process process = Start(repoPath, args);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        await using (FileStream file = File.Create(outputFile)) {
            await process.StandardOutput.BaseStream.CopyToAsync(file, ct).ConfigureAwait(false);
        }
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return new GitResult(process.ExitCode, "", await stderr.ConfigureAwait(false));
    }

    // ---- Queries -------------------------------------------------------------------------------

    /// <summary>Returns the repository's top-level directory, or null if the path is not inside a work tree.</summary>
    public async Task<string?> GetTopLevelAsync(string path) {
        if (!Directory.Exists(path)) {
            return null;
        }
        GitResult result = await RunAsync(path, ["rev-parse", "--show-toplevel"], CancellationToken.None).ConfigureAwait(false);
        return result.Success ? Path.GetFullPath(result.StdOut.Trim()) : null;
    }

    /// <summary>The branch, upstream, and staged and unstaged changes of a working tree.</summary>
    public async Task<RepoStatus> GetStatusAsync(string repoPath) {
        if (!Directory.Exists(repoPath)) {
            throw new DirectoryNotFoundException($"Folder not found: {repoPath}");
        }
        GitResult result = await RunAsync(repoPath, ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success) {
            throw new GitException(result);
        }
        return ParseStatus(result.StdOut);
    }

    /// <summary>Local and remote branches, most recently committed first.</summary>
    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string repoPath) {
        const string format = "%(refname)%00%(refname:short)%00%(HEAD)%00%(upstream:short)%00%(upstream:track,nobracket)"
                              + "%00%(objectname:short)%00%(committerdate:iso-strict)%00%(contents:subject)";
        GitResult result = await RunAsync(repoPath, ["for-each-ref", "--sort=-committerdate", $"--format={format}", "refs/heads", "refs/remotes"],
            CancellationToken.None).ConfigureAwait(false);
        if (!result.Success) {
            throw new GitException(result);
        }

        List<BranchInfo> branches = [];
        foreach (string line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            string[] f = line.Split('\0');
            if (f.Length < 8 || f[0].EndsWith("/HEAD", StringComparison.Ordinal)) {
                continue;
            }

            (int ahead, int behind, bool gone) = ParseTrack(f[4]);
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
                CommitDate: DateTimeOffset.TryParse(f[6], out DateTimeOffset date) ? date : null,
                Subject: f[7]));
        }
        return branches;
    }

    /// <summary>The names of the repository's remotes.</summary>
    public async Task<IReadOnlyList<string>> GetRemotesAsync(string repoPath) {
        GitResult result = await RunAsync(repoPath, ["remote"], CancellationToken.None).ConfigureAwait(false);
        return result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Files that differ between two revisions.</summary>
    public async Task<IReadOnlyList<FileChange>> GetChangesBetweenAsync(string repoPath, string from, string to) {
        GitResult result = await RunAsync(repoPath, ["diff", "--name-status", "-z", "-M", from, to, "--"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success) {
            throw new GitException(result);
        }

        List<FileChange> changes = [];
        string[] f = result.StdOut.Split('\0');
        for (int i = 0; i + 1 < f.Length; i++) {
            string code = f[i];
            if (code.Length == 0) {
                continue;
            }
            if (code[0] is 'R' or 'C' && i + 2 < f.Length) {
                changes.Add(new FileChange(f[i + 2], f[i + 1], FileChange.KindFromCode(code[0]), code[..1]));
                i += 2;
            } else {
                changes.Add(new FileChange(f[i + 1], null, FileChange.KindFromCode(code[0]), code[..1]));
                i += 1;
            }
        }
        return changes;
    }

    /// <summary>The best common ancestor of two revisions, or null if they have none.</summary>
    public async Task<string?> GetMergeBaseAsync(string repoPath, string a, string b) {
        GitResult result = await RunAsync(repoPath, ["merge-base", a, b], CancellationToken.None).ConfigureAwait(false);
        return result.Success ? result.StdOut.Trim() : null;
    }

    /// <summary>Whether <paramref name="name"/> is allowed as a branch name.</summary>
    public async Task<bool> IsValidBranchNameAsync(string repoPath, string name) {
        GitResult result = await RunAsync(repoPath, ["check-ref-format", "--branch", name], CancellationToken.None).ConfigureAwait(false);
        return result.Success;
    }

    /// <summary>Whether a local branch with this name exists.</summary>
    public async Task<bool> LocalBranchExistsAsync(string repoPath, string name) {
        GitResult result = await RunAsync(repoPath, ["rev-parse", "--verify", "--quiet", $"refs/heads/{name}"], CancellationToken.None)
            .ConfigureAwait(false);
        return result.Success;
    }

    /// <summary>Writes the contents of <paramref name="path"/> at <paramref name="revision"/> to a file, applying checkout filters.</summary>
    public async Task ExtractFileAsync(string repoPath, string revision, string path, string outputFile) {
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        GitResult result = await RunToFileAsync(repoPath, ["cat-file", "--filters", $"{revision}:{path}"], outputFile).ConfigureAwait(false);
        if (!result.Success) {
            throw new GitException(result);
        }
    }

    /// <summary>Up to <paramref name="maxCount"/> commits reachable from <paramref name="revision"/>, newest first.</summary>
    public async Task<IReadOnlyList<CommitInfo>> GetLogAsync(string repoPath, string revision, int maxCount) {
        GitResult result = await RunAsync(repoPath,
            ["log", $"--max-count={maxCount}", "--format=%H%x00%h%x00%an%x00%aI%x00%P%x00%s", revision, "--"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success) {
            throw new GitException(result);
        }

        List<CommitInfo> commits = [];
        foreach (string line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            string[] f = line.Split('\0');
            if (f.Length < 6) {
                continue;
            }
            commits.Add(new CommitInfo(f[0], f[1], f[2], DateTimeOffset.Parse(f[3]), f[5],
                f[4].Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }
        return commits;
    }

    /// <summary>The full message of the HEAD commit (used to pre-fill an amend).</summary>
    public async Task<string?> GetHeadMessageAsync(string repoPath) {
        GitResult result = await RunAsync(repoPath, ["log", "-1", "--format=%B"], CancellationToken.None).ConfigureAwait(false);
        return result.Success ? result.StdOut.TrimEnd() : null;
    }

    /// <summary>The stash list, newest first.</summary>
    public async Task<IReadOnlyList<StashInfo>> GetStashesAsync(string repoPath) {
        GitResult result = await RunAsync(repoPath, ["stash", "list", "--format=%gd%x00%cI%x00%gs"], CancellationToken.None)
            .ConfigureAwait(false);
        if (!result.Success) {
            throw new GitException(result);
        }

        List<StashInfo> stashes = [];
        foreach (string line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            string[] f = line.Split('\0');
            if (f.Length >= 3) {
                stashes.Add(new StashInfo(f[0], f[2], DateTimeOffset.Parse(f[1])));
            }
        }
        return stashes;
    }

    // ---- Actions -------------------------------------------------------------------------------

    /// <summary>Stages paths (including deletions).</summary>
    public Task<GitResult> StageAsync(string repoPath, IReadOnlyList<string> paths) =>
        RunBatchedAsync(repoPath, ["add", "-A", "--"], paths);

    /// <summary>Stages every change, including untracked files.</summary>
    public Task<GitResult> StageAllAsync(string repoPath) => RunAsync(repoPath, "add", "-A");

    /// <summary>Unstages paths, keeping the working-tree changes.</summary>
    public Task<GitResult> UnstageAsync(string repoPath, IReadOnlyList<string> paths, bool hasHead) =>
        hasHead
            ? RunBatchedAsync(repoPath, ["restore", "--staged", "--"], paths)
            : RunBatchedAsync(repoPath, ["rm", "--cached", "-r", "-q", "--"], paths);

    /// <summary>Unstages everything, keeping the working-tree changes.</summary>
    public Task<GitResult> UnstageAllAsync(string repoPath, bool hasHead) =>
        hasHead ? RunAsync(repoPath, "reset", "-q") : RunAsync(repoPath, "rm", "--cached", "-r", "-q", ".");

    /// <summary>Commits the staged changes, or replaces the last commit when <paramref name="amend"/>.</summary>
    public Task<GitResult> CommitAsync(string repoPath, string message, bool amend) {
        List<string> args = ["commit", "-m", message];
        if (amend) {
            args.Add("--amend");
        }
        return RunAsync(repoPath, args, CancellationToken.None);
    }

    /// <summary>Stashes the working-tree changes.</summary>
    public Task<GitResult> StashPushAsync(string repoPath, string? message, bool includeUntracked) {
        List<string> args = ["stash", "push"];
        if (includeUntracked) {
            args.Add("--include-untracked");
        }
        if (!string.IsNullOrWhiteSpace(message)) {
            args.AddRange(["-m", message]);
        }
        return RunAsync(repoPath, args, CancellationToken.None);
    }

    /// <summary>Applies a stash and keeps it.</summary>
    public Task<GitResult> StashApplyAsync(string repoPath, string stashRef) => RunAsync(repoPath, "stash", "apply", stashRef);

    /// <summary>Applies a stash and removes it.</summary>
    public Task<GitResult> StashPopAsync(string repoPath, string stashRef) => RunAsync(repoPath, "stash", "pop", stashRef);

    /// <summary>Deletes a stash.</summary>
    public Task<GitResult> StashDropAsync(string repoPath, string stashRef) => RunAsync(repoPath, "stash", "drop", stashRef);

    /// <summary>Fetches all remotes, pruning deleted branches.</summary>
    public Task<GitResult> FetchAsync(string repoPath) => RunAsync(repoPath, "fetch", "--all", "--prune");

    /// <summary>Pulls the current branch, fast-forward only.</summary>
    public Task<GitResult> PullAsync(string repoPath) => RunAsync(repoPath, "pull", "--ff-only");

    /// <summary>Pushes the current branch; without an upstream, pushes to origin (or the only remote) and sets it.</summary>
    public async Task<GitResult> PushAsync(string repoPath, string branch, bool hasUpstream) {
        if (hasUpstream) {
            return await RunAsync(repoPath, "push").ConfigureAwait(false);
        }

        IReadOnlyList<string> remotes = await GetRemotesAsync(repoPath).ConfigureAwait(false);
        string? remote = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
        if (remote is null) {
            return new GitResult(1, "", "This repository has no remotes to push to.");
        }
        return await RunAsync(repoPath, "push", "--set-upstream", remote, branch).ConfigureAwait(false);
    }

    /// <summary>
    /// Switches to a branch. For a remote branch, switches to the matching local branch,
    /// creating a tracking branch if one does not exist yet.
    /// </summary>
    public async Task<GitResult> CheckoutAsync(string repoPath, BranchInfo branch) {
        if (!branch.IsRemote) {
            return await RunAsync(repoPath, "switch", branch.Name).ConfigureAwait(false);
        }
        return await LocalBranchExistsAsync(repoPath, branch.ShortName).ConfigureAwait(false)
            ? await RunAsync(repoPath, "switch", branch.ShortName).ConfigureAwait(false)
            : await RunAsync(repoPath, "switch", "--track", branch.Name).ConfigureAwait(false);
    }

    /// <summary>
    /// Switches to a branch by name. git creates a tracking branch automatically when only
    /// a single remote has a branch of that name.
    /// </summary>
    public Task<GitResult> SwitchAsync(string repoPath, string branchName) => RunAsync(repoPath, "switch", branchName);

    /// <summary>Creates a branch at <paramref name="startPoint"/> (default HEAD), optionally switching to it.</summary>
    public Task<GitResult> CreateBranchAsync(string repoPath, string name, string? startPoint, bool checkout) {
        List<string> args = checkout ? ["switch", "-c", name] : ["branch", name];
        if (!string.IsNullOrEmpty(startPoint)) {
            args.Add(startPoint);
        }
        return RunAsync(repoPath, args, CancellationToken.None);
    }

    /// <summary>Deletes a local branch; <paramref name="force"/> deletes it even if unmerged.</summary>
    public Task<GitResult> DeleteBranchAsync(string repoPath, string name, bool force) =>
        RunAsync(repoPath, "branch", force ? "-D" : "-d", name);

    /// <summary>Merges a branch into the current branch.</summary>
    public Task<GitResult> MergeAsync(string repoPath, string branch) => RunAsync(repoPath, "merge", "--no-edit", branch);

    // ---- Parsing and helpers -------------------------------------------------------------------

    /// <summary>Parses <c>git status --porcelain=v2 --branch -z</c> output.</summary>
    internal static RepoStatus ParseStatus(string output) {
        string? branch = null;
        string? oid = null;
        string? upstream = null;
        bool detached = false;
        int ahead = 0;
        int behind = 0;
        List<FileChange> staged = [];
        List<FileChange> unstaged = [];

        string[] entries = output.Split('\0');
        for (int i = 0; i < entries.Length; i++) {
            string entry = entries[i];
            if (entry.Length == 0) {
                continue;
            }

            switch (entry[0]) {
                case '#': {
                    // # branch.<key> <value>
                    string[] header = entry.Split(' ', 3);
                    if (header.Length < 3) {
                        break;
                    }
                    switch (header[1]) {
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
                            string[] ab = header[2].Split(' ');
                            ahead = int.Parse(ab[0].TrimStart('+'));
                            behind = int.Parse(ab[1].TrimStart('-'));
                            break;
                    }
                    break;
                }
                case '1': {
                    // 1 XY sub mH mI mW hH hI path
                    string[] f = entry.Split(' ', 9);
                    AddXY(f[1], f[8], null, staged, unstaged);
                    break;
                }
                case '2': {
                    // 2 XY sub mH mI mW hH hI Xscore path \0 origPath
                    string[] f = entry.Split(' ', 10);
                    string? orig = i + 1 < entries.Length ? entries[++i] : null;
                    AddXY(f[1], f[9], orig, staged, unstaged);
                    break;
                }
                case 'u': {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    string[] f = entry.Split(' ', 11);
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

    /// <summary>Runs a command once per batch of paths, stopping at the first failure.</summary>
    private async Task<GitResult> RunBatchedAsync(string repoPath, string[] command, IReadOnlyList<string> paths) {
        GitResult result = new(0, "", "");
        foreach (string[] batch in paths.Chunk(PathBatchSize)) {
            result = await RunAsync(repoPath, [.. command, .. batch], CancellationToken.None).ConfigureAwait(false);
            if (!result.Success) {
                break;
            }
        }
        return result;
    }

    /// <summary>Starts git with output redirected and terminal prompts disabled.</summary>
    private static Process Start(string repoPath, IReadOnlyList<string> args) {
        ProcessStartInfo psi = new("git") {
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
        foreach (string arg in args) {
            psi.ArgumentList.Add(arg);
        }

        try {
            Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
            process.StandardInput.Close();
            return process;
        } catch (Win32Exception ex) {
            throw new InvalidOperationException("Could not run git. Make sure Git for Windows is installed and on PATH.", ex);
        }
    }

    /// <summary>Whether a command only reads state (and so is not logged).</summary>
    private static bool IsQuiet(IReadOnlyList<string> args) =>
        args.Count > 0 && (QuietCommands.Contains(args[0]) || (args[0] == "stash" && args.Count > 1 && args[1] == "list"));

    /// <summary>Quotes an argument containing spaces, for the log.</summary>
    private static string QuoteForDisplay(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    /// <summary>
    /// Splits an XY status into its index (staged) and work-tree (unstaged) halves.
    /// A rename is recorded on the index side; the work-tree side compares against the new path.
    /// </summary>
    private static void AddXY(string xy, string path, string? orig, List<FileChange> staged, List<FileChange> unstaged) {
        if (xy[0] != '.') {
            staged.Add(new FileChange(path, orig, FileChange.KindFromCode(xy[0]), xy[0].ToString()));
        }
        if (xy[1] != '.') {
            unstaged.Add(new FileChange(path, null, FileChange.KindFromCode(xy[1]), xy[1].ToString()));
        }
    }

    /// <summary>Parses "ahead 1, behind 2" / "gone" from <c>%(upstream:track,nobracket)</c>.</summary>
    private static (int Ahead, int Behind, bool Gone) ParseTrack(string track) {
        if (track == "gone") {
            return (0, 0, true);
        }

        int ahead = 0;
        int behind = 0;
        foreach (string part in track.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            string[] kv = part.Split(' ');
            if (kv.Length != 2 || !int.TryParse(kv[1], out int n)) {
                continue;
            }
            if (kv[0] == "ahead") {
                ahead = n;
            } else if (kv[0] == "behind") {
                behind = n;
            }
        }
        return (ahead, behind, false);
    }
}
