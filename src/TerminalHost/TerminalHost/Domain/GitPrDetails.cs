using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Cached details about a GitHub pull request.
/// </summary>
public class GitPrDetails
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>PR state: "open", "closed", or "merged".</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("baseBranch")]
    public string? BaseBranch { get; set; }

    [JsonPropertyName("headBranch")]
    public string? HeadBranch { get; set; }

    [JsonPropertyName("additions")]
    public int Additions { get; set; }

    [JsonPropertyName("deletions")]
    public int Deletions { get; set; }

    [JsonPropertyName("changedFiles")]
    public int ChangedFiles { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>When these details were fetched from the API.</summary>
    [JsonPropertyName("fetchedAt")]
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a compact display string for the PR stats.
    /// Example: "+150/-50 • 8 files"
    /// </summary>
    [JsonIgnore]
    public string StatsDisplay =>
        $"+{Additions}/-{Deletions} • {ChangedFiles} file{(ChangedFiles == 1 ? "" : "s")}";

    /// <summary>
    /// Gets the state with an appropriate icon.
    /// </summary>
    [JsonIgnore]
    public string StateDisplay => State.ToLowerInvariant() switch
    {
        "open" => "🟢 Open",
        "closed" => "🔴 Closed",
        "merged" => "🟣 Merged",
        _ => State
    };
}
