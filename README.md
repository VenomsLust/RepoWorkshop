# Repo Workshop

A desktop front end (C# / Avalonia) for managing git repositories and branches, grouped into **Projects**. Diffs open in [WinMerge](https://winmerge.org/).

## Requirements

- .NET 10 SDK
- Git for Windows on `PATH`
- WinMerge (auto-detected from the registry / Program Files; or set the path in **Settings**)

## Build & run

```bash
dotnet run --project src/RepoWorkshop
dotnet test
```

To install a Release build (app + `rw`) to `%LOCALAPPDATA%\Programs\RepoWorkshop` with Start Menu and Desktop shortcuts, and the agent skill (re-run to update; close the app first):

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```

Settings and projects are stored in `%APPDATA%\RepoWorkshop\settings.json`. Set `REPOWORKSHOP_SETTINGS` to use a different file (handy for testing).

## Features

- **Projects** that each reference one or more repositories (add one at a time, or scan a folder).
- Per-repository status: current branch, ahead/behind upstream, working-tree changes. Updates live: each repository's folder is watched, so edits, new files and branch switches made outside the app show up within a second.
- Changed-files panel beside the repository tabs: every changed file (repo-relative path; staged files in green italics), multi-select, double-click to diff, and a right-click menu with Stage, Unstage, Revert (back to the last commit), Diff and Delete (to the Recycle Bin).
- Branches: checkout (local, or a remote branch as a new tracking branch), create, delete, merge into current.
- Fetch / pull (fast-forward only) / push (sets upstream on first push).
- Changes: staged and unstaged lists, stage/unstage (Ctrl/Shift-click for several), commit, amend.
- History: the last 300 commits of the selected branch and the files each one changed.
- Stashes: stash (including untracked files), apply, pop, drop.
- Project-wide actions: refresh, fetch, pull, switch all repos to a branch, create a branch in all repos.
- WinMerge integration:
  - Double-click an unstaged file to compare the staged version with the editable working copy, or a staged file to compare `HEAD` with the staged version.
  - Double-click a commit (or a file in it) to see what it changed; double-click a stash to see its changes.
  - *Compare with current* opens a folder comparison of every file that differs between the current branch (or the merge base) and the selected branch.
- Output log of every git command that changes state.

## LLM / automation API

While the app is running it serves a local API over a named pipe (`RepoWorkshop.<username>`, current Windows user only; JSON-RPC 2.0, one message per line). The `rw` command-line client wraps it:

```bash
rw help                      # list methods; "rw help repo.commit" for parameters
rw project.status --project "AutoArmorUpgrade"
rw repo.status               # repo defaults to the one containing the current directory
rw ui.compareBranch --branch feature/x   # opens WinMerge for the user
```

- `rw` prints JSON results (errors as JSON on stderr, exit code 1) and starts the app if it isn't running.
- Destructive requests (delete branch, drop stash, remove project/repo, amend a pushed commit) show a confirmation dialog in the app. Declining returns error code -32001.
- The API can be turned off in **Settings → Automation**.
- [skills/repo-workshop/SKILL.md](skills/repo-workshop/SKILL.md) teaches an AI agent how to use it; `publish.ps1` installs it to `~/.claude/skills`. Pass `-AddToPath` to put `rw` on your PATH.
- `REPOWORKSHOP_PIPE` and `REPOWORKSHOP_SETTINGS` point a dev instance at a separate pipe and settings file.

## Layout

```
src/RepoWorkshop.Api/   Named-pipe protocol, server and client (shared by the app and rw)
src/RepoWorkshop.Cli/   rw.exe command-line client
src/RepoWorkshop/
  Automation/   API method handlers (run on the UI thread against the view models)
  Models/       Plain data: settings, projects, repo refs, git results
  Services/     GitService (git CLI), WinMergeService, DiffService, SettingsStore, LogService
  ViewModels/   MainViewModel → ProjectViewModel → RepoViewModel → Changes/History/Stashes (CommunityToolkit.Mvvm)
  Views/        Avalonia windows / user controls, dialogs
tests/RepoWorkshop.Tests/   xUnit tests (git parsing/integration, pipe protocol, rw argument parsing)
skills/repo-workshop/       SKILL.md for AI agents
```
