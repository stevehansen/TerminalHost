using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a file modification tracked in a Claude session.
/// </summary>
public class TimelineFileChange
{
    /// <summary>Relative file path.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Number of lines added.</summary>
    [JsonPropertyName("additions")]
    public int Additions { get; set; }

    /// <summary>Number of lines deleted.</summary>
    [JsonPropertyName("deletions")]
    public int Deletions { get; set; }

    /// <summary>Gets the change summary (e.g., "+95 -15").</summary>
    [JsonIgnore]
    public string ChangeSummary => $"+{Additions} -{Deletions}";

    /// <summary>Gets the total number of changes.</summary>
    [JsonIgnore]
    public int TotalChanges => Additions + Deletions;

    /// <summary>Gets the file name without path.</summary>
    [JsonIgnore]
    public string FileName => System.IO.Path.GetFileName(Path);
}
