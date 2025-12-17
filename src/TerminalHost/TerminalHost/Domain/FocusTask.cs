using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// A task in the focus mode system.
/// Tasks can be organized hierarchically and linked to git branches/PRs.
/// </summary>
public class FocusTask
{
    /// <summary>Unique identifier (e.g., "task-20251217120000-abc12345").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Short task title (required).</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Longer description (optional, can add later).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Scratch notes, meeting notes, scribbles for this task.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Parent task ID for hierarchy (null = root level).</summary>
    [JsonPropertyName("parentTaskId")]
    public string? ParentTaskId { get; set; }

    /// <summary>Associated project directories.</summary>
    [JsonPropertyName("projectPaths")]
    public List<string> ProjectPaths { get; set; } = [];

    /// <summary>Current status.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FocusTaskStatus Status { get; set; } = FocusTaskStatus.NotStarted;

    /// <summary>Priority for sorting (higher = more important).</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    /// <summary>Optional tags for categorization.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    // Timestamps
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    // Branch/PR Integration
    /// <summary>Linked git branch name (e.g., "issues/123").</summary>
    [JsonPropertyName("linkedBranch")]
    public string? LinkedBranch { get; set; }

    /// <summary>Linked PR number (e.g., "123").</summary>
    [JsonPropertyName("linkedPrNumber")]
    public string? LinkedPrNumber { get; set; }

    /// <summary>Full PR URL for quick access.</summary>
    [JsonPropertyName("linkedPrUrl")]
    public string? LinkedPrUrl { get; set; }

    /// <summary>Cached PR details (refreshed on demand).</summary>
    [JsonPropertyName("prDetails")]
    public GitPrDetails? PrDetails { get; set; }

    // Computed properties

    /// <summary>Whether this task has any PR/branch linking.</summary>
    [JsonIgnore]
    public bool HasPrLink => !string.IsNullOrEmpty(LinkedPrNumber) || !string.IsNullOrEmpty(LinkedBranch);

    /// <summary>Whether this is a root-level task (no parent).</summary>
    [JsonIgnore]
    public bool IsRootTask => string.IsNullOrEmpty(ParentTaskId);

    /// <summary>Whether the task is in a terminal state.</summary>
    [JsonIgnore]
    public bool IsTerminal => Status == FocusTaskStatus.Completed;

    /// <summary>Gets the elapsed time since the task was started.</summary>
    [JsonIgnore]
    public TimeSpan? ElapsedTime => StartedAt.HasValue
        ? (CompletedAt ?? DateTime.UtcNow) - StartedAt.Value
        : null;

    /// <summary>Gets a formatted elapsed time string.</summary>
    [JsonIgnore]
    public string ElapsedTimeDisplay
    {
        get
        {
            var elapsed = ElapsedTime;
            if (!elapsed.HasValue) return "";

            var ts = elapsed.Value;
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m";
            return "< 1m";
        }
    }

    /// <summary>Gets the status icon (simple Unicode for reliable rendering).</summary>
    [JsonIgnore]
    public string StatusIcon => Status switch
    {
        FocusTaskStatus.NotStarted => "○",
        FocusTaskStatus.InProgress => "●",
        FocusTaskStatus.Completed => "✓",
        FocusTaskStatus.Deferred => "◐",
        _ => "○"
    };

    /// <summary>Gets the status color hex for UI binding.</summary>
    [JsonIgnore]
    public string StatusColorHex => Status switch
    {
        FocusTaskStatus.NotStarted => "#808080",  // Gray
        FocusTaskStatus.InProgress => "#FFD700",  // Yellow/Gold
        FocusTaskStatus.Completed => "#4EC9B0",   // Green
        FocusTaskStatus.Deferred => "#569CD6",    // Blue
        _ => "#808080"
    };

    /// <summary>
    /// Creates a new FocusTask with a generated ID.
    /// </summary>
    public static FocusTask Create(string title, string? description = null, string? parentTaskId = null)
    {
        return new FocusTask
        {
            Id = $"task-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            Title = title,
            Description = description,
            ParentTaskId = parentTaskId,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Starts this task, setting status to InProgress.
    /// </summary>
    public void Start()
    {
        if (Status == FocusTaskStatus.Completed)
            return; // Can't start a completed task

        Status = FocusTaskStatus.InProgress;
        StartedAt ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Completes this task.
    /// </summary>
    public void Complete()
    {
        Status = FocusTaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Pauses/defers this task.
    /// </summary>
    public void Defer()
    {
        if (Status == FocusTaskStatus.Completed)
            return;

        Status = FocusTaskStatus.Deferred;
    }

    /// <summary>
    /// Returns task to backlog (not started).
    /// </summary>
    public void Reset()
    {
        Status = FocusTaskStatus.NotStarted;
        StartedAt = null;
        CompletedAt = null;
    }

    /// <summary>
    /// Gets a preview of the title (truncated if needed).
    /// </summary>
    public string GetTitlePreview(int maxLength = 50)
    {
        return Title.Length > maxLength
            ? Title[..(maxLength - 3)] + "..."
            : Title;
    }
}
