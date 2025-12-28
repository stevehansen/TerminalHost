using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a Claude Code session within an intent.
/// Displayed as a block on the timeline.
/// </summary>
public class ClaudeCodeSession
{
    /// <summary>Unique session identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Parent intent ID.</summary>
    [JsonPropertyName("intentId")]
    public string IntentId { get; set; } = string.Empty;

    /// <summary>Parent session ID (null for first session, set for forks).</summary>
    [JsonPropertyName("parentSessionId")]
    public string? ParentSessionId { get; set; }

    /// <summary>When the session started.</summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>When the session ended (null if still running).</summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>Current status of the session.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClaudeSessionStatus Status { get; set; } = ClaudeSessionStatus.Running;

    /// <summary>Git commit hash (if session resulted in a commit).</summary>
    [JsonPropertyName("commitHash")]
    public string? CommitHash { get; set; }

    /// <summary>Git commit message.</summary>
    [JsonPropertyName("commitMessage")]
    public string? CommitMessage { get; set; }

    /// <summary>Files changed during this session.</summary>
    [JsonPropertyName("filesChanged")]
    public List<TimelineFileChange> FilesChanged { get; set; } = [];

    /// <summary>Bash commands executed during this session.</summary>
    [JsonPropertyName("commandsExecuted")]
    public List<string> CommandsExecuted { get; set; } = [];

    /// <summary>AI agent's summary/notes about what was done.</summary>
    [JsonPropertyName("agentNotes")]
    public string? AgentNotes { get; set; }

    /// <summary>Claude Code session ID for --continue flag.</summary>
    [JsonPropertyName("continueSessionId")]
    public string? ContinueSessionId { get; set; }

    /// <summary>Initial prompt/task given to Claude.</summary>
    [JsonPropertyName("initialPrompt")]
    public string? InitialPrompt { get; set; }

    /// <summary>Number of tokens used (optional tracking).</summary>
    [JsonPropertyName("tokensUsed")]
    public int? TokensUsed { get; set; }

    /// <summary>Estimated cost in USD (optional tracking).</summary>
    [JsonPropertyName("costUsd")]
    public decimal? CostUsd { get; set; }

    /// <summary>Path to the JSONL transcript file.</summary>
    [JsonPropertyName("transcriptPath")]
    public string? TranscriptPath { get; set; }

    // Computed properties

    /// <summary>Whether this session is a fork.</summary>
    [JsonIgnore]
    public bool IsFork => !string.IsNullOrEmpty(ParentSessionId);

    /// <summary>Gets the session duration.</summary>
    [JsonIgnore]
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;

    /// <summary>Gets a short duration string.</summary>
    [JsonIgnore]
    public string ShortDuration
    {
        get
        {
            var d = Duration;
            if (d.TotalHours >= 1)
                return $"{(int)d.TotalHours}h {d.Minutes}m";
            if (d.TotalMinutes >= 1)
                return $"{(int)d.TotalMinutes}m";
            return "< 1m";
        }
    }

    /// <summary>Gets the time range display string.</summary>
    [JsonIgnore]
    public string TimeRangeDisplay
    {
        get
        {
            var start = StartTime.ToLocalTime().ToString("HH:mm");
            var end = EndTime?.ToLocalTime().ToString("HH:mm") ?? "now";
            return $"{start} - {end}";
        }
    }

    /// <summary>Gets the number of files changed.</summary>
    [JsonIgnore]
    public int FileCount => FilesChanged.Count;

    /// <summary>Gets total additions across all files.</summary>
    [JsonIgnore]
    public int TotalAdditions => FilesChanged.Sum(f => f.Additions);

    /// <summary>Gets total deletions across all files.</summary>
    [JsonIgnore]
    public int TotalDeletions => FilesChanged.Sum(f => f.Deletions);

    /// <summary>Gets a summary of file changes.</summary>
    [JsonIgnore]
    public string FilesSummary => FileCount > 0 ? $"{FileCount} files (+{TotalAdditions} -{TotalDeletions})" : "No files";

    /// <summary>Gets the status icon.</summary>
    [JsonIgnore]
    public string StatusIcon => Status switch
    {
        ClaudeSessionStatus.Running => "▶",
        ClaudeSessionStatus.Success => "✓",
        ClaudeSessionStatus.Failed => "✗",
        ClaudeSessionStatus.Abandoned => "◌",
        _ => "○"
    };

