using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoWorkshop.Api;

/// <summary>
/// Wire format: one JSON-RPC 2.0 message per line (UTF-8, newline-terminated) over a named pipe
/// that only the current Windows user can open.
/// </summary>
public static class ApiProtocol {
    /// <summary>Serializer settings for every message on the pipe.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// The per-user pipe name, so different users on one machine never collide.
    /// REPOWORKSHOP_PIPE overrides it (for running a separate dev/test instance).
    /// </summary>
    public static string DefaultPipeName =>
        Environment.GetEnvironmentVariable("REPOWORKSHOP_PIPE") is { Length: > 0 } name ? name : $"RepoWorkshop.{Environment.UserName}";
}
