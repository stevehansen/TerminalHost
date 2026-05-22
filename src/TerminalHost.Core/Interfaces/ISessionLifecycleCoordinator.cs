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
    /// Display-ready snapshot: deduped by working directory (most-recently-active per
    /// directory wins; sessions without a working directory pass through keyed by id),
    /// then ordered active-first then by recency. Consumers should treat the result as
    /// the source of truth for tree/list rendering — they no longer need to dedupe.
    /// </summary>
    IReadOnlyList<SessionView> GetSessionsForDisplay();

    /// <summary>
    /// Per-session change notification. Only fires for changes the coordinator can
    /// attribute to a specific session (activity-service lifecycle changes and
    /// inactivity-clock-driven session ends). Global live-set updates are not
    /// rebroadcast here.
    /// </summary>
    event EventHandler<SessionChanged>? Changed;

    /// <summary>
    /// Coalesced "something about the session set may have changed" pulse. Fires for
    /// any of the inputs the coordinator already observes (lifecycle changes, activity
    /// events, inactivity-clock ticks). Reentrant calls within a single dispatch chain
    /// collapse into a single pulse so consumers like the session tree don't refresh
    /// in a tight loop during bursts. Use this for "refresh my view" subscriptions;
    /// use <see cref="Changed"/> when you need the specific session and transition kind.
    /// </summary>
    event EventHandler? SessionsChanged;

    /// <summary>1:1 passthrough of <c>SessionActivityService.ActivityEventProcessed</c>.</summary>
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
    /// <see cref="SessionChangeKind.Ended"/> events; otherwise it delegates to the
    /// underlying live-session tracker's own internal Timer.
    /// </summary>
    void StartInactivityClock();

    void StopInactivityClock();
}
