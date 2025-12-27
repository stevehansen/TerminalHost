using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a Claude Code session that was detected via hooks but doesn't
/// have a matching intent. These sessions are preserved so they can be
/// retroactively associated with an intent.
/// </summary>
public class OrphanSession
{
    /// <summary>
    /// Claude Code session ID (from hooks).
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Working directory where Claude Code was running.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    /// <summary>
    /// Path to the Claude Code transcript file (for extracting details later).
    /// </summary>
    [JsonPropertyName("transcriptPath")]
    public string? TranscriptPath { get; set; }

    /// <summary>
    /// When the session started (from SessionStart hook).
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// When the session ended (from Stop hook), null if still running.
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Files modified during the session (from PostToolUse hooks).
    /// </summary>
    [JsonPropertyName("filesModified")]
    public List<string> FilesModified { get; set; } = [];

    /// <summary>
    /// Whether this session has been assigned to an intent.
    /// </summary>
    [JsonPropertyName("isAssigned")]
    public bool IsAssigned { get; set; }

    /// <summary>
    /// The ID of the ClaudeSession created when this orphan was assigned.
    /// </summary>
    [JsonPropertyName("assignedSessionId")]
    public string? AssignedSessionId { get; set; }

    /// <summary>
    /// Whether the session is still running (no Stop event received).
    /// </summary>
    [JsonIgnore]
    public bool IsRunning => !EndTime.HasValue;

    /// <summary>
    /// Duration of the session.
    /// </summary>
    [JsonIgnore]
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;

    /// <summary>
    /// Gets the formatted duration display.
    /// </summary>
    [JsonIgnore]
    public string DurationDisplay
    {
        get
        {
            var ts = Duration;
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m";
            return "< 1m";
        }
    }

    /// <summary>
    /// Gets a short display name for the session (folder name from cwd).
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(Cwd))
                return "Unknown";
            return Path.GetFileName(Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "Unknown";
        }
    }

    /// <summary>
    /// Adds a file to the modified list if not already present.
    /// </summary>
    public void AddFile(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath) && !FilesModified.Contains(filePath))
            FilesModified.Add(filePath);
    }

    /// <summary>
    /// Converts this orphan session to a ClaudeSession for the given intent.
    /// </summary>
    public ClaudeSession ToClaudeSession(string intentId)
    {
        var session = new ClaudeSession
        {
            Id = $"session-{StartTime:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            IntentId = intentId,
            ContinueSessionId = SessionId,
            StartTime = StartTime,
            EndTime = EndTime,
            Status = EndTime.HasValue ? ClaudeSessionStatus.Success : ClaudeSessionStatus.Running
        };

        // Add files with zero line counts (can be updated later via git diff)
        foreach (var file in FilesModified)
        {
            session.AddFileChange(file, 0, 0);
        }

        return session;
    }
}
