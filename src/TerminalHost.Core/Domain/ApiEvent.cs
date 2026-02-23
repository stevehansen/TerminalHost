using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// An event emitted by the API system, used by SSE streaming and webhooks.
/// </summary>
public class ApiEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"evt_{Guid.NewGuid():N}"[..12];

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("repoIndex")]
    public int? RepoIndex { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
