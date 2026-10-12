namespace RepoWorkshop.Models;

/// <summary>The outcome of running a git command.</summary>
/// <param name="ExitCode">git's exit code; 0 means success.</param>
/// <param name="StdOut">Standard output.</param>
/// <param name="StdErr">Standard error.</param>
public sealed record GitResult(int ExitCode, string StdOut, string StdErr) {
    /// <summary>Whether the command succeeded.</summary>
    public bool Success => ExitCode == 0;

    /// <summary>The most useful message to show the user for this result.</summary>
    public string Message => (string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr).Trim();
}
