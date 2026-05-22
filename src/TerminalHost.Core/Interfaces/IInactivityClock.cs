namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Pluggable clock for inactivity-driven session sweeps. Production wires
/// <c>SystemInactivityClock</c> (a thin <see cref="System.Threading.Timer"/> wrapper);
/// tests inject a virtual-time fake that drives ticks via <c>Advance</c>.
/// </summary>
public interface IInactivityClock
{
    DateTime UtcNow { get; }

    /// <summary>
    /// Schedules <paramref name="onTick"/> to fire at <paramref name="period"/> intervals.
    /// Disposing the returned handle cancels the schedule.
    /// </summary>
    IDisposable Schedule(TimeSpan period, Action onTick);
}
