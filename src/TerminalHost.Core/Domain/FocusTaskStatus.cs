namespace TerminalHost.Core.Domain;

/// <summary>
/// Status of a focus task.
/// </summary>
public enum FocusTaskStatus
{
    /// <summary>Task is in backlog, not yet started.</summary>
    NotStarted,

    /// <summary>Task is currently being worked on.</summary>
    InProgress,

    /// <summary>Task has been completed.</summary>
    Completed,

    /// <summary>Task has been paused/postponed.</summary>
    Deferred
}
