using System;
using RepoWorkshop.Models;

namespace RepoWorkshop.Services;

/// <summary>A git command that had to succeed failed.</summary>
public sealed class GitException : Exception {
    /// <summary>Creates the error from the failed command's result.</summary>
    public GitException(GitResult result) : base(result.Message) {
        Result = result;
    }

    /// <summary>The failed command's result.</summary>
    public GitResult Result { get; }
}
