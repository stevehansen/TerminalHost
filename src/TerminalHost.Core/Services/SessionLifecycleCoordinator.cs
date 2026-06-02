using System.Collections.Concurrent;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Thin facade over <see cref="ISessionActivityService"/> and
/// <see cref="ILiveSessionTracker"/>. Holds no session state of its own — reads and
/// writes are forwarded to the underlying services, whose locks already protect the
/// data. Event invocations happen on whichever thread the upstream service fires
/// them; subscribers must be thread-safe.
/// <para>
/// In Phase 2 the legacy services retain their public surface; the coordinator is
/// purely additive. Phase 3 will migrate consumers off the underlying services.
/// </para>
/// </summary>
public sealed class SessionLifecycleCoordinator : ISessionLifecycleCoordinator, IDisposable
{
    private readonly ISessionActivityService _activity;
    private readonly ILiveSessionTracker _live;
    private readonly IInactivityClock? _inactivityClock;
    private readonly ConcurrentDictionary<string, SessionLifecycle> _previousLifecycle =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly AdvancedFacade _advanced;
    private IDisposable? _inactivityHandle;

    // Dispatch-depth coalescer for SessionsChanged. Every external entry point
    // (Ingest, Advanced.*, RunInactivityScan) opens a frame on entry and closes
    // it on exit. Upstream pulses raised while a frame is open set _pulsePending
    // instead of firing; the outermost frame drains the pending flag on exit so a
    // burst of N upstream events produces exactly one consumer-visible pulse.
    // Instance-level (not [ThreadStatic]) so async frames survive await thread
    // switches; concurrent Ingests from different threads share one drain.
    private int _dispatchDepth;
    private int _pulsePending; // 0 or 1, managed via Interlocked

    public event EventHandler<SessionChanged>? Changed;
    public event EventHandler? SessionsChanged;
    public event EventHandler<ActivityEvent>? ActivityEventProcessed;

    // Constructor is internal because ISessionActivityService / ILiveSessionTracker
    // are internal interfaces. DI / tests construct via InternalsVisibleTo + the
    // CoreSessionServiceRegistration helper.
    internal SessionLifecycleCoordinator(
        ISessionActivityService activity,
        ILiveSessionTracker live,
        IInactivityClock? inactivityClock = null)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _live = live ?? throw new ArgumentNullException(nameof(live));
        _inactivityClock = inactivityClock;
        _advanced = new AdvancedFacade(this);

