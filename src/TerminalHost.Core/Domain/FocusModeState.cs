using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Persisted state for focus mode.
/// </summary>
public class FocusModeState
{
    /// <summary>Whether focus mode is currently active.</summary>
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    /// <summary>ID of the current task being worked on.</summary>
    [JsonPropertyName("currentTaskId")]
    public string? CurrentTaskId { get; set; }

    /// <summary>
    /// Recently worked-on task IDs (most recent first).
    /// Used for quick task switching.
    /// </summary>
    [JsonPropertyName("taskHistory")]
    public List<string> TaskHistory { get; set; } = [];

    /// <summary>Maximum number of tasks to keep in history.</summary>
    [JsonIgnore]
    public const int MaxHistorySize = 20;

    /// <summary>
    /// Adds a task ID to the history, moving it to the front if it already exists.
    /// </summary>
    public void AddToHistory(string taskId)
    {
        // Remove if already present
        TaskHistory.Remove(taskId);

        // Add to front
        TaskHistory.Insert(0, taskId);

        // Trim to max size
        while (TaskHistory.Count > MaxHistorySize)
        {
            TaskHistory.RemoveAt(TaskHistory.Count - 1);
        }
    }
}