    /// <summary>Gets the status color hex for UI binding.</summary>
    [JsonIgnore]
    public string StatusColorHex => Status switch
    {
        ClaudeSessionStatus.Running => "#569CD6",   // Blue
        ClaudeSessionStatus.Success => "#4EC9B0",   // Green
        ClaudeSessionStatus.Failed => "#F14C4C",    // Red
        ClaudeSessionStatus.Abandoned => "#808080", // Gray
        _ => "#808080"
    };

    /// <summary>Gets the short commit hash (first 7 chars).</summary>
    [JsonIgnore]
    public string ShortCommitHash => CommitHash?.Length > 7 ? CommitHash[..7] : CommitHash ?? "";

    /// <summary>Whether the session has a commit.</summary>
    [JsonIgnore]
    public bool HasCommit => !string.IsNullOrEmpty(CommitHash);

    /// <summary>Whether the session has notes.</summary>
    [JsonIgnore]
    public bool HasNotes => !string.IsNullOrEmpty(AgentNotes);

    /// <summary>Whether the session has file changes.</summary>
    [JsonIgnore]
    public bool HasFilesChanged => FilesChanged.Count > 0;

    /// <summary>Whether the session has commands.</summary>
    [JsonIgnore]
    public bool HasCommands => CommandsExecuted.Count > 0;

    /// <summary>Whether the session is currently running.</summary>
    [JsonIgnore]
    public bool IsRunning => Status == ClaudeSessionStatus.Running;

    /// <summary>Whether the session is completed (success or failed).</summary>
    [JsonIgnore]
    public bool IsCompleted => Status == ClaudeSessionStatus.Success || Status == ClaudeSessionStatus.Failed;

    /// <summary>Gets a display title for the session.</summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            // Priority: commit message > agent notes > initial prompt > fallback
            if (!string.IsNullOrEmpty(CommitMessage))
                return Truncate(CommitMessage, 50);
            if (!string.IsNullOrEmpty(AgentNotes))
                return Truncate(AgentNotes, 50);
            if (!string.IsNullOrEmpty(InitialPrompt))
                return Truncate(InitialPrompt, 50);
            return "Claude Code";
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Get first line
        var firstLine = text.Split('\n')[0].Trim();
        return firstLine.Length > maxLength ? firstLine[..(maxLength - 3)] + "..." : firstLine;
    }

    /// <summary>
    /// Creates a new ClaudeCodeSession with a generated ID.
    /// </summary>
    public static ClaudeCodeSession Create(string intentId, string? parentSessionId = null)
    {
        return new ClaudeCodeSession
        {
            Id = $"session-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            IntentId = intentId,
            ParentSessionId = parentSessionId,
            StartTime = DateTime.UtcNow,
            Status = ClaudeSessionStatus.Running
        };
    }

    /// <summary>Marks the session as successful.</summary>
    public void MarkSuccess(string? commitHash = null, string? commitMessage = null)
    {
        Status = ClaudeSessionStatus.Success;
        EndTime = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(commitHash))
            CommitHash = commitHash;
        if (!string.IsNullOrEmpty(commitMessage))
            CommitMessage = commitMessage;
    }

    /// <summary>Marks the session as failed.</summary>
    public void MarkFailed()
    {
        Status = ClaudeSessionStatus.Failed;
        EndTime = DateTime.UtcNow;
    }

    /// <summary>Marks the session as abandoned.</summary>
    public void MarkAbandoned()
    {
        Status = ClaudeSessionStatus.Abandoned;
        EndTime = DateTime.UtcNow;
    }

    /// <summary>Adds a file change to the session.</summary>
    public void AddFileChange(string path, int additions = 0, int deletions = 0)
    {
        var existing = FilesChanged.FirstOrDefault(f => f.Path == path);
        if (existing != null)
        {
            existing.Additions += additions;
            existing.Deletions += deletions;
        }
        else
        {
            FilesChanged.Add(new TimelineFileChange
            {
                Path = path,
                Additions = additions,
                Deletions = deletions
            });
        }
    }

    /// <summary>Adds a command to the session.</summary>
    public void AddCommand(string command)
    {
        if (!string.IsNullOrEmpty(command) && !CommandsExecuted.Contains(command))
        {
            CommandsExecuted.Add(command);
        }
    }
}
