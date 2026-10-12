using System;
using System.IO;
using System.Threading;

namespace RepoWorkshop.Services;

/// <summary>
/// Watches a working tree for file changes and raises <see cref="Changed"/> once things go quiet,
/// so a burst of changes (a build, a checkout) causes a single refresh.
/// </summary>
public sealed class RepoWatcher : IDisposable {
    /// <summary>How long changes must stop before <see cref="Changed"/> is raised.</summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);

    /// <summary>The OS file watcher.</summary>
    private readonly FileSystemWatcher _watcher;

    /// <summary>Fires <see cref="Changed"/> after the quiet period.</summary>
    private readonly Timer _debounce;

    /// <summary>Set once disposed, so a pending timer doesn't raise the event.</summary>
    private volatile bool _disposed;

    /// <summary>Raised on a thread-pool thread after relevant changes settle.</summary>
    public event Action? Changed;

    /// <summary>Starts watching <paramref name="repoPath"/> (which must exist) and everything below it.</summary>
    public RepoWatcher(string repoPath) {
        _debounce = new Timer(_ => RaiseChanged(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(repoPath) {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.Error += (_, _) => Schedule(); // Buffer overflow: we may have missed events, so just refresh.
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Whether a change at <paramref name="relativePath"/> (relative to the repository root) can affect
    /// what the app shows. Working-tree files always can; inside .git only HEAD, the index and refs matter.
    /// </summary>
    public static bool IsRelevant(string relativePath) {
        string path = relativePath.Replace('\\', '/');
        if (path != ".git" && !path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (path.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        string inside = path.Length > 5 ? path[5..] : "";
        return inside is "HEAD" or "index" or "packed-refs" or "MERGE_HEAD" or "ORIG_HEAD"
               || inside.StartsWith("refs/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stops watching.</summary>
    public void Dispose() {
        _disposed = true;
        _watcher.Dispose();
        _debounce.Dispose();
    }

    /// <summary>(Re)starts the quiet-period timer.</summary>
    private void Schedule() {
        if (!_disposed) {
            _debounce.Change(QuietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Raises <see cref="Changed"/> unless disposed.</summary>
    private void RaiseChanged() {
        if (!_disposed) {
            Changed?.Invoke();
        }
    }

    /// <summary>Schedules a refresh for relevant changes; renames count if either name is relevant.</summary>
    private void OnFileEvent(object sender, FileSystemEventArgs e) {
        bool relevant = IsRelevant(e.Name ?? "")
                        || (e is RenamedEventArgs renamed && IsRelevant(renamed.OldName ?? ""));
        if (relevant) {
            Schedule();
        }
    }
}
