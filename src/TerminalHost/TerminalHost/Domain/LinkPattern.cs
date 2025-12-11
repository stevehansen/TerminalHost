using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Defines a pattern for detecting clickable links in terminal output.
/// </summary>
public class LinkPattern
{
    /// <summary>
    /// Unique identifier for this pattern.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name for this pattern (shown in settings).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Regular expression pattern to match. Use capturing groups for URL template substitution.
    /// Example: "FD-(\d+)" to match ticket numbers like "FD-123"
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    /// <summary>
    /// URL template with placeholders for captured groups.
    /// Use $0 for full match, $1, $2, etc. for captured groups.
    /// Example: "https://ticketing.example.com/ticket/$1"
    /// </summary>
    [JsonPropertyName("urlTemplate")]
    public string UrlTemplate { get; set; } = "";

    /// <summary>
    /// Whether this pattern is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Priority for pattern matching (higher = matched first). Default is 0.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;
}
