using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RepoWorkshop.Api;
using RepoWorkshop.Models;
using RepoWorkshop.Services;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Automation;

/// <summary>The API's method table: each method's documentation and handler.</summary>
public sealed partial class ApiHost {
    /// <summary>The tab names accepted by ui.show.</summary>
    private const string TabNames = "branches, changes, history or stashes";

    /// <summary>Default number of commits returned by repo.log.</summary>
    private const int DefaultLogCount = 30;

    /// <summary>Maximum number of commits returned by repo.log.</summary>
    private const int MaxLogCount = 1000;

    /// <summary>The "repo" parameter shared by repository methods.</summary>
    private static readonly ApiParamInfo RepoParam =
        P("repo", "string", "Repository name, alias or path. Defaults to the registered repository containing the current directory.");

    /// <summary>An optional "project" parameter that narrows which repositories "repo" matches.</summary>
    private static readonly ApiParamInfo ProjectScopeParam =
        P("project", "string", "Project name or id; only needed when a repository name is ambiguous.");

    /// <summary>The required "project" parameter of project methods.</summary>
    private static readonly ApiParamInfo ProjectParam = P("project", "string", "Project name or id.", required: true);

    /// <summary>Defines every API method.</summary>
    private IEnumerable<ApiMethodInfo> BuildMethods() {
        // ---- App ------------------------------------------------------------------------------

        yield return Query("help", "List the API methods and their parameters.",
            [P("method", "string", "Show only this method.")],
            call => {
                string? filter = call.String("method");
                List<ApiMethodInfo> matches = Methods
                    .Where(m => filter is null || string.Equals(m.Name, filter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ToList();
                if (filter is not null && matches.Count == 0) {
                    throw ApiCall.NotFound($"Unknown method \"{filter}\".");
                }
                return Task.FromResult<object?>(new {
                    Methods = matches.Select(m => new { m.Name, m.Summary, m.ChangesState, m.Params }),
                });
            });

        yield return Query("app.info", "Version and status of the running app.", [],
            call => Task.FromResult<object?>(new {
                App = "Repo Workshop",
                Version,
                ProcessId = Environment.ProcessId,
                Pipe = _pipeName,
                WinMerge = call.Services.WinMerge.ResolveExecutable(),
                Projects = call.Main.Projects.Count,
                SelectedProject = call.Main.SelectedProject?.Name,
            }));

        yield return Command("app.activate", "Bring the app window to the front.", [],
            call => {
                call.Services.Dialogs.BringToFront();
                return Task.FromResult<object?>(null);
            });

        // ---- Projects -------------------------------------------------------------------------

        yield return Query("project.list", "List projects and their repositories.", [],
            call => Task.FromResult<object?>(new {
                Projects = call.Main.Projects.Select(p => new {
                    p.Name,
                    Id = p.Model.Id,
                    Selected = p == call.Main.SelectedProject,
                    Repos = p.Repos.Select(r => new { r.Name, r.Path }),
                }),
            }));

        yield return Command("project.create", "Create a project.",
            [P("name", "string", "Project name.", required: true)],
            call => {
                string name = call.RequiredString("name").Trim();
                if (call.Main.Projects.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) {
                    throw ApiCall.Invalid($"A project named \"{name}\" already exists.");
                }
                ProjectViewModel project = call.Main.CreateProject(name);
                return Task.FromResult<object?>(new { project.Name, Id = project.Model.Id });
            });

        yield return Command("project.rename", "Rename a project.",
            [ProjectParam, P("name", "string", "New name.", required: true)],
            call => {
                ProjectViewModel project = call.Project();
                project.Name = call.RequiredString("name").Trim();
                call.Services.SaveSettings();
                return Task.FromResult<object?>(new { project.Name, Id = project.Model.Id });
            });

        yield return Command("project.delete", "Remove a project from the app (repositories on disk are untouched). Asks the user to confirm.",
            [ProjectParam],
            async call => {
                ProjectViewModel project = call.Project();
                await call.ConfirmAsync("Remove project",
                    $"Remove the project \"{project.Name}\" from Repo Workshop.\n(Repositories on disk are not affected.)", "Remove");
                call.Main.RemoveProject(project);
                return new { Removed = project.Name };
            });

        yield return Command("project.addRepo", "Add the git repository at (or containing) a path to a project.",
            [ProjectParam, P("path", "string", "Repository folder; relative paths are resolved from the current directory.", required: true)],
            async call => {
                call.RequiredString("project");
                ProjectViewModel project = call.Project();
                string path = call.RequiredString("path");
                if (!Path.IsPathRooted(path) && call.String(ApiCall.CwdParam) is { } cwd) {
                    path = Path.GetFullPath(path, cwd);
                }

                (RepoViewModel? repo, string? error) = await project.AddRepoByPathAsync(path);
                if (repo is null) {
                    throw ApiCall.Invalid(error!.Replace("\n\n", " "));
                }
                await repo.RefreshAsync();
                return RepoSummary(repo);
            });

        yield return Command("project.removeRepo", "Remove a repository from a project (the repository on disk is untouched). Asks the user to confirm.",
            [ProjectParam, RepoParam with { Required = true }],
            async call => {
                ProjectViewModel project = call.Project();
                RepoViewModel repo = call.Repo();
                await call.ConfirmAsync("Remove repository",
                    $"Remove \"{repo.Name}\" from the project \"{project.Name}\".\n{repo.Path}\n(The repository on disk is not affected.)", "Remove");
                project.RemoveRepo(repo);
                return new { Removed = repo.Name, Project = project.Name };
            });

        yield return Query("project.status", "Branch, sync and change summary for every repository in a project.",
            [ProjectParam],
            async call => {
                ProjectViewModel project = call.Project();
                await project.RefreshAllAsync();
                return new { Project = project.Name, Repos = project.Repos.Select(RepoSummary) };
            });

        yield return Command("project.fetch", "Fetch (all remotes, prune) in every repository of a project.", [ProjectParam],
            async call => {
                ProjectViewModel project = call.Project();
                return new { Project = project.Name, Results = await project.FetchAllAsync() };
            });

        yield return Command("project.pull", "Pull (fast-forward only) in every repository of a project.", [ProjectParam],
            async call => {
                ProjectViewModel project = call.Project();
                return new { Project = project.Name, Results = await project.PullAllAsync() };
            });

        yield return Command("project.switch", "Switch every repository that has the branch (locally or on a remote) to it; others are skipped.",
            [ProjectParam, P("branch", "string", "Branch name, e.g. feature/login.", required: true)],
            async call => {
                ProjectViewModel project = call.Project();
                return new { Project = project.Name, Results = await project.SwitchAllAsync(call.RequiredString("branch").Trim()) };
            });

        yield return Command("project.createBranch", "Create a branch from the current HEAD in every repository of a project and switch to it; repositories that already have it are skipped.",
            [ProjectParam, P("branch", "string", "New branch name.", required: true)],
            async call => {
                ProjectViewModel project = call.Project();
                string branch = call.RequiredString("branch").Trim();
                if (project.Repos.Count > 0 && !await call.Services.Git.IsValidBranchNameAsync(project.Repos[0].Path, branch)) {
                    throw ApiCall.Invalid($"\"{branch}\" is not a valid branch name.");
                }
                return new { Project = project.Name, Results = await project.CreateBranchAllAsync(branch) };
            });

        // ---- Repository queries ---------------------------------------------------------------

        yield return Query("repo.status", "Branch, upstream, ahead/behind, staged and unstaged files, and stash count for a repository.",
            [RepoParam, ProjectScopeParam],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                return RepoDetail(repo);
            });

        yield return Query("repo.branches", "Local (and optionally remote) branches with tracking info and last commit.",
            [RepoParam, ProjectScopeParam, P("remote", "bool", "Include remote branches (default true).")],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                return new {
                    Repo = repo.Name,
                    Current = repo.CurrentBranch,
                    Local = repo.LocalBranches.Select(BranchDto),
                    Remote = call.Bool("remote", true) ? repo.RemoteBranches.Select(BranchDto) : null,
                };
            });

        yield return Query("repo.log", "Commit history of a branch or revision, newest first.",
            [RepoParam, ProjectScopeParam,
             P("ref", "string", "Branch, tag or commit (default HEAD)."),
             P("max", "int", $"Maximum commits to return (default {DefaultLogCount}, at most {MaxLogCount}).")],
            async call => {
                RepoViewModel repo = call.Repo();
                string revision = call.String("ref") ?? "HEAD";
                int max = Math.Clamp(call.Int("max", DefaultLogCount), 1, MaxLogCount);
                IReadOnlyList<CommitInfo> commits = await call.Services.Git.GetLogAsync(repo.Path, revision, max);
                return new { Repo = repo.Name, Ref = revision, Commits = commits.Select(CommitDto) };
            });

        yield return Query("repo.show", "A commit's details and the files it changed (vs its first parent).",
            [RepoParam, ProjectScopeParam, P("commit", "string", "Commit hash or revision.", required: true)],
            async call => {
                RepoViewModel repo = call.Repo();
                CommitInfo commit = await GetCommitAsync(call, repo, call.RequiredString("commit"));
                IReadOnlyList<FileChange> files = await call.Services.Git.GetChangesBetweenAsync(
                    repo.Path, commit.FirstParent ?? GitService.EmptyTree, commit.Hash);
                return new { Repo = repo.Name, Commit = CommitDto(commit), Files = files.Select(FileDto) };
            });

        yield return Query("repo.stashes", "List a repository's stashes.", [RepoParam, ProjectScopeParam],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                return new { Repo = repo.Name, Stashes = repo.Stashes.Items.Select(s => new { s.Ref, s.Message, s.Date }) };
            });

