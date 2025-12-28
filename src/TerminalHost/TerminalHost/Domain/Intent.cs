using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a development intent (goal/feature) backed by a git worktree.
/// Each intent is a swimlane in the timeline view.
/// </summary>
public class Intent
{
    /// <summary>Unique identifier (e.g., "intent-20251226120000-abc12345").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable name (e.g., "Implement user authentication").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path to the git worktree directory.</summary>
    [JsonPropertyName("worktreePath")]
    public string WorktreePath { get; set; } = string.Empty;

    /// <summary>Git branch name (e.g., "feature/auth", "issues/123").</summary>
    [JsonPropertyName("branchName")]
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Current status of the intent.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IntentStatus Status { get; set; } = IntentStatus.Active;

    /// <summary>Path to optional context file for Claude Code sessions.</summary>
    [JsonPropertyName("contextFilePath")]
    public string? ContextFilePath { get; set; }

    /// <summary>Inline context content (alternative to file).</summary>
    [JsonPropertyName("contextContent")]
    public string? ContextContent { get; set; }

    /// <summary>When the intent was created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the intent was last active.</summary>
    [JsonPropertyName("lastActiveAt")]
    public DateTime? LastActiveAt { get; set; }

    /// <summary>When the intent was completed.</summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>IDs of sessions in this intent.</summary>
    [JsonPropertyName("sessionIds")]
    public List<string> SessionIds { get; set; } = [];

    /// <summary>Total focus time spent on this intent.</summary>
    [JsonPropertyName("totalFocusTime")]
    public TimeSpan TotalFocusTime { get; set; } = TimeSpan.Zero;

    /// <summary>Path to the parent repository (for worktree management).</summary>
    [JsonPropertyName("mainRepoPath")]
    public string? MainRepoPath { get; set; }

    // Computed properties

    /// <summary>Gets the status icon.</summary>
    [JsonIgnore]
    public string StatusIcon => Status switch
    {
        IntentStatus.Active => "●",
        IntentStatus.Paused => "◐",
        IntentStatus.Completed => "✓",
        IntentStatus.Abandoned => "✗",
        _ => "○"
    };

    /// <summary>Gets the status color hex for UI binding.</summary>
    [JsonIgnore]
    public string StatusColorHex => Status switch
    {
        IntentStatus.Active => "#4EC9B0",   // Green
        IntentStatus.Paused => "#569CD6",   // Blue
        IntentStatus.Completed => "#6A9955", // Dim green
        IntentStatus.Abandoned => "#808080", // Gray
        _ => "#808080"
    };

    /// <summary>Gets a short display-friendly branch name.</summary>
    [JsonIgnore]
    public string ShortBranchName
    {
        get
        {
            if (string.IsNullOrEmpty(BranchName)) return "";

            // Convert issues/123 to #123
            if (BranchName.StartsWith("issues/"))
                return "#" + BranchName[7..];

            // Take last segment for feature/xxx or hotfix/xxx
            var lastSlash = BranchName.LastIndexOf('/');
            return lastSlash >= 0 ? BranchName[(lastSlash + 1)..] : BranchName;
        }
    }

    /// <summary>Gets formatted focus time display.</summary>
    [JsonIgnore]
    public string FocusTimeDisplay
    {
        get
        {
            if (TotalFocusTime.TotalHours >= 1)
                return $"{(int)TotalFocusTime.TotalHours}h {TotalFocusTime.Minutes}m";
            if (TotalFocusTime.TotalMinutes >= 1)
                return $"{(int)TotalFocusTime.TotalMinutes}m";
            return "< 1m";
        }
    }

    /// <summary>Whether the intent has context content.</summary>
    [JsonIgnore]
    public bool HasContext => !string.IsNullOrEmpty(ContextFilePath) || !string.IsNullOrEmpty(ContextContent);

    /// <summary>
    /// Creates a new Intent with a generated ID.
    /// </summary>
    public static Intent Create(string name, string worktreePath, string branchName, string? mainRepoPath = null)
    {
        return new Intent
        {
            Id = $"intent-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            Name = name,
            WorktreePath = worktreePath,
            BranchName = branchName,
            MainRepoPath = mainRepoPath,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
    }

    /// <summary>Marks the intent as active.</summary>
    public void Activate()
    {
        Status = IntentStatus.Active;
        LastActiveAt = DateTime.UtcNow;
    }

    /// <summary>Pauses the intent.</summary>
    public void Pause()
    {
        if (Status == IntentStatus.Completed || Status == IntentStatus.Abandoned)
            return;
        Status = IntentStatus.Paused;
    }

    /// <summary>Marks the intent as completed.</summary>
    public void Complete()
    {
        Status = IntentStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the intent as abandoned.</summary>
    public void Abandon()
    {
        Status = IntentStatus.Abandoned;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Adds focus time to the intent.</summary>
    public void AddFocusTime(TimeSpan time)
    {
        TotalFocusTime += time;
        LastActiveAt = DateTime.UtcNow;
    }

    /// <summary>Adds a session to the intent.</summary>
    public void AddSession(string sessionId)
    {
        if (!SessionIds.Contains(sessionId))
        {
            SessionIds.Add(sessionId);
            LastActiveAt = DateTime.UtcNow;
        }
    }
}
