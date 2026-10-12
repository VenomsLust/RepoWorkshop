namespace RepoWorkshop.Cli;

/// <summary>A malformed <c>rw</c> command line.</summary>
public sealed class UsageException : Exception {
    /// <summary>Creates the error with a message describing what was wrong.</summary>
    public UsageException(string message) : base(message) {
    }
}
