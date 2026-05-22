using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// Minimal in-memory <see cref="ISessionLifecycleCoordinator"/> for orchestrator and
/// VM tests. Only the methods/events exercised by current callers are implemented;
/// the rest are stubs.
/// </summary>
public sealed class FakeSessionLifecycleCoordinator : ISessionLifecycleCoordinator
{
    public int EnrichCallCount { get; private set; }

    public event EventHandler<SessionChanged>? Changed;
    public event EventHandler? SessionsChanged;
    public event EventHandler<ActivityEvent>? ActivityEventProcessed;

    public ISessionLifecycleAdvanced Advanced { get; }

    public FakeSessionLifecycleCoordinator()
    {
        Advanced = new AdvancedFake(this);
    }

    /// <summary>Synchronously raises <see cref="ActivityEventProcessed"/>.</summary>
    public void RaiseActivityEvent(ActivityEvent evt)
    {
        ActivityEventProcessed?.Invoke(this, evt);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Synchronously raises <see cref="SessionsChanged"/>.</summary>
    public void RaiseSessionsChanged()
    {
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Synchronously raises <see cref="Changed"/>.</summary>
    public void RaiseChanged(SessionChanged change)
    {
        Changed?.Invoke(this, change);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Ingest(HookEvent hookEvent, HookEventData? rawData = null) { }
    public void Ingest(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null) { }

    public SessionView? GetSession(string sessionId) => null;
    public IReadOnlyList<SessionView> GetActiveSessions() => Array.Empty<SessionView>();
    public IReadOnlyList<SessionView> GetAllSessions() => Array.Empty<SessionView>();
    public IReadOnlyList<SessionView> GetSessionsForDisplay() => Array.Empty<SessionView>();

    private sealed class AdvancedFake : ISessionLifecycleAdvanced
    {
        private readonly FakeSessionLifecycleCoordinator _owner;
        public AdvancedFake(FakeSessionLifecycleCoordinator owner) { _owner = owner; }

        public Task EnrichFromTranscriptAsync(string sessionId, CancellationToken ct = default)
        {
            _owner.EnrichCallCount++;
            return Task.CompletedTask;
        }

        public void ForceTerminate(string sessionId, string reason) { }
        public void ManualRevive(string sessionId, string reason) { }
        public void StartInactivityClock() { }
        public void StopInactivityClock() { }
    }
}