        yield return Command("repo.refresh", "Reload a repository's state in the app (or every repository of a project when only \"project\" is given). Call after changing a repository with plain git.",
            [RepoParam, P("project", "string", "Project name or id.")],
            async call => {
                if (call.Has("project") && !call.Has("repo")) {
                    ProjectViewModel project = call.Project();
                    await project.RefreshAllAsync();
                    return new { Project = project.Name, Repos = project.Repos.Select(RepoSummary) };
                }
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                return RepoSummary(repo);
            });

        // ---- Repository actions ---------------------------------------------------------------

        yield return Command("repo.fetch", "Fetch all remotes (with prune).", [RepoParam, ProjectScopeParam],
            call => RunAsync(call, "Fetch", repo => repo.FetchAsync()));

        yield return Command("repo.pull", "Pull the current branch (fast-forward only).", [RepoParam, ProjectScopeParam],
            call => RunAsync(call, "Pull", repo => repo.PullAsync()));

        yield return Command("repo.push", "Push the current branch; sets the upstream on the first push.", [RepoParam, ProjectScopeParam],
            call => RunAsync(call, "Push", repo => {
                string branch = repo.CurrentBranch ?? throw ApiCall.Invalid("HEAD is detached; switch to a branch before pushing.");
                bool hasUpstream = repo.Upstream is not null;
                return repo.ExecuteAsync(() => call.Services.Git.PushAsync(repo.Path, branch, hasUpstream));
            }));

