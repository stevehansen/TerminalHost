using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Configuration for a single webhook endpoint.
/// </summary>
public class WebhookEndpoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new() { "*" };

    [JsonPropertyName("debounceMs")]
    public int DebounceMs { get; set; } = 500;

    [JsonPropertyName("batchWindowMs")]
    public int BatchWindowMs { get; set; } = 0;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("templatePath")]
    public string? TemplatePath { get; set; }
}
