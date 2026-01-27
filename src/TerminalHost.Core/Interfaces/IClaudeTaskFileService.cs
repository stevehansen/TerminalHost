using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for reading persisted Claude Code tasks from ~/.claude/tasks/.
/// Each Claude Code session writes tasks to JSON files in a session-specific directory.
/// </summary>
public interface IClaudeTaskFileService
{
    /// <summary>
    /// Event raised when tasks are added, updated, or removed from the filesystem.
    /// </summary>
    event EventHandler? TasksChanged;

    /// <summary>
    /// Gets all tasks for a specific Claude Code session.
    /// </summary>
    /// <param name="sessionId">The Claude Code session ID (matches timeline session ID)</param>
    /// <returns>List of tasks for the session, or empty list if session not found</returns>
    IReadOnlyList<FocusTask> GetSessionTasks(string sessionId);

    /// <summary>
    /// Gets all tasks across all Claude Code sessions.
    /// </summary>
    /// <returns>List of all persisted tasks</returns>
    IReadOnlyList<FocusTask> GetAllTasks();

    /// <summary>
    /// Gets all active sessions that have task files.
    /// </summary>
    /// <returns>List of session IDs that have task directories</returns>
    IReadOnlyList<string> GetActiveSessions();

    /// <summary>
    /// Forces a refresh of all task data from disk.
    /// Normally not needed as FileSystemWatcher keeps data synced.
    /// </summary>
    void Refresh();
}
