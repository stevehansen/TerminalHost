using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a Claude Code session detected via hooks but not yet matched to an intent.
/// Orphan sessions are stored temporarily until an intent is created for the working directory.
/// </summary>
public class OrphanSession
{
    /// <summary>Claude Code session ID from hooks.</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Working directory where Claude Code ran.</summary>
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    /// <summary>Path to the JSONL transcript file.</summary>
    [JsonPropertyName("transcriptPath")]
    public string? TranscriptPath { get; set; }

    /// <summary>When the session started.</summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>When the session ended.</summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>List of modified file paths.</summary>
    [JsonPropertyName("filesModified")]
    public List<string> FilesModified { get; set; } = [];

    /// <summary>Whether this orphan has been assigned to an intent.</summary>
    [JsonPropertyName("isAssigned")]
    public bool IsAssigned { get; set; }

    /// <summary>ID of the ClaudeCodeSession created when assigned to an intent.</summary>
    [JsonPropertyName("assignedSessionId")]
    public string? AssignedSessionId { get; set; }

    /// <summary>
    /// Converts this orphan session to a ClaudeCodeSession when assigning to an intent.
    /// </summary>
    public ClaudeCodeSession ToClaudeCodeSession(string intentId)
    {
        var session = new ClaudeCodeSession
        {
            Id = $"session-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            IntentId = intentId,
            StartTime = StartTime,
            EndTime = EndTime,
            Status = EndTime.HasValue ? ClaudeSessionStatus.Success : ClaudeSessionStatus.Running,
            ContinueSessionId = SessionId,
            TranscriptPath = TranscriptPath
        };

        // Add file changes (without line counts - we don't have that info)
        foreach (var file in FilesModified)
        {
            session.AddFileChange(file);
        }

        return session;
    }
}
