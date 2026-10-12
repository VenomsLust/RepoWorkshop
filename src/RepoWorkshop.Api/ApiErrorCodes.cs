namespace RepoWorkshop.Api;

/// <summary>Standard JSON-RPC error codes, plus a few application-specific ones.</summary>
public static class ApiErrorCodes {
    /// <summary>The request was not valid JSON.</summary>
    public const int ParseError = -32700;

    /// <summary>The request was JSON but not a valid request (e.g. no method).</summary>
    public const int InvalidRequest = -32600;

    /// <summary>The method does not exist.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>A parameter was missing, unknown or of the wrong type.</summary>
    public const int InvalidParams = -32602;

    /// <summary>An unexpected error inside the app.</summary>
    public const int InternalError = -32603;

    /// <summary>The operation ran but failed (e.g. git returned an error).</summary>
    public const int OperationFailed = -32000;

    /// <summary>The user declined a confirmation prompt in the app.</summary>
    public const int Declined = -32001;

    /// <summary>The project, repository or other named item could not be found.</summary>
    public const int NotFound = -32002;
}
