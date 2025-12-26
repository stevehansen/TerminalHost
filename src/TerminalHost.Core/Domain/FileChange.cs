using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a file change within a Claude Code session.
/// </summary>
public class FileChange
{
    /// <summary>
    /// Relative path to the changed file.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Number of lines added.
    /// </summary>
    [JsonPropertyName("additions")]
    public int Additions { get; set; }

    /// <summary>
    /// Number of lines deleted.
    /// </summary>
    [JsonPropertyName("deletions")]
    public int Deletions { get; set; }

    /// <summary>
    /// Gets the change summary (e.g., "+95 -15").
    /// </summary>
    [JsonIgnore]
    public string ChangeSummary => $"+{Additions} -{Deletions}";

    /// <summary>
    /// Gets the total lines changed.
    /// </summary>
    [JsonIgnore]
    public int TotalChanges => Additions + Deletions;

    public FileChange() { }

    public FileChange(string path, int additions, int deletions)
    {
        Path = path;
        Additions = additions;
        Deletions = deletions;
    }
}
