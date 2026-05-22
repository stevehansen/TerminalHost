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

    public event EventHandler<SessionChanged>? Changed;
    public event EventHandler<ActivityEvent>? ActivityEventProcessed;

    public SessionLifecycleCoordinator(
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
    }

    public ISessionLifecycleAdvanced Advanced => _advanced;

    public void Ingest(HookEvent hookEvent, HookEventData? rawData = null)
    {
        if (hookEvent is null) return;

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

    public void Ingest(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _activity.ProcessTranscriptEvents(sessionId, events, summary, model);
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
    }

    private void OnLifecycleChanged(object? sender, (string SessionId, SessionLifecycle NewState) e)
    {
        var previous = _previousLifecycle.TryGetValue(e.SessionId, out var p) ? (SessionLifecycle?)p : null;
        _previousLifecycle[e.SessionId] = e.NewState;

        var view = GetSession(e.SessionId);
        if (view is null) return;

        var kind = ClassifyTransition(previous, e.NewState);
        Changed?.Invoke(this, new SessionChanged(e.SessionId, kind, view));
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
        _inactivityHandle?.Dispose();
        _inactivityHandle = null;
    }

    private sealed class AdvancedFacade : ISessionLifecycleAdvanced
    {
        private readonly SessionLifecycleCoordinator _owner;

        public AdvancedFacade(SessionLifecycleCoordinator owner) { _owner = owner; }

        // ct currently unused; deferred until ISessionActivityService grows a ct overload.
        public Task EnrichFromTranscriptAsync(string sessionId, CancellationToken ct = default) =>
            _owner._activity.EnrichFromTranscriptAsync(sessionId);

        public void ForceTerminate(string sessionId, string reason)
        {
            // No structured "fatal" channel today — Failed for explicit error reasons, Completed otherwise.
            var newLifecycle = reason is "error" or "crash" or "fatal"
                ? SessionLifecycle.Failed
                : SessionLifecycle.Completed;

            // The activity service raises LifecycleChanged → OnLifecycleChanged → Changed(Ended).
            _owner.TransitionLifecycle(sessionId, newLifecycle);
        }

        public void ManualRevive(string sessionId, string reason)
        {
            var state = _owner._activity.GetState(sessionId);
            if (state is null) return;

            var verdict = LifecycleDecision.ClassifyArrival(state.Lifecycle);
            if (!verdict.Revive || verdict.NewLifecycle is not { } newLifecycle) return;

            // OnLifecycleChanged → ClassifyTransition sees prev terminal + new Active → Revived.
            _owner.TransitionLifecycle(sessionId, newLifecycle);
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
