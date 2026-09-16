using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class SystemInactivityClock : IInactivityClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public IDisposable Schedule(TimeSpan period, Action onTick) =>
        new TimerHandle(new Timer(_ => onTick(), null, period, period));

    private sealed class TimerHandle : IDisposable
    {
        private readonly Timer _t;
        public TimerHandle(Timer t) { _t = t; }
        public void Dispose() => _t.Dispose();
    }
}
