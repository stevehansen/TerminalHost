using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// An intent represents a development goal or feature in Timeline IDE.
/// Each intent is backed by a git worktree, providing isolation for parallel development.
/// </summary>
public class Intent
{
    /// <summary>
    /// Unique identifier (e.g., "intent-20251226120000-abc12345").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable name (e.g., "Implement user authentication").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Full path to the git worktree directory.
    /// </summary>
    [JsonPropertyName("worktreePath")]
    public string WorktreePath { get; set; } = "";

    /// <summary>
    /// Git branch name (e.g., "feature/auth", "hotfix/payment").
    /// </summary>
    [JsonPropertyName("branchName")]
    public string BranchName { get; set; } = "";

    /// <summary>
    /// Current status of the intent.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IntentStatus Status { get; set; } = IntentStatus.Active;

    /// <summary>
    /// Path to the intent context file (optional).
    /// When set, this file is loaded into every Claude Code session.
    /// </summary>
    [JsonPropertyName("contextFilePath")]
    public string? ContextFilePath { get; set; }

    /// <summary>
    /// Intent context content (inline, alternative to file).
    /// </summary>
    [JsonPropertyName("contextContent")]
    public string? ContextContent { get; set; }

    /// <summary>
    /// When the intent was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this intent's swimlane is expanded in the UI.
    /// </summary>
    [JsonPropertyName("isExpanded")]
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// When the intent was last worked on.
    /// </summary>
    [JsonPropertyName("lastActiveAt")]
    public DateTime? LastActiveAt { get; set; }

    /// <summary>
    /// When the intent was completed (if completed).
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// IDs of Claude Code sessions within this intent.
    /// </summary>
    [JsonPropertyName("sessionIds")]
    public List<string> SessionIds { get; set; } = [];

    /// <summary>
    /// Accumulated focus time spent on this intent.
    /// </summary>
    [JsonPropertyName("totalFocusTime")]
    public TimeSpan TotalFocusTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Path to the main repository (parent of worktree).
    /// </summary>
    [JsonPropertyName("mainRepoPath")]
    public string? MainRepoPath { get; set; }

    // Computed properties

    /// <summary>
    /// Whether this intent has context (file or inline).
    /// </summary>
    [JsonIgnore]
    public bool HasContext => !string.IsNullOrEmpty(ContextFilePath) || !string.IsNullOrEmpty(ContextContent);

    /// <summary>
    /// Gets the number of sessions in this intent.
    /// </summary>
    [JsonIgnore]
    public int SessionCount => SessionIds.Count;

    /// <summary>
    /// Whether the intent is in a terminal state (completed or abandoned).
    /// </summary>
    [JsonIgnore]
    public bool IsTerminal => Status == IntentStatus.Completed || Status == IntentStatus.Abandoned;

    /// <summary>
    /// Gets the status icon for UI display.
    /// </summary>
    [JsonIgnore]
    public string StatusIcon => Status switch
    {
        IntentStatus.Active => "●",
        IntentStatus.Paused => "◐",
        IntentStatus.Completed => "✓",
        IntentStatus.Abandoned => "✗",
        _ => "○"
    };

    /// <summary>
    /// Gets the status color hex for UI binding.
    /// </summary>
    [JsonIgnore]
    public string StatusColorHex => Status switch
    {
        IntentStatus.Active => "#4EC9B0",    // Green
        IntentStatus.Paused => "#569CD6",    // Blue
        IntentStatus.Completed => "#808080", // Gray
        IntentStatus.Abandoned => "#F14C4C", // Red
        _ => "#808080"
    };

    /// <summary>
    /// Gets the short branch name for display (e.g., "#123" for "issues/123").
    /// </summary>
    [JsonIgnore]
    public string ShortBranchName
    {
        get
        {
            if (string.IsNullOrEmpty(BranchName))
                return "";

            // Extract issue number if present
            if (BranchName.StartsWith("issues/") && BranchName.Length > 7)
                return $"#{BranchName[7..]}";
            if (BranchName.StartsWith("feature/") && BranchName.Length > 8)
                return BranchName[8..];
            if (BranchName.StartsWith("hotfix/") && BranchName.Length > 7)
                return BranchName[7..];
            if (BranchName.StartsWith("experiment/") && BranchName.Length > 11)
                return BranchName[11..];

            return BranchName;
        }
    }

    /// <summary>
    /// Gets the formatted focus time display.
    /// </summary>
    [JsonIgnore]
    public string FocusTimeDisplay
    {
        get
        {
            var ts = TotalFocusTime;
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m";
            return "< 1m";
        }
    }

    /// <summary>
    /// Creates a new Intent with a generated ID.
    /// </summary>
    public static Intent Create(string name, string branchName, string worktreePath, string? mainRepoPath = null)
    {
        return new Intent
        {
            Id = $"intent-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            Name = name,
            BranchName = branchName,
            WorktreePath = worktreePath,
            MainRepoPath = mainRepoPath,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Marks the intent as active and updates last active time.
    /// </summary>
    public void Activate()
    {
        Status = IntentStatus.Active;
        LastActiveAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Pauses the intent.
    /// </summary>
    public void Pause()
    {
        if (!IsTerminal)
            Status = IntentStatus.Paused;
    }

    /// <summary>
    /// Completes the intent.
    /// </summary>
    public void Complete()
    {
        Status = IntentStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Abandons the intent.
    /// </summary>
    public void Abandon()
    {
        Status = IntentStatus.Abandoned;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds focus time to this intent.
    /// </summary>
    public void AddFocusTime(TimeSpan duration)
    {
        TotalFocusTime += duration;
        LastActiveAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a session ID to this intent.
    /// </summary>
    public void AddSession(string sessionId)
    {
        if (!SessionIds.Contains(sessionId))
            SessionIds.Add(sessionId);
    }
}
