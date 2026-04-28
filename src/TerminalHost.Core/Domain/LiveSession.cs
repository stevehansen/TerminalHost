namespace TerminalHost.Core.Domain;

/// <summary>
/// Lightweight in-memory model for an active or recently-ended Claude Code session.
/// Not persisted — historical sessions come from ClaudeSessionIndexService.
/// </summary>
public class LiveSession
{
    /// <summary>Claude Code's session ID (from hook event).</summary>
    public string ClaudeSessionId { get; set; } = "";

    /// <summary>Working directory from the hook event.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Resolved project path (may differ from WorkingDirectory for worktrees).</summary>
    public string? ProjectPath { get; set; }

    /// <summary>Path to the JSONL transcript file.</summary>
    public string? TranscriptPath { get; set; }

    /// <summary>Matched intent ID, if any.</summary>
    public string? IntentId { get; set; }

    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }

    /// <summary>How this session ended: "explicit" (Stop hook), "timeout" (inactivity), "error".</summary>
    public string? EndReason { get; set; }

    /// <summary>Whether the session had file write activity (used for status determination).</summary>
    public bool HadFileWrites { get; set; }

    /// <summary>Whether the session had tool errors (used for status determination).</summary>
    public bool HadErrors { get; set; }

    /// <summary>Origin of the session (Local, Container, DevContainer).</summary>
    public SessionSource Source { get; set; } = SessionSource.Local;

    /// <summary>Container display name (for Container/DevContainer sessions).</summary>
    public string? ContainerName { get; set; }

    /// <summary>Whether this session is still active (no EndTime set).</summary>
    public bool IsActive => !EndTime.HasValue;

    /// <summary>Display name derived from the working directory folder name.</summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(WorkingDirectory)) return "Unknown";
            // Use Split for cross-platform safety (Linux paths on Windows host)
            var segments = WorkingDirectory.TrimEnd('/', '\\').Split('/', '\\');
            var baseName = segments.LastOrDefault(s => s.Length > 0) ?? WorkingDirectory;
            return Source == SessionSource.DevContainer ? $"{baseName} [devcontainer]" : baseName;
        }
    }

    /// <summary>Duration of the session.</summary>
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
}
