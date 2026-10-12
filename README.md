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

Settings and projects are stored in `%APPDATA%\RepoWorkshop\settings.json`. Set `REPOWORKSHOP_SETTINGS` to use a different file (handy for testing).

## Features

- **Projects** that each reference one or more repositories (add one at a time, or scan a folder).
- Per-repository status: current branch, ahead/behind upstream, working-tree changes.
- Branches: checkout (local, or a remote branch as a new tracking branch), create, delete, merge into current.
- Fetch / pull (fast-forward only) / push (sets upstream on first push).
- Project-wide actions: refresh, fetch, pull, switch all repos to a branch, create a branch in all repos.
- WinMerge integration:
  - Double-click a working-tree change to compare `HEAD` with the editable working copy.
  - *Compare with current* opens a folder comparison of every file that differs between the current branch (or the merge base) and the selected branch.
- Output log of every git command that changes state.

## Layout

```
src/RepoWorkshop/
  Models/       Plain data: settings, projects, repo refs, git results
  Services/     GitService (git CLI), WinMergeService, DiffService, SettingsStore, LogService
  ViewModels/   MainViewModel → ProjectViewModel → RepoViewModel (CommunityToolkit.Mvvm)
  Views/        Avalonia windows / user controls, dialogs
tests/RepoWorkshop.Tests/   xUnit tests (status parsing + git integration against a temp repo)
```
