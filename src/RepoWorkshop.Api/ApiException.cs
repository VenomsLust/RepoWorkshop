namespace RepoWorkshop.Api;

/// <summary>An API error: thrown by method handlers, and by <see cref="ApiClient"/> when a call fails.</summary>
public sealed class ApiException : Exception {
    /// <summary>Creates an error with one of the <see cref="ApiErrorCodes"/>.</summary>
    public ApiException(int code, string message) : base(message) {
        Code = code;
    }

    /// <summary>One of the <see cref="ApiErrorCodes"/>.</summary>
    public int Code { get; }
}
