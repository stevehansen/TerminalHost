using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a Claude Code session within an intent in Timeline IDE.
/// Tracks the work done, files changed, commands executed, and agent notes.
/// </summary>
public class ClaudeSession
{
    /// <summary>
    /// Unique identifier (e.g., "session-20251226120000-abc12345").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// ID of the parent intent this session belongs to.
    /// </summary>
    [JsonPropertyName("intentId")]
    public string IntentId { get; set; } = "";

    /// <summary>
    /// ID of the parent session if this is a fork (null for first session).
    /// </summary>
    [JsonPropertyName("parentSessionId")]
    public string? ParentSessionId { get; set; }

    /// <summary>
    /// When the session started.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the session ended (null if still running).
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Current status of the session.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClaudeSessionStatus Status { get; set; } = ClaudeSessionStatus.Running;

    /// <summary>
    /// Git commit hash created by this session (if any).
    /// </summary>
    [JsonPropertyName("commitHash")]
    public string? CommitHash { get; set; }

    /// <summary>
    /// Git commit message (if committed).
    /// </summary>
    [JsonPropertyName("commitMessage")]
    public string? CommitMessage { get; set; }

    /// <summary>
    /// Files changed during this session.
    /// </summary>
    [JsonPropertyName("filesChanged")]
    public List<FileChange> FilesChanged { get; set; } = [];

    /// <summary>
    /// Shell commands executed during this session.
    /// </summary>
    [JsonPropertyName("commandsExecuted")]
    public List<string> CommandsExecuted { get; set; } = [];

    /// <summary>
    /// Agent's notes/summary of work done.
    /// </summary>
    [JsonPropertyName("agentNotes")]
    public string? AgentNotes { get; set; }

    /// <summary>
    /// Claude Code session ID for --continue functionality.
    /// </summary>
    [JsonPropertyName("continueSessionId")]
    public string? ContinueSessionId { get; set; }

    /// <summary>
    /// Initial prompt/task given to Claude Code.
    /// </summary>
    [JsonPropertyName("initialPrompt")]
    public string? InitialPrompt { get; set; }

    /// <summary>
    /// Total tokens used in this session (if available).
    /// </summary>
    [JsonPropertyName("tokensUsed")]
    public int? TokensUsed { get; set; }

    /// <summary>
    /// Cost of this session in USD (if available).
    /// </summary>
    [JsonPropertyName("costUsd")]
    public decimal? CostUsd { get; set; }

    /// <summary>
    /// Path to the Claude Code transcript file.
    /// </summary>
    [JsonPropertyName("transcriptPath")]
    public string? TranscriptPath { get; set; }

    // Computed properties

    /// <summary>
    /// Whether this session is a fork from another session.
    /// </summary>
    [JsonIgnore]
    public bool IsFork => !string.IsNullOrEmpty(ParentSessionId);

    /// <summary>
    /// Whether this session has a commit.
    /// </summary>
    [JsonIgnore]
    public bool HasCommit => !string.IsNullOrEmpty(CommitHash);

    /// <summary>
    /// Whether this session has agent notes.
    /// </summary>
    [JsonIgnore]
    public bool HasNotes => !string.IsNullOrEmpty(AgentNotes);

    /// <summary>
    /// Whether this session has any files changed.
    /// </summary>
    [JsonIgnore]
    public bool HasFilesChanged => FilesChanged.Count > 0;

    /// <summary>
    /// Whether this session has any commands executed.
    /// </summary>
    [JsonIgnore]
    public bool HasCommands => CommandsExecuted.Count > 0;

    /// <summary>
    /// Whether this session is currently running.
    /// </summary>
    [JsonIgnore]
    public bool IsRunning => Status == ClaudeSessionStatus.Running;

    /// <summary>
    /// Whether this session has completed (success or failed).
    /// </summary>
    [JsonIgnore]
    public bool IsCompleted => Status == ClaudeSessionStatus.Success || Status == ClaudeSessionStatus.Failed;

    /// <summary>
    /// Gets a display title for the session.
    /// </summary>
    [JsonIgnore]
    public string DisplayTitle => ShortDuration;

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
    /// Gets the short duration for compact display (e.g., "25m").
    /// </summary>
    [JsonIgnore]
    public string ShortDuration
    {
        get
        {
            var ts = Duration;
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h{ts.Minutes}m";
            return $"{Math.Max(1, (int)ts.TotalMinutes)}m";
        }
    }

