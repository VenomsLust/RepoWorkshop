# Repo Workshop

C# / .NET 10 / Avalonia 12 desktop app for managing git repositories grouped into projects, with WinMerge for diffs and a named-pipe API (`rw` CLI) for AI agents. See README.md for features and layout.

## Build and test

```bash
dotnet build RepoWorkshop.slnx
dotnet test RepoWorkshop.slnx
```

The build must stay at **zero warnings**: `.editorconfig` + `Directory.Build.props` turn the style rules below (and missing XML docs) into build warnings.

For a dev instance that doesn't touch the user's installed copy, set `REPOWORKSHOP_SETTINGS` (settings file) and `REPOWORKSHOP_PIPE` (API pipe name) before launching.

## Code conventions

- **One type per file**: class, record, enum or interface; the file is named after the type. Partial classes may span files (e.g. `ApiHost.cs` + `ApiHost.Methods.cs`).
- **Braces at the end of the line** (K&R), including `} else {`, `} catch {`, lambdas and initializers.
- **Always brace** control-flow bodies, even one-liners.
- **Explicit types**, never `var` (target-typed `new()` is fine: `List<string> items = [];`, `Foo foo = new(...);`).
- **Comments**: a lean `/// <summary>` on every class, constructor, property and method, and a very brief one on every field. Positional records document their parameters with `<param>` tags.
- **Member order**: constants, static fields, instance fields, events, constructors, properties, methods, event hooks.
  - Event hooks are event handlers (`OnXxx(object? sender, ...)`) and CommunityToolkit `partial void OnXxxChanged` methods.
  - One-line lambda hooks can stay inline (`x.Changed += (_, _) => Update();`); multi-line ones become named event-hook methods.
- Prefer explicit constructors and fields over primary constructors for classes.

## Gotchas

- Processes started from the Claude desktop app get a sandboxed `%APPDATA%` (MSIX virtualization). Never edit the user's settings file directly; use `rw` against their running app. `rw` launches the app via `explorer.exe` for this reason.
- Destructive API calls show a confirmation dialog on the user's screen; warn them before testing those.
