using TerminalHost.Core.Interfaces;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// Virtual-time <see cref="IInactivityClock"/> for coordinator tests. Tests call
/// <see cref="Advance"/> to fire all scheduled callbacks without wall-clock delay.
/// </summary>
public sealed class FakeInactivityClock : IInactivityClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly object _lock = new();
    private readonly List<Action> _ticks = new();

    public IDisposable Schedule(TimeSpan period, Action onTick)
    {
        lock (_lock) { _ticks.Add(onTick); }
        return new Subscription(() => { lock (_lock) { _ticks.Remove(onTick); } });
    }

    public void Advance(TimeSpan delta)
    {
        UtcNow += delta;
        Action[] snapshot;
        lock (_lock) { snapshot = _ticks.ToArray(); }
        foreach (var t in snapshot) t();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _onDispose;
        public Subscription(Action onDispose) { _onDispose = onDispose; }
        public void Dispose() => _onDispose();
    }
}
