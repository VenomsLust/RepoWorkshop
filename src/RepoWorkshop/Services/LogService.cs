using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace RepoWorkshop.Services;

public enum LogLevel
{
    Info,
    Command,
    Error,
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Text)
{
    public string TimeText => Time.ToString("HH:mm:ss");

    public bool IsError => Level == LogLevel.Error;

    public bool IsCommand => Level == LogLevel.Command;
}

/// <summary>An output log of git commands and their results, safe to write from any thread.</summary>
public sealed class LogService
{
    private const int MaxEntries = 2000;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public event Action<LogEntry>? EntryAdded;

    public void Info(string text) => Add(LogLevel.Info, text);

    public void Command(string text) => Add(LogLevel.Command, text);

    public void Error(string text) => Add(LogLevel.Error, text);

    private void Add(LogLevel level, string text)
    {
        var entry = new LogEntry(DateTime.Now, level, text.TrimEnd());
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
            EntryAdded?.Invoke(entry);
        });
    }
}