        yield return Command("repo.checkout", "Switch to a branch. A remote branch (e.g. origin/x) gets a local tracking branch.",
            [RepoParam, ProjectScopeParam, P("branch", "string", "Local branch, or remote branch such as origin/feature.", required: true)],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                string name = call.RequiredString("branch").Trim();
                BranchInfo? branch = repo.LocalBranches.FirstOrDefault(b => b.Name == name)
                                     ?? repo.RemoteBranches.FirstOrDefault(b => b.Name == name);
                return await RunAsync(call, "Checkout", r => branch is null
                    ? r.SwitchToAsync(name)
                    : r.ExecuteAsync(() => call.Services.Git.CheckoutAsync(r.Path, branch)));
            });

        yield return Command("repo.createBranch", "Create a branch (and by default switch to it).",
            [RepoParam, ProjectScopeParam,
             P("branch", "string", "New branch name.", required: true),
             P("from", "string", "Start point (default HEAD)."),
             P("checkout", "bool", "Switch to the new branch (default true).")],
            async call => {
                RepoViewModel repo = call.Repo();
                string name = call.RequiredString("branch").Trim();
                if (!await call.Services.Git.IsValidBranchNameAsync(repo.Path, name)) {
                    throw ApiCall.Invalid($"\"{name}\" is not a valid branch name.");
                }
                string? from = call.String("from");
                bool checkout = call.Bool("checkout", true);
                return await RunAsync(call, "Create branch", r => r.CreateBranchAsync(name, from, checkout));
            });

        yield return Command("repo.deleteBranch", "Delete a local branch. Asks the user to confirm.",
            [RepoParam, ProjectScopeParam,
             P("branch", "string", "Local branch name.", required: true),
             P("force", "bool", "Delete even if not fully merged (default false).")],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                string name = call.RequiredString("branch").Trim();
                bool force = call.Bool("force");
                if (repo.LocalBranches.FirstOrDefault(b => b.Name == name) is not { } branch) {
                    throw ApiCall.NotFound($"No local branch \"{name}\" in {repo.Name}.");
                }
                if (branch.IsHead) {
                    throw ApiCall.Invalid($"\"{name}\" is the current branch; switch to another branch first.");
                }

                await call.ConfirmAsync("Delete branch",
                    $"Delete the local branch \"{name}\" in {repo.Name}" +
                    (force ? ", even if it has unmerged commits (they will be lost)." : "."), "Delete");
                return await RunAsync(call, "Delete branch", r => r.ExecuteAsync(() => call.Services.Git.DeleteBranchAsync(r.Path, name, force)));
            });

        yield return Command("repo.merge", "Merge a branch into the current branch. On conflicts the error explains; conflicted files appear as unmerged in repo.status.",
            [RepoParam, ProjectScopeParam, P("branch", "string", "Branch to merge in.", required: true)],
            call => {
                string branch = call.RequiredString("branch").Trim();
                return RunAsync(call, "Merge", repo => repo.CurrentBranch is null
                    ? throw ApiCall.Invalid("HEAD is detached; switch to a branch before merging.")
                    : repo.ExecuteAsync(() => call.Services.Git.MergeAsync(repo.Path, branch)));
            });

        yield return Command("repo.stage", "Stage files (including deletions), or everything with all=true.",
            [RepoParam, ProjectScopeParam,
             P("paths", "string[]", "Repository-relative paths to stage."),
             P("all", "bool", "Stage every change, including untracked files.")],
            call => {
                bool all = call.Bool("all");
                IReadOnlyList<string> paths = call.StringList("paths");
                if (!all && paths.Count == 0) {
                    throw ApiCall.Invalid("Pass \"paths\" or all=true.");
                }
                return RunAsync(call, "Stage", repo => repo.ExecuteAsync(() =>
                    all ? call.Services.Git.StageAllAsync(repo.Path) : call.Services.Git.StageAsync(repo.Path, paths)));
            });

        yield return Command("repo.unstage", "Unstage files (keeping the working-tree changes), or everything with all=true.",
            [RepoParam, ProjectScopeParam,
             P("paths", "string[]", "Repository-relative paths to unstage."),
             P("all", "bool", "Unstage everything.")],
            call => {
                bool all = call.Bool("all");
                IReadOnlyList<string> paths = call.StringList("paths");
                if (!all && paths.Count == 0) {
                    throw ApiCall.Invalid("Pass \"paths\" or all=true.");
                }
                return RunAsync(call, "Unstage", repo => {
                    bool hasHead = repo.HasHead;
                    return repo.ExecuteAsync(() => all
                        ? call.Services.Git.UnstageAllAsync(repo.Path, hasHead)
                        : call.Services.Git.UnstageAsync(repo.Path, paths, hasHead));
                });
            });

        yield return Command("repo.commit", "Commit the staged changes. Amending a commit that was already pushed asks the user to confirm.",
            [RepoParam, ProjectScopeParam,
             P("message", "string", "Commit message (may span several lines).", required: true),
             P("amend", "bool", "Replace the last commit instead (default false).")],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                string message = call.RequiredString("message").Trim();
                bool amend = call.Bool("amend");
                if (!amend && repo.Changes.Staged.Count == 0) {
                    throw ApiCall.Invalid("Nothing is staged. Stage files with repo.stage first.");
                }
                if (amend && repo.Upstream is not null && repo.Ahead == 0) {
                    await call.ConfirmAsync("Amend pushed commit",
                        $"Amend the last commit in {repo.Name}, which has already been pushed. The next push will need --force.", "Amend");
                }

                GitResult result = await repo.ExecuteAsync(() => call.Services.Git.CommitAsync(repo.Path, message, amend));
                ApiCall.EnsureSuccess(result, amend ? "Amend" : "Commit");
                CommitInfo head = (await call.Services.Git.GetLogAsync(repo.Path, "HEAD", 1))[0];
                return new { Commit = CommitDto(head), Status = RepoSummary(repo) };
            });

        yield return Command("repo.stash", "Stash the working-tree changes (including untracked files by default) and clean the working tree.",
            [RepoParam, ProjectScopeParam,
             P("message", "string", "Stash description."),
             P("includeUntracked", "bool", "Include untracked files (default true).")],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                if (!repo.IsDirty) {
                    throw ApiCall.Invalid("There are no changes to stash.");
                }
                string? message = call.String("message");
                bool untracked = call.Bool("includeUntracked", true);
                return await RunAsync(call, "Stash", r => r.ExecuteAsync(() => call.Services.Git.StashPushAsync(r.Path, message, untracked)));
            });

        yield return Command("repo.stashApply", "Apply a stash and keep it.",
            [RepoParam, ProjectScopeParam, P("stash", "string", "Stash ref (default stash@{0}).")],
            call => {
                string stash = call.String("stash") ?? "stash@{0}";
                return RunAsync(call, "Apply stash", r => r.ExecuteAsync(() => call.Services.Git.StashApplyAsync(r.Path, stash)));
            });

        yield return Command("repo.stashPop", "Apply a stash and remove it.",
            [RepoParam, ProjectScopeParam, P("stash", "string", "Stash ref (default stash@{0}).")],
            call => {
                string stash = call.String("stash") ?? "stash@{0}";
                return RunAsync(call, "Pop stash", r => r.ExecuteAsync(() => call.Services.Git.StashPopAsync(r.Path, stash)));
            });

        yield return Command("repo.stashDrop", "Delete a stash. Asks the user to confirm.",
            [RepoParam, ProjectScopeParam, P("stash", "string", "Stash ref, e.g. stash@{1}.", required: true)],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                string stashRef = call.RequiredString("stash");
                StashInfo stash = repo.Stashes.Items.FirstOrDefault(s => s.Ref == stashRef)
                                  ?? throw ApiCall.NotFound($"No stash \"{stashRef}\" in {repo.Name}.");
                await call.ConfirmAsync("Drop stash", $"Permanently delete {stash.Ref} in {repo.Name}:\n{stash.Message}", "Drop");
                return await RunAsync(call, "Drop stash", r => r.ExecuteAsync(() => call.Services.Git.StashDropAsync(r.Path, stashRef)));
            });

        // ---- UI -------------------------------------------------------------------------------

        yield return Command("ui.show", $"Bring the app to the front showing a project, repository and tab ({TabNames}).",
            [RepoParam, P("project", "string", "Project name or id."), P("tab", "string", $"One of {TabNames}.")],
            call => {
                ProjectViewModel project;
                RepoViewModel? repo = null;
                if (call.Has("repo") || !call.Has("project")) {
                    repo = call.Repo();
                    project = call.Has("project") ? call.Project() : call.Main.Projects.First(p => p.Repos.Contains(repo));
                } else {
                    project = call.Project();
                }

                call.Main.SelectedProject = project;
                if (repo is not null) {
                    project.SelectedRepo = repo;
                }
                if (call.String("tab") is { } tab) {
                    RepoViewModel target = repo ?? project.SelectedRepo
                                           ?? throw ApiCall.Invalid("The project has no repositories to show a tab for.");
                    target.SelectedTabIndex = tab.ToLowerInvariant() switch {
                        "branches" => RepoViewModel.BranchesTab,
                        "changes" => RepoViewModel.ChangesTab,
                        "history" => RepoViewModel.HistoryTab,
                        "stashes" => RepoViewModel.StashesTab,
                        _ => throw ApiCall.Invalid($"\"tab\" must be one of {TabNames}."),
                    };
                }
                call.Services.Dialogs.BringToFront();
                return Task.FromResult<object?>(new { Project = project.Name, Repo = (repo ?? project.SelectedRepo)?.Name });
            });

        yield return Command("ui.diff", "Open WinMerge for the user on one changed file: staged version vs working copy (editable), or with staged=true, HEAD vs staged version.",
            [RepoParam, ProjectScopeParam,
             P("path", "string", "Repository-relative (or absolute) path of a changed file.", required: true),
             P("staged", "bool", "Diff the staged change instead of the unstaged one (default false).")],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                string path = call.RequiredString("path");
                if (Path.IsPathRooted(path)) {
                    path = Path.GetRelativePath(repo.Path, path);
                }
                path = path.Replace('\\', '/');

                bool staged = call.Bool("staged");
                ObservableCollection<FileChange> list = staged ? repo.Changes.Staged : repo.Changes.Unstaged;
                FileChange change = list.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase))
                                    ?? throw ApiCall.NotFound($"\"{path}\" has no {(staged ? "staged" : "unstaged")} changes in {repo.Name}.");
                await (staged ? repo.Changes.OpenStagedDiffAsync(change) : repo.Changes.OpenUnstagedDiffAsync(change));
                return new { Opened = change.Path, Staged = staged };
            });

        yield return Command("ui.compareBranch", "Open WinMerge for the user comparing the current branch with another branch (all differing files).",
            [RepoParam, ProjectScopeParam,
             P("branch", "string", "Branch to compare with the current one.", required: true),
             P("mergeBase", "bool", "Compare from the merge base, i.e. only what the branch changed (default: the app setting).")],
            async call => {
                RepoViewModel repo = call.Repo();
                await repo.RefreshAsync();
                if (!repo.HasHead) {
                    throw ApiCall.Invalid("The repository has no commits yet.");
                }
                string branch = call.RequiredString("branch");
                int count = await repo.CompareWithBranchAsync(branch, call.Bool("mergeBase", call.Services.Settings.CompareFromMergeBase));
                return new { Files = count, Opened = count > 0 };
            });

        yield return Command("ui.compareCommit", "Open WinMerge for the user showing everything a commit changed.",
            [RepoParam, ProjectScopeParam, P("commit", "string", "Commit hash or revision.", required: true)],
            async call => {
                RepoViewModel repo = call.Repo();
                CommitInfo commit = await GetCommitAsync(call, repo, call.RequiredString("commit"));
                int count = await repo.History.OpenCommitComparisonAsync(commit);
                return new { Commit = commit.ShortHash, Files = count, Opened = count > 0 };
            });

        yield return Command("ui.notify", "Show a message to the user in the app's status bar and output log.",
            [P("message", "string", "Text to show.", required: true), P("error", "bool", "Show as an error (default false).")],
            call => {
                string message = $"Agent: {call.RequiredString("message")}";
                if (call.Bool("error")) {
                    call.Services.Log.Error(message);
                } else {
                    call.Services.Log.Info(message);
                }
                return Task.FromResult<object?>(null);
            });
    }

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>Defines a parameter.</summary>
    private static ApiParamInfo P(string name, string type, string description, bool required = false) =>
        new(name, type, required, description);

    /// <summary>Defines a read-only method.</summary>
    private static ApiMethodInfo Query(string name, string summary, ApiParamInfo[] ps, Func<ApiCall, Task<object?>> handler) =>
        new(name, summary, ps, ChangesState: false, handler);

    /// <summary>Defines a method that changes something.</summary>
    private static ApiMethodInfo Command(string name, string summary, ApiParamInfo[] ps, Func<ApiCall, Task<object?>> handler) =>
        new(name, summary, ps, ChangesState: true, handler);

    /// <summary>Runs a repository action; on failure returns git's message as an API error.</summary>
    private static async Task<object?> RunAsync(ApiCall call, string what, Func<RepoViewModel, Task<GitResult>> action) {
        RepoViewModel repo = call.Repo();
        GitResult result = await action(repo);
        ApiCall.EnsureSuccess(result, what);
        return new { Output = result.Message.Length > 0 ? result.Message : null, Status = RepoSummary(repo) };
    }

    /// <summary>Looks up one commit; throws not-found for an unknown revision.</summary>
    private static async Task<CommitInfo> GetCommitAsync(ApiCall call, RepoViewModel repo, string revision) {
        try {
            IReadOnlyList<CommitInfo> commits = await call.Services.Git.GetLogAsync(repo.Path, revision, 1);
            return commits.Count > 0 ? commits[0] : throw ApiCall.NotFound($"No commit \"{revision}\" in {repo.Name}.");
        } catch (GitException ex) {
            throw ApiCall.NotFound($"No commit \"{revision}\" in {repo.Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    /// <summary>A repository's branch, sync and change counts.</summary>
    private static object RepoSummary(RepoViewModel r) => new {
        r.Name,
        r.Path,
        Branch = r.CurrentBranch,
        Detached = r.CurrentBranch is null && r.HeadCommit is not null,
        Head = r.HeadCommit,
        r.Upstream,
        r.Ahead,
        r.Behind,
        Staged = r.Changes.Staged.Count,
        Unstaged = r.Changes.Unstaged.Count,
        Stashes = r.Stashes.Items.Count,
        r.Error,
    };

    /// <summary>A repository's summary plus its staged and unstaged files.</summary>
    private static object RepoDetail(RepoViewModel r) => new {
        Summary = RepoSummary(r),
        Staged = r.Changes.Staged.Select(FileDto),
        Unstaged = r.Changes.Unstaged.Select(FileDto),
    };

    /// <summary>A changed file.</summary>
    private static object FileDto(FileChange f) => new { f.Path, From = f.OriginalPath, f.Kind };

    /// <summary>A branch; tracking fields are omitted when there is no upstream.</summary>
    private static object BranchDto(BranchInfo b) => new {
        b.Name,
        Current = b.IsHead,
        b.Upstream,
        Ahead = b.Upstream is null ? (int?)null : b.Ahead,
        Behind = b.Upstream is null ? (int?)null : b.Behind,
        UpstreamGone = b.UpstreamGone ? true : (bool?)null,
        Commit = b.CommitId,
        Date = b.CommitDate,
        b.Subject,
    };

    /// <summary>A commit.</summary>
    private static object CommitDto(CommitInfo c) => new {
        c.Hash,
        c.ShortHash,
        c.Author,
        c.Date,
        c.Subject,
        c.Parents,
    };
}
