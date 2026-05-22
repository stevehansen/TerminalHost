using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Maintains in-memory SessionActivityState per active Claude Code session.
/// Processes hook events and transcript data into rich activity tracking.
/// </summary>
public interface ISessionActivityService
{
    /// <summary>
    /// Gets the activity state for a session, or null if not tracked.
    /// </summary>
    SessionActivityState? GetState(string sessionId);

    /// <summary>
    /// Gets all active session activity states.
    /// </summary>
    IReadOnlyList<SessionActivityState> GetActiveStates();

    /// <summary>
    /// Gets all session activity states (active and completed).
    /// </summary>
    IReadOnlyList<SessionActivityState> GetAllStates();

    /// <summary>
    /// Creates or retrieves the activity state for a session.
    /// Called when a SessionStart hook arrives.
    /// </summary>
    SessionActivityState GetOrCreateState(string sessionId, string? cwd = null, string? transcriptPath = null,
        SessionSource source = SessionSource.Local, string? containerName = null);

    /// <summary>
    /// Removes the activity state for a session (cleanup after session ends).
    /// Completed sessions are retained for a configurable period before cleanup.
    /// </summary>
    void RemoveState(string sessionId);

    /// <summary>
    /// Processes a hook event, translating it into activity events and updating state.
    /// This is the main entry point for real-time hook data.
    /// </summary>
    void ProcessHookEvent(HookEvent hookEvent, HookEventData? rawData = null);

    /// <summary>
    /// Enriches a session's activity state by parsing its transcript file.
    /// Called when a session ends or on-demand for additional detail.
    /// </summary>
    Task EnrichFromTranscriptAsync(string sessionId);

    /// <summary>
    /// Applies transcript-derived activity events to a session's state.
    /// Called by ITranscriptWatcher with incremental events from JSONL file changes.
    /// </summary>
    void ProcessTranscriptEvents(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null);

    /// <summary>
    /// Forcibly sets a session's lifecycle under the service lock and raises
    /// <see cref="LifecycleChanged"/>. Terminal states stamp <see cref="SessionActivityState.EndTime"/>
    /// (if not already set); transitioning to <see cref="SessionLifecycle.Active"/> clears
    /// EndTime and reactivates the main agent. No-op if the session is unknown or already
    /// in the target lifecycle. The single write path for out-of-band lifecycle mutation
    /// (used by ISessionLifecycleCoordinator.Advanced).
    /// </summary>
    bool MarkLifecycle(string sessionId, SessionLifecycle newLifecycle);

    /// <summary>
    /// Gets tool call statistics for a session.
    /// </summary>
    (int Total, int FileReads, int FileWrites, int ShellCommands, int Subagents) GetToolCallStats(string sessionId);

    /// <summary>
    /// Gets the top files accessed in a session.
    /// </summary>
    IReadOnlyList<FileActivity> GetTopFiles(string sessionId, int count = 10);

    /// <summary>
    /// Fired when an activity event is processed.
    /// </summary>
    event EventHandler<ActivityEvent>? ActivityEventProcessed;

    /// <summary>
    /// Fired when a session's lifecycle state changes.
    /// </summary>
    event EventHandler<(string SessionId, SessionLifecycle NewState)>? LifecycleChanged;
}
