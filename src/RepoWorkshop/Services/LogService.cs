using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace RepoWorkshop.Services;

/// <summary>An output log of git commands and their results, safe to write from any thread.</summary>
public sealed class LogService {
    /// <summary>Oldest entries are dropped beyond this many.</summary>
    private const int MaxEntries = 2000;

    /// <summary>Raised on the UI thread after an entry is added.</summary>
    public event Action<LogEntry>? EntryAdded;

    /// <summary>The log, oldest first. Only modified on the UI thread.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>Logs an informational message.</summary>
    public void Info(string text) => Add(LogLevel.Info, text);

    /// <summary>Logs a command being run.</summary>
    public void Command(string text) => Add(LogLevel.Command, text);

    /// <summary>Logs an error.</summary>
    public void Error(string text) => Add(LogLevel.Error, text);

    /// <summary>Adds an entry on the UI thread.</summary>
    private void Add(LogLevel level, string text) {
        LogEntry entry = new(DateTime.Now, level, text.TrimEnd());
        Dispatcher.UIThread.Post(() => {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries) {
                Entries.RemoveAt(0);
            }
            EntryAdded?.Invoke(entry);
        });
    }
}
