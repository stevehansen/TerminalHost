using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents the sessions-index.json file structure from ~/.claude/projects/{project}/.
/// Contains metadata about all Claude Code sessions for a project.
/// </summary>
public sealed class ClaudeSessionIndex
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("entries")]
    public List<ClaudeSessionIndexEntry> Entries { get; set; } = [];

    [JsonPropertyName("originalPath")]
    public string? OriginalPath { get; set; }
}

/// <summary>
/// Represents a single session entry in sessions-index.json.
/// Contains rich metadata about a Claude Code session.
/// </summary>
public sealed class ClaudeSessionIndexEntry
{
    /// <summary>
    /// Unique session identifier (GUID format).
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the session's JSONL conversation file.
    /// </summary>
    [JsonPropertyName("fullPath")]
    public string? FullPath { get; set; }

    /// <summary>
    /// File modification time as Unix timestamp in milliseconds.
    /// </summary>
    [JsonPropertyName("fileMtime")]
    public long FileMtime { get; set; }

    /// <summary>
    /// The first user prompt that started this session (truncated).
    /// Useful for session context preview.
    /// </summary>
    [JsonPropertyName("firstPrompt")]
    public string? FirstPrompt { get; set; }

    /// <summary>
    /// AI-generated summary of the session's purpose/content.
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Number of messages in the session conversation.
    /// </summary>
    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }

    /// <summary>
    /// Session creation timestamp (ISO 8601 format).
    /// </summary>
    [JsonPropertyName("created")]
    public DateTime? Created { get; set; }

    /// <summary>
    /// Session last modification timestamp (ISO 8601 format).
    /// </summary>
    [JsonPropertyName("modified")]
    public DateTime? Modified { get; set; }

    /// <summary>
    /// Git branch name at the time of the session.
    /// </summary>
    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    /// <summary>
    /// The project path this session belongs to.
    /// </summary>
    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Whether this is a sidechain session (branched from another session).
    /// </summary>
    [JsonPropertyName("isSidechain")]
    public bool IsSidechain { get; set; }

    /// <summary>
    /// Gets the file modification time as a DateTime.
    /// </summary>
    [JsonIgnore]
    public DateTime FileMtimeUtc => DateTimeOffset.FromUnixTimeMilliseconds(FileMtime).UtcDateTime;
}
