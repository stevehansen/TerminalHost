using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a snapshot of a Claude Code task at a point in time.
/// Used to track which tasks Claude worked on during a session in the Timeline.
/// </summary>
public class ClaudeTaskSnapshot
{
    /// <summary>
    /// Task ID (from FocusTask.Id).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Task title (imperative form, e.g., "Fix authentication bug").
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Active form for progress display (e.g., "Fixing authentication bug...").
    /// </summary>
    [JsonPropertyName("activeForm")]
    public string? ActiveForm { get; set; }

    /// <summary>
    /// Task status at the time of snapshot.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FocusTaskStatus Status { get; set; }

    /// <summary>
    /// When the task was started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the task was completed.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Task elapsed time (computed from start/complete times).
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Elapsed
    {
        get
        {
            if (!CompletedAt.HasValue || !StartedAt.HasValue)
                return null;

            return CompletedAt.Value - StartedAt.Value;
        }
    }

    /// <summary>
    /// Formatted elapsed time display (e.g., "5m 23s").
    /// </summary>
    [JsonIgnore]
    public string ElapsedDisplay
    {
        get
        {
            if (!Elapsed.HasValue)
                return "";

            var ts = Elapsed.Value;
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }

    /// <summary>
    /// Creates a snapshot from a FocusTask.
    /// </summary>
    public static ClaudeTaskSnapshot FromFocusTask(FocusTask task)
    {
        return new ClaudeTaskSnapshot
        {
            Id = task.Id,
            Title = task.Title,
            ActiveForm = task.ActiveForm,
            Status = task.Status,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt
        };
    }
}
