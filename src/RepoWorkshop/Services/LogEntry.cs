using System;

namespace RepoWorkshop.Services;

/// <summary>One line of the output log.</summary>
/// <param name="Time">When it was logged.</param>
/// <param name="Level">What kind of entry it is.</param>
/// <param name="Text">The message.</param>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Text) {
    /// <summary>The time, for display.</summary>
    public string TimeText => Time.ToString("HH:mm:ss");

    /// <summary>Whether this is an error.</summary>
    public bool IsError => Level == LogLevel.Error;

    /// <summary>Whether this is a command being run.</summary>
    public bool IsCommand => Level == LogLevel.Command;
}
