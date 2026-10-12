---
name: repo-workshop
description: Use when working with the user's git repositories through Repo Workshop, their desktop app that groups repos into Projects. Covers listing projects and repos, status across a project, branch/stage/commit/stash/push operations, project-wide branch actions (create or switch a branch in every repo), and opening WinMerge so the user can review diffs. Use it whenever the user mentions Repo Workshop, a project by name, "all repos in the project", or wants to see or review changes in WinMerge.
---

# Repo Workshop

Repo Workshop is the user's Windows app for managing git repositories grouped into **Projects** (a project references one or more repos). You control it with the `rw` command, which talks to the running app over a local named pipe. Everything you do through `rw` shows up in the app's UI and its Output log, so the user can follow along.

## Running `rw`

`rw.exe` is installed at `%LOCALAPPDATA%\Programs\RepoWorkshop\rw.exe` (it may also be on PATH as `rw`). From bash:

```bash
RW="$LOCALAPPDATA/Programs/RepoWorkshop/rw.exe"
"$RW" project.list
```

From PowerShell: `& "$env:LOCALAPPDATA\Programs\RepoWorkshop\rw.exe" project.list`

- `rw` starts the app if it isn't running (pass `--no-launch` to prevent that).
- Syntax: `rw <method> --name value ...`. Booleans: `--flag` (true) or `--flag false`. Lists: repeat the option (`--paths a.txt --paths b.txt`). Or pass `--json '{"paths":["a.txt"]}'`.
- `--repo` accepts a repo name, alias or path. **If omitted, it defaults to the registered repo containing the current directory**, so `cd` into a repo and skip it.
- Results are JSON on stdout (exit code 0). Errors are JSON on stderr: `{"error":{"code":...,"message":"..."}}` with exit code 1. Exit code 3 means the app couldn't be reached.
- **Multi-line text** (commit messages): use `--message-file -` with a here-doc, or `--message-file msg.txt`, instead of `--message "..."`. Shell quoting, especially in Windows PowerShell 5.1, can drop double quotes or split arguments. `--json-file <path|->` does the same for any parameters.

  ```bash
  "$RW" repo.commit --repo R --message-file - <<'EOF'
  Subject line

  Body with "quotes" and more lines.
  EOF
  ```
- `rw help` lists every method; `rw help <method>` shows its parameters. Check it rather than guessing parameter names: unknown parameters are rejected.

## Start here

1. `rw project.list`: the projects and their repos (names and paths).
2. `rw project.status --project "<name>"`: branch, ahead/behind, staged/unstaged counts and stash count for every repo in the project.
3. `rw repo.status --repo <name>`: one repo in detail, including the staged and unstaged file lists.

## Common tasks

| Goal | Command |
|---|---|
| Feature branch across a whole project | `rw project.createBranch --project P --branch feature/x` |
| Switch every repo that has a branch | `rw project.switch --project P --branch main` |
| Fetch / pull everything | `rw project.fetch --project P`, `rw project.pull --project P` (fast-forward only) |
| Branches of a repo | `rw repo.branches --repo R` (add `--remote false` for local only) |
| History | `rw repo.log --repo R --ref main --max 20`; one commit: `rw repo.show --repo R --commit <hash>` |
| Stage and commit | `rw repo.stage --repo R --paths src/a.cs` (or `--all`), then `rw repo.commit --repo R --message "..."` |
| Unstage | `rw repo.unstage --repo R --paths src/a.cs` (or `--all`); the working-tree change is kept |
| Read a diff | `rw repo.diff --repo R` (unstaged), `--staged`, `--from main [--to feature]`, `--commit <hash>`, `--paths a.cs` |
| Discard changes | `rw repo.revert --repo R --paths src/a.cs` (asks the user; new files are only unstaged, untracked files skipped) |
| Branch work | `rw repo.checkout`, `rw repo.createBranch --branch x [--from main]`, `rw repo.merge --branch x` |
| Stash | `rw repo.stash --message "wip"`, `rw repo.stashes`, `rw repo.stashPop` |
| Push | `rw repo.push --repo R` (sets the upstream on first push) |

Project-wide methods return a `results` list with an `outcome` of `succeeded`, `failed` or `skipped` per repo. Report any failures or skips to the user rather than assuming everything worked.

## Showing changes to the user

When the user wants to **see or review** changes, open WinMerge for them instead of pasting large diffs into the chat:

- `rw ui.diff --repo R --path src/a.cs`: one file, staged version vs working copy (editable in WinMerge). Add `--staged` for HEAD vs staged.
- `rw ui.compareBranch --repo R --branch feature/x`: every file that differs between the current branch and another. By default this compares from the merge base, so it shows only what that branch changed.
- `rw ui.compareCommit --repo R --commit <hash>`: everything a commit changed.
- `rw ui.show --repo R --tab changes`: bring the app forward on a repo and tab (`branches`, `changes`, `history`, `stashes`).
- `rw ui.notify --message "Done: created feature/x in 4 repos"`: leave the user a note in the app's status bar.

To read a diff yourself, use `rw repo.diff`. It returns unified diff text, cut off at `maxChars` (default 100,000) with `truncated: true` when it's larger; narrow it with `--paths`. Untracked files aren't included.

## Merge conflicts

Conflicted files appear in `repo.status` as `"kind": "unmerged"`.

- To let the user resolve them, run `rw ui.resolveConflict --repo R --path <file>`. It opens WinMerge's three-way merge (ours | base | theirs); saving the middle pane writes the resolved file.
- Once a file is resolved (by the user, or by you editing out the conflict markers), stage it with `rw repo.stage --paths <file>` to mark it resolved. Then commit to finish the merge.
- Don't resolve conflicts yourself unless the user asks; show them with `ui.resolveConflict` instead.

## Confirmations and safety

These methods make the app ask the user to approve in a dialog: `repo.revert`, `repo.deleteBranch`, `repo.stashDrop`, `project.delete`, `project.removeRepo`, and `repo.commit --amend` on an already-pushed commit. The call blocks until they answer.

- Error code **-32001** means **the user declined**. Do not retry or look for a workaround; tell the user and ask how they'd like to proceed.
- Only push, merge, delete or amend when the user has asked for it. Creating branches, staging and committing as part of a requested task is fine.
- Pull is fast-forward only. If it fails because branches have diverged, report it; don't force anything.

## Using plain git alongside `rw`

`rw` covers the everyday operations. For anything else (rebase, cherry-pick, reset, reading diffs, blame), use `git` directly in the repo. Then run `rw repo.refresh --repo R` (or `rw repo.refresh --project P`) so the app's view catches up.

## Error codes

| Code | Meaning |
|---|---|
| -32001 | The user declined a confirmation |
| -32002 | Project, repo, branch, stash or commit not found (the message lists what exists) |
| -32602 | Bad or missing parameter (e.g. nothing staged to commit) |
| -32601 | Unknown method; run `rw help` |
| -32000 | The git operation failed; the message contains git's output |
