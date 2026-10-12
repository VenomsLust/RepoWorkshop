using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RepoWorkshop.Api;

/// <summary>
/// Wire format: one JSON-RPC 2.0 message per line (UTF-8, newline-terminated) over a named pipe
/// that only the current Windows user can open.
/// </summary>
public static class ApiProtocol
{
    /// <summary>
    /// The pipe name is per-user, so different users on one machine never collide.
    /// REPOWORKSHOP_PIPE overrides it (for running a separate dev/test instance).
    /// </summary>
    public static string DefaultPipeName =>
        Environment.GetEnvironmentVariable("REPOWORKSHOP_PIPE") is { Length: > 0 } name ? name : $"RepoWorkshop.{Environment.UserName}";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

/// <summary>Standard JSON-RPC error codes, plus a few application-specific ones.</summary>
public static class ApiErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    /// <summary>The operation ran but failed (e.g. git returned an error).</summary>
    public const int OperationFailed = -32000;

    /// <summary>The user declined a confirmation prompt in the app.</summary>
    public const int Declined = -32001;

    /// <summary>The project or repository could not be found.</summary>
    public const int NotFound = -32002;
}

public sealed class ApiRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    public JsonNode? Id { get; set; }

    public string Method { get; set; } = "";

    public JsonObject? Params { get; set; }
}

public sealed class ApiResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    public JsonNode? Id { get; set; }

    public JsonNode? Result { get; set; }

    public ApiError? Error { get; set; }
}

public sealed class ApiError
{
    public int Code { get; set; }

    public string Message { get; set; } = "";
}

/// <summary>Thrown by method handlers to return a JSON-RPC error to the caller.</summary>
public sealed class ApiException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
