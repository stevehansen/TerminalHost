using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// In-memory, present-moment view of running Claude Code sessions.
/// Owns the inactivity clock, transcript-watcher subscription, and the dictionary
/// of <see cref="LiveSession"/> objects keyed by Claude session id.
/// Hides hook-event routing, container-path resolution, and timer scheduling.
/// </summary>
public interface ILiveSessionTracker
{
    IReadOnlyList<LiveSession> GetLiveSessions();
    LiveSession? GetLiveSessionByClaudeId(string claudeSessionId);

    void HandleSessionStart(HookEvent hookEvent);
    void HandleFileChanged(HookEvent hookEvent);
    Task HandleSessionStopAsync(HookEvent hookEvent);
    void HandleToolStart(HookEvent hookEvent);
    void HandleToolEnd(HookEvent hookEvent);

    void StartInactivityTimer();
    void StopInactivityTimer();

    /// <summary>
    /// Runs one inactivity sweep synchronously. Production callers use the internal
    /// Timer started by <see cref="StartInactivityTimer"/>; SessionLifecycleCoordinator
    /// drives this directly when an <see cref="IInactivityClock"/> is wired so tests
    /// can advance virtual time.
    /// </summary>
    void CheckInactiveSessions();

    /// <summary>
    /// Fired whenever a live session is added, transitions to/from active, or is removed
    /// after the retention window. Pass-through: not coalesced.
    /// </summary>
    event EventHandler? LiveSessionsChanged;
}
