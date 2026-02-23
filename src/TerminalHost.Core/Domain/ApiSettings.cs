using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Settings for the REST API, SSE streaming, and webhook system.
/// </summary>
public class ApiSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 19280;

    [JsonPropertyName("bindAddress")]
    public string BindAddress { get; set; } = "127.0.0.1";

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("enableSse")]
    public bool EnableSse { get; set; } = true;

    [JsonPropertyName("enableWebhooks")]
    public bool EnableWebhooks { get; set; } = false;

    [JsonPropertyName("webhooks")]
    public List<WebhookEndpoint> Webhooks { get; set; } = new();

    [JsonPropertyName("corsOrigins")]
    public List<string> CorsOrigins { get; set; } = new() { "http://localhost:*" };
}
