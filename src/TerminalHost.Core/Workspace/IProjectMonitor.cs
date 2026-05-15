namespace TerminalHost.Core.Workspace;

/// <summary>
/// Coordinates periodic per-project signals (git status, git auto-fetch, activity,
/// link detection, run URL detection) behind a single seam.
/// <para>
/// Step 3a of the manager decomposition (issue #48) — this is the timer
/// aggregation seam. Refresh logic still lives in the subscriber; the monitor's
/// job is to own timer lifetime, expose a single <see cref="Tick"/> event with
/// a <see cref="SignalKind"/> discriminator, and provide deterministic control
/// (Start/Stop/SetInterval/Pause) for tests and configuration changes.
/// </para>
/// <para>
/// Snapshot computation (<c>For(workdir)</c>) lands in Step 3b after
/// <c>IWorkspaceService</c> owns per-workdir state.
/// </para>
/// </summary>
public interface IProjectMonitor : IDisposable
{
    /// <summary>
    /// Raised on the UI thread when a started signal fires. <c>e.Kind</c> is
    /// always a single <see cref="SignalKind"/> flag. Suppressed while a
    /// <see cref="Pause"/> token is held.
    /// </summary>
    event EventHandler<ProjectSignalEventArgs>? Tick;

    /// <summary>
    /// Starts the timer(s) for every flag set in <paramref name="kinds"/>.
    /// Already-running timers are unaffected.
    /// </summary>
    void Start(SignalKind kinds);

    /// <summary>
    /// Stops the timer(s) for every flag set in <paramref name="kinds"/>.
    /// Already-stopped timers are unaffected.
    /// </summary>
    void Stop(SignalKind kinds);

    /// <summary>
    /// Adjusts the interval of a single signal's timer. Currently used for
    /// <see cref="SignalKind.GitAutoFetch"/> which has a configurable interval.
    /// </summary>
    void SetInterval(SignalKind kind, TimeSpan interval);

    /// <summary>
    /// Suppresses <see cref="Tick"/> events until the returned token is
    /// disposed. Underlying timers keep running; ticks that fire while paused
    /// are dropped. Pause tokens nest — Tick resumes only when all outstanding
    /// tokens are disposed.
    /// </summary>
    IDisposable Pause();
}