    /// <summary>
    /// Gets the time range display (e.g., "10:45 → 11:10").
    /// </summary>
    [JsonIgnore]
    public string TimeRangeDisplay
    {
        get
        {
            var start = StartTime.ToLocalTime().ToString("HH:mm");
            var end = EndTime?.ToLocalTime().ToString("HH:mm") ?? "...";
            return $"{start} → {end}";
        }
    }

    /// <summary>
    /// Total number of files changed.
    /// </summary>
    [JsonIgnore]
    public int FileCount => FilesChanged.Count;

    /// <summary>
    /// Total lines added across all files.
    /// </summary>
    [JsonIgnore]
    public int TotalAdditions => FilesChanged.Sum(f => f.Additions);

    /// <summary>
    /// Total lines deleted across all files.
    /// </summary>
    [JsonIgnore]
    public int TotalDeletions => FilesChanged.Sum(f => f.Deletions);

    /// <summary>
    /// Gets the files summary for compact display (e.g., "3 files").
    /// </summary>
    [JsonIgnore]
    public string FilesSummary => FileCount switch
    {
        0 => "",
        1 => "1 file",
        _ => $"{FileCount} files"
    };

    /// <summary>
    /// Gets the status icon for UI display.
    /// </summary>
    [JsonIgnore]
    public string StatusIcon => Status switch
    {
        ClaudeSessionStatus.Running => "●",
        ClaudeSessionStatus.Success => "✓",
        ClaudeSessionStatus.Failed => "✗",
        ClaudeSessionStatus.Abandoned => "○",
        _ => "○"
    };

    /// <summary>
    /// Gets the status color hex for UI binding.
    /// </summary>
    [JsonIgnore]
    public string StatusColorHex => Status switch
    {
        ClaudeSessionStatus.Running => "#569CD6",  // Blue
        ClaudeSessionStatus.Success => "#4EC9B0",  // Green
        ClaudeSessionStatus.Failed => "#F14C4C",   // Red
        ClaudeSessionStatus.Abandoned => "#808080", // Gray
        _ => "#808080"
    };

    /// <summary>
    /// Gets the short commit hash for display (first 7 chars).
    /// </summary>
    [JsonIgnore]
    public string ShortCommitHash => CommitHash?.Length > 7 ? CommitHash[..7] : CommitHash ?? "";

    /// <summary>
    /// Creates a new ClaudeSession with a generated ID.
    /// </summary>
    public static ClaudeSession Create(string intentId, string? parentSessionId = null)
    {
        return new ClaudeSession
        {
            Id = $"session-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            IntentId = intentId,
            ParentSessionId = parentSessionId,
            StartTime = DateTime.UtcNow,
            Status = ClaudeSessionStatus.Running
        };
    }

    /// <summary>
    /// Marks the session as successful.
    /// </summary>
    public void MarkSuccess(string? commitHash = null, string? commitMessage = null, string? agentNotes = null)
    {
        Status = ClaudeSessionStatus.Success;
        EndTime = DateTime.UtcNow;
        CommitHash = commitHash;
        CommitMessage = commitMessage;
        if (agentNotes != null)
            AgentNotes = agentNotes;
    }

    /// <summary>
    /// Marks the session as failed.
    /// </summary>
    public void MarkFailed(string? agentNotes = null)
    {
        Status = ClaudeSessionStatus.Failed;
        EndTime = DateTime.UtcNow;
        if (agentNotes != null)
            AgentNotes = agentNotes;
    }

    /// <summary>
    /// Marks the session as abandoned.
    /// </summary>
    public void MarkAbandoned()
    {
        Status = ClaudeSessionStatus.Abandoned;
        EndTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a file change to this session.
    /// </summary>
    public void AddFileChange(string path, int additions, int deletions)
    {
        var existing = FilesChanged.FirstOrDefault(f => f.Path == path);
        if (existing != null)
        {
            existing.Additions = additions;
            existing.Deletions = deletions;
        }
        else
        {
            FilesChanged.Add(new FileChange(path, additions, deletions));
        }
    }

    /// <summary>
    /// Adds a command to the executed commands list.
    /// </summary>
    public void AddCommand(string command)
    {
        if (!string.IsNullOrWhiteSpace(command) && !CommandsExecuted.Contains(command))
            CommandsExecuted.Add(command);
    }
}