        _activity.ActivityEventProcessed += OnActivityEventProcessed;
        _activity.LifecycleChanged += OnLifecycleChanged;
        // The live tracker raises LiveSessionsChanged from paths that don't always
        // route through the activity service (e.g. transcript-watcher inactivity);
        // route those into the same coalesced pulse so consumers see them.
        _live.LiveSessionsChanged += OnLiveSessionsChanged;
    }

    public ISessionLifecycleAdvanced Advanced => _advanced;

    public void Ingest(HookEvent hookEvent, HookEventData? rawData = null)
    {
        if (hookEvent is null) return;

        EnterDispatchFrame();
        try
        {
            _activity.ProcessHookEvent(hookEvent, rawData);

            switch (hookEvent.EventType)
            {
                case HookEventType.SessionStart:
                    _live.HandleSessionStart(hookEvent);
                    break;
                case HookEventType.FileChanged:
                    _live.HandleFileChanged(hookEvent);
                    break;
                case HookEventType.SessionStop:
                case HookEventType.SessionEnd:
                    // HandleSessionStopAsync returns Task.CompletedTask synchronously and schedules
                    // its retention cleanup via Task.Run internally — discarding is safe.
                    _ = _live.HandleSessionStopAsync(hookEvent);
                    break;
                case HookEventType.ToolStart:
                    _live.HandleToolStart(hookEvent);
                    break;
                case HookEventType.ToolEnd:
                case HookEventType.ToolError:
                    _live.HandleToolEnd(hookEvent);
                    break;
                case HookEventType.SubagentStart:
                case HookEventType.SubagentStop:
                case HookEventType.Notification:
                    // Route through HandleToolStart so EnsureLiveSession runs if the
                    // SessionStart hook was missed. Matches App.xaml.cs behavior.
                    _live.HandleToolStart(hookEvent);
                    break;
            }
        }
        finally { ExitDispatchFrame(); }
    }

    public void Ingest(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        EnterDispatchFrame();
        try { _activity.ProcessTranscriptEvents(sessionId, events, summary, model); }
        finally { ExitDispatchFrame(); }
    }

    public void RecordTerminalTitleActivity(string workingDirectory, string title)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return;
        // Pure state stamp — fires no upstream event, so no dispatch frame / SessionsChanged
        // pulse here. The spinner animates several times a second; pulsing on every change
        // would spam consumers. The session tree polls on its own short tick to pick up the
        // working/idle transitions (see SessionsTreePanelViewModel); other consumers refresh
        // on their next read.
        _activity.RecordTerminalTitleActivity(workingDirectory, title, Now());
    }

    public SessionView? GetSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        var state = _activity.GetState(sessionId);
        var live = _live.GetLiveSessionByClaudeId(sessionId);
        if (state is null && live is null) return null;
        return ToView(sessionId, state, live, Now());
    }

    public IReadOnlyList<SessionView> GetActiveSessions()
    {
        var states = _activity.GetActiveStates();
        var liveSessions = _live.GetLiveSessions().Where(s => s.IsActive).ToList();
        return Merge(states, liveSessions);
    }

    public IReadOnlyList<SessionView> GetAllSessions()
    {
        var states = _activity.GetAllStates();
        var liveSessions = _live.GetLiveSessions();
        return Merge(states, liveSessions);
    }

    public IReadOnlyList<SessionView> GetSessionsForDisplay()
    {
        // Dedupe by working directory: a resumed session creates a new SessionId for
        // the same workspace while the prior id often lingers (no Stop hook arrived).
        // The session tree shouldn't render both rows — keep the most-recently-active
        // entry per directory. Sessions with no working directory pass through keyed
        // by their session id so they don't collapse into a single bucket.
        return GetAllSessions()
            .GroupBy(v => string.IsNullOrEmpty(v.ActivityState.WorkingDirectory)
                ? v.SessionId
                : v.ActivityState.WorkingDirectory!,
                     StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(v => v.ActivityState.IsActive)
                .ThenByDescending(v => v.ActivityState.LastActivityTime ?? v.StartTime)
                .First())
            .OrderByDescending(v => v.ActivityState.IsActive)
            .ThenByDescending(v => v.ActivityState.LastActivityTime ?? v.StartTime)
            .ToList();
    }

    private List<SessionView> Merge(IReadOnlyList<SessionActivityState> states, IReadOnlyList<LiveSession> liveSessions)
    {
        var byId = new Dictionary<string, (SessionActivityState? State, LiveSession? Live)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in states)
            byId[s.SessionId] = (s, null);
        foreach (var l in liveSessions)
        {
            if (string.IsNullOrEmpty(l.ClaudeSessionId)) continue;
            byId.TryGetValue(l.ClaudeSessionId, out var existing);
            byId[l.ClaudeSessionId] = (existing.State, l);
        }

        var now = Now();
        var views = new List<SessionView>(byId.Count);
        foreach (var (id, pair) in byId)
            views.Add(ToView(id, pair.State, pair.Live, now));
        return views;
    }

    private DateTime Now() => _inactivityClock?.UtcNow ?? DateTime.UtcNow;

    private static SessionView ToView(string sessionId, SessionActivityState? state, LiveSession? live, DateTime now)
    {
        // Synthesize an empty activity state for live-only sessions so callers always see a non-null value.
        var activityState = state ?? SessionActivityState.Create(sessionId, live?.WorkingDirectory, live?.TranscriptPath,
            live?.Source ?? SessionSource.Local, live?.ContainerName);

        var isLive = live is not null
            ? live.IsActive
            : activityState.DeriveParentDisplay(now) is AgentDisplayState.Working or AgentDisplayState.WaitingPermission;

        return new SessionView(
            SessionId: sessionId,
            Source: state?.Source ?? live?.Source ?? SessionSource.Local,
            ContainerName: state?.ContainerName ?? live?.ContainerName,
            Lifecycle: activityState.Lifecycle,
            IsLive: isLive,
            StartTime: state?.StartTime ?? live?.StartTime ?? now,
            EndTime: state?.EndTime ?? live?.EndTime,
            ActivityState: activityState,
            LiveSession: live);
    }

    private void OnActivityEventProcessed(object? sender, ActivityEvent e)
    {
        ActivityEventProcessed?.Invoke(this, e);
        PulseSessionsChanged();
    }

    private void OnLifecycleChanged(object? sender, (string SessionId, SessionLifecycle NewState) e)
    {
        var previous = _previousLifecycle.TryGetValue(e.SessionId, out var p) ? (SessionLifecycle?)p : null;
        _previousLifecycle[e.SessionId] = e.NewState;

        var view = GetSession(e.SessionId);
        if (view is null) return;

        var kind = ClassifyTransition(previous, e.NewState);
        Changed?.Invoke(this, new SessionChanged(e.SessionId, kind, view));
        PulseSessionsChanged();
    }

    private void OnLiveSessionsChanged(object? sender, EventArgs e) => PulseSessionsChanged();

    private void PulseSessionsChanged()
    {
        // If a dispatch frame is open, defer; the outermost frame drains the pending
        // flag once on exit. Outside any frame (e.g. the live tracker raising
        // LiveSessionsChanged from its own internal timer with no coordinator entry
        // on the call stack), fire directly.
        if (Volatile.Read(ref _dispatchDepth) > 0)
        {
            Interlocked.Exchange(ref _pulsePending, 1);
            return;
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnterDispatchFrame() => Interlocked.Increment(ref _dispatchDepth);

    // Outermost frame (depth → 0) observes any pending pulse, clears it, and fires
    // SessionsChanged exactly once. Race-safe across threads: the Interlocked.Exchange
    // returns the previous value so two threads racing the drain can't both fire.
    private void ExitDispatchFrame()
    {
        var newDepth = Interlocked.Decrement(ref _dispatchDepth);
        if (newDepth == 0 && Interlocked.Exchange(ref _pulsePending, 0) == 1)
            SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SessionChangeKind ClassifyTransition(SessionLifecycle? previous, SessionLifecycle next)
    {
        bool prevIsTerminal = previous is SessionLifecycle.Completed or SessionLifecycle.Failed or SessionLifecycle.TimedOut;
        bool nextIsTerminal = next is SessionLifecycle.Completed or SessionLifecycle.Failed or SessionLifecycle.TimedOut;

        if (previous is null) return SessionChangeKind.Created;
        if (prevIsTerminal && next == SessionLifecycle.Active) return SessionChangeKind.Revived;
        if (nextIsTerminal) return SessionChangeKind.Ended;
        return SessionChangeKind.LifecycleChanged;
    }

    private void RunInactivityScan()
    {
        EnterDispatchFrame();
        // Inactivity ticks must pulse SessionsChanged even when no session transitioned —
        // consumers re-render "active 5m ago" → "5m1s ago" rows. Seed the pending flag so
        // ExitDispatchFrame fires at the end whether or not TransitionLifecycle ran.
        Interlocked.Exchange(ref _pulsePending, 1);
        try
        {
            // Snapshot active live sessions before the scan, run the tracker's sweep, then mark
            // any newly-ended session as TimedOut via the activity service. MarkLifecycle raises
            // LifecycleChanged, which OnLifecycleChanged classifies as Ended — single write path.
            var before = _live.GetLiveSessions()
                .Where(s => s.IsActive)
                .Select(s => s.ClaudeSessionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _live.CheckInactiveSessions();

            foreach (var id in before)
            {
                var live = _live.GetLiveSessionByClaudeId(id);
                if (live is null || live.IsActive) continue;
                TransitionLifecycle(id, SessionLifecycle.TimedOut);
            }
        }
        finally { ExitDispatchFrame(); }
    }

    // Seeds _previousLifecycle so OnLifecycleChanged classifies the transition correctly.
    // The activity service raises LifecycleChanged only on transitions, so sessions created
    // at the default Active state have no cache entry until one fires.
    private bool TransitionLifecycle(string sessionId, SessionLifecycle newLifecycle)
    {
        var state = _activity.GetState(sessionId);
        if (state is null) return false;
        _previousLifecycle[sessionId] = state.Lifecycle;
        return _activity.MarkLifecycle(sessionId, newLifecycle);
    }

    public void Dispose()
    {
        _activity.ActivityEventProcessed -= OnActivityEventProcessed;
        _activity.LifecycleChanged -= OnLifecycleChanged;
        _live.LiveSessionsChanged -= OnLiveSessionsChanged;
        _inactivityHandle?.Dispose();
        _inactivityHandle = null;
    }

    private sealed class AdvancedFacade : ISessionLifecycleAdvanced
    {
        private readonly SessionLifecycleCoordinator _owner;

        public AdvancedFacade(SessionLifecycleCoordinator owner) { _owner = owner; }

        // ct currently unused; deferred until ISessionActivityService grows a ct overload.
        public async Task EnrichFromTranscriptAsync(string sessionId, CancellationToken ct = default)
        {
            _owner.EnterDispatchFrame();
            try { await _owner._activity.EnrichFromTranscriptAsync(sessionId); }
            finally { _owner.ExitDispatchFrame(); }
        }

        public void ForceTerminate(string sessionId, string reason)
        {
            _owner.EnterDispatchFrame();
            try
            {
                // No structured "fatal" channel today — Failed for explicit error reasons, Completed otherwise.
                var newLifecycle = reason is "error" or "crash" or "fatal"
                    ? SessionLifecycle.Failed
                    : SessionLifecycle.Completed;

                // The activity service raises LifecycleChanged → OnLifecycleChanged → Changed(Ended).
                _owner.TransitionLifecycle(sessionId, newLifecycle);
            }
            finally { _owner.ExitDispatchFrame(); }
        }

        public void ManualRevive(string sessionId, string reason)
        {
            _owner.EnterDispatchFrame();
            try
            {
                var state = _owner._activity.GetState(sessionId);
                if (state is null) return;

                var verdict = LifecycleDecision.ClassifyArrival(state.Lifecycle);
                if (!verdict.Revive || verdict.NewLifecycle is not { } newLifecycle) return;

                // OnLifecycleChanged → ClassifyTransition sees prev terminal + new Active → Revived.
                _owner.TransitionLifecycle(sessionId, newLifecycle);
            }
            finally { _owner.ExitDispatchFrame(); }
        }

        public void StartInactivityClock()
        {
            if (_owner._inactivityClock is { } clock)
            {
                _owner._inactivityHandle?.Dispose();
                _owner._inactivityHandle = clock.Schedule(
                    TimeSpan.FromMilliseconds(LiveSessionTracker.InactivityCheckIntervalMs),
                    _owner.RunInactivityScan);
            }
            else
            {
                _owner._live.StartInactivityTimer();
            }
        }

        public void StopInactivityClock()
        {
            if (_owner._inactivityClock is not null)
            {
                _owner._inactivityHandle?.Dispose();
                _owner._inactivityHandle = null;
            }
            else
            {
                _owner._live.StopInactivityTimer();
            }
        }
    }
}
