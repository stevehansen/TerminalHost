using System;
using System.Collections.Generic;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for detecting and tracking Claude Code tasks from terminal output.
/// Monitors terminal sessions for task-related messages and creates corresponding FocusTask entries.
/// </summary>
public interface IClaudeTaskDetectionService
{
    /// <summary>
    /// Start monitoring a terminal session for Claude task output.
    /// </summary>
    /// <param name="sessionId">The terminal session ID to monitor.</param>
    void StartMonitoring(Guid sessionId);

    /// <summary>
    /// Stop monitoring a terminal session.
    /// </summary>
    /// <param name="sessionId">The terminal session ID to stop monitoring.</param>
    void StopMonitoring(Guid sessionId);

    /// <summary>
    /// Process a line of terminal output for task detection.
    /// Called by terminal output event handlers.
    /// </summary>
    /// <param name="line">The output line to process.</param>
    /// <param name="sessionId">The terminal session ID that produced the output.</param>
    void ProcessOutput(string line, Guid sessionId);

    /// <summary>
    /// Get all detected Claude tasks for a specific session.
    /// </summary>
    /// <param name="sessionId">The terminal session ID.</param>
    /// <returns>Read-only list of Claude tasks detected from this session.</returns>
    IReadOnlyList<FocusTask> GetClaudeTasks(Guid sessionId);

    /// <summary>
    /// Get all detected Claude tasks across all sessions.
    /// </summary>
    /// <returns>Read-only list of all Claude tasks.</returns>
    IReadOnlyList<FocusTask> GetAllClaudeTasks();

    /// <summary>
    /// Clean up tasks associated with a terminal session.
    /// Called when a terminal exits.
    /// </summary>
    /// <param name="sessionId">The terminal session ID to clean up.</param>
    void CleanupSession(Guid sessionId);

    /// <summary>
    /// Event fired when a Claude task is detected, updated, or completed.
    /// </summary>
    event EventHandler<ClaudeTaskEventArgs>? ClaudeTaskChanged;
}

/// <summary>
/// Event args for Claude task changes.
/// </summary>
public class ClaudeTaskEventArgs : EventArgs
{
    /// <summary>The task that changed.</summary>
    public required FocusTask Task { get; init; }

    /// <summary>The type of change that occurred.</summary>
    public required ClaudeTaskEventType EventType { get; init; }

    /// <summary>The terminal session ID that owns this task.</summary>
    public required Guid SessionId { get; init; }
}

/// <summary>
/// Type of Claude task event.
/// </summary>
public enum ClaudeTaskEventType
{
    /// <summary>A new task was created.</summary>
    Created,

    /// <summary>An existing task was updated.</summary>
    Updated,

    /// <summary>A task was completed.</summary>
    Completed,

    /// <summary>A task was deleted/cleaned up.</summary>
    Deleted
}
