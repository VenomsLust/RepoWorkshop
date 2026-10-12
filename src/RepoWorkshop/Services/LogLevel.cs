namespace RepoWorkshop.Services;

/// <summary>The kind of an output-log entry.</summary>
public enum LogLevel {
    /// <summary>A result or status message.</summary>
    Info,

    /// <summary>A command being run.</summary>
    Command,

    /// <summary>A failure.</summary>
    Error,
}
