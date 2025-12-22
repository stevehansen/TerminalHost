using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a run configuration for a project directory.
/// </summary>
public class RunConfiguration
{
    /// <summary>
    /// Unique identifier for this configuration.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for this configuration (e.g., "Development", "Production").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    /// <summary>
    /// The command to execute (e.g., "dotnet watch run", "npm run dev").
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>
    /// Whether this is the default configuration for the project.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Optional regex pattern to detect localhost URLs from output.
    /// If null, built-in patterns will be used.
    /// </summary>
    [JsonPropertyName("urlPattern")]
    public string? UrlPattern { get; set; }
}
