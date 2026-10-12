namespace RepoWorkshop.Api;

/// <summary>The error part of a JSON-RPC response.</summary>
public sealed class ApiError {
    /// <summary>One of the <see cref="ApiErrorCodes"/>.</summary>
    public int Code { get; set; }

    /// <summary>A human-readable description of the error.</summary>
    public string Message { get; set; } = "";
}
