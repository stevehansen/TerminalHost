namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for creating UI-thread-safe timers.
/// Platform implementations should ensure callbacks run on the UI thread.
/// </summary>
public interface ITimerService
{
    /// <summary>
    /// Creates a new timer with the specified interval and callback.
    /// The timer is created in a stopped state.
    /// </summary>
    IAppTimer CreateTimer(TimeSpan interval, Action callback);
}

/// <summary>
/// Represents a timer that can be started and stopped.
/// Named IAppTimer to avoid conflict with System.Threading.ITimer in .NET 8.
/// </summary>
public interface IAppTimer : IDisposable
{
    /// <summary>
    /// Starts the timer.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the timer.
    /// </summary>
    void Stop();

    /// <summary>
    /// Gets or sets whether the timer is currently running.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the interval between timer ticks.
    /// </summary>
    TimeSpan Interval { get; set; }
}
