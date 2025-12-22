namespace TerminalHost.Services;

/// <summary>
/// Abstraction for timer functionality.
/// Replaces WPF DispatcherTimer.
/// </summary>
public interface ITimerService
{
    /// <summary>
    /// Creates a new timer that executes on the UI thread.
    /// </summary>
    /// <param name="interval">Timer interval</param>
    /// <param name="callback">Callback to execute on each tick</param>
    /// <returns>A controllable timer instance</returns>
    IPlatformTimer CreateTimer(TimeSpan interval, Action callback);

    /// <summary>
    /// Creates a new async timer.
    /// </summary>
    IPlatformTimer CreateTimer(TimeSpan interval, Func<Task> asyncCallback);
}

/// <summary>
/// A controllable timer instance.
/// </summary>
public interface IPlatformTimer : IDisposable
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
    /// Gets whether the timer is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets or sets the timer interval.
    /// </summary>
    TimeSpan Interval { get; set; }
}
