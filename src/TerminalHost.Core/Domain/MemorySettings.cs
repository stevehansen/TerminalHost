using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Settings for the Eidet memory service integration.
/// All memory-specific settings (L1 count, duplicate threshold, Ollama config, etc.)
/// are managed by Eidet's own config at ~/.eidet/config.json.
/// </summary>
public class MemorySettings
{
    /// <summary>
    /// Whether the Eidet memory integration is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Eidet service URL.
    /// </summary>
    [JsonPropertyName("eidetUrl")]
    public string EidetUrl { get; set; } = "http://localhost:19380";

    /// <summary>
    /// Ensure sensible values after deserialization.
    /// </summary>
    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(EidetUrl)) EidetUrl = "http://localhost:19380";
    }
}
