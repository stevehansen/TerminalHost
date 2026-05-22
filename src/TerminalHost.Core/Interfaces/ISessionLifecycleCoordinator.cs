using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Thin facade over <see cref="ISessionActivityService"/> and
/// <see cref="ILiveSessionTracker"/>. Routes ingestion to both legacy services and
/// projects their combined state into <see cref="SessionView"/> for consumers that
/// want a single read surface. Additive in Phase 2 — the legacy services keep
/// their full public surface.
/// </summary>
public interface ISessionLifecycleCoordinator
{
    /// <summary>Routes a hook event into both the activity service and the live tracker.</summary>
    void Ingest(HookEvent hookEvent, HookEventData? rawData = null);

    /// <summary>Routes transcript-derived events into the activity service.</summary>
    void Ingest(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null);

    SessionView? GetSession(string sessionId);
    IReadOnlyList<SessionView> GetActiveSessions();
    IReadOnlyList<SessionView> GetAllSessions();

    /// <summary>
    /// Per-session change notification. Only fires for changes the coordinator can
    /// attribute to a specific session (activity-service lifecycle changes and
    /// inactivity-clock-driven session ends). Global live-set updates are not
    /// rebroadcast here.
    /// </summary>
    event EventHandler<SessionChanged>? Changed;

    /// <summary>1:1 passthrough of <see cref="ISessionActivityService.ActivityEventProcessed"/>.</summary>
    event EventHandler<ActivityEvent>? ActivityEventProcessed;

    ISessionLifecycleAdvanced Advanced { get; }
}

/// <summary>
/// Less-commonly-used operations. Segregated to keep the main interface small.
/// </summary>
public interface ISessionLifecycleAdvanced
{
    Task EnrichFromTranscriptAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Force a session into a terminal lifecycle state with the given reason.</summary>
    void ForceTerminate(string sessionId, string reason);

    /// <summary>Manually revive a terminal session if the classifier permits.</summary>
    void ManualRevive(string sessionId, string reason);

    /// <summary>
    /// Starts the inactivity sweep. If an <see cref="IInactivityClock"/> was supplied
    /// the coordinator drives the sweep itself and emits per-session
    /// <see cref="SessionChangeKind.Ended"/> events; otherwise it delegates to
    /// <see cref="ILiveSessionTracker.StartInactivityTimer"/>.
    /// </summary>
    void StartInactivityClock();

    void StopInactivityClock();
}
