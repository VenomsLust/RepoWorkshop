using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RepoWorkshop.Api;

/// <summary>A JSON-RPC 2.0 request.</summary>
public sealed class ApiRequest {
    /// <summary>The protocol version; always "2.0".</summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    /// <summary>Caller-chosen id, echoed back in the response.</summary>
    public JsonNode? Id { get; set; }

    /// <summary>The method to call, e.g. "repo.status".</summary>
    public string Method { get; set; } = "";

    /// <summary>Named parameters for the method.</summary>
    public JsonObject? Params { get; set; }
}
