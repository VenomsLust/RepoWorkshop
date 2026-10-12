using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RepoWorkshop.Api;

/// <summary>A JSON-RPC 2.0 response: either a result or an error.</summary>
public sealed class ApiResponse {
    /// <summary>The protocol version; always "2.0".</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>The id of the request this answers.</summary>
    public JsonNode? Id { get; set; }

    /// <summary>The method's result, when it succeeded.</summary>
    public JsonNode? Result { get; set; }

    /// <summary>The error, when it failed.</summary>
    public ApiError? Error { get; set; }
}
