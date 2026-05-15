using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Default <see cref="IProjectMonitor"/> implementation. Owns one
/// <see cref="IAppTimer"/> per <see cref="SignalKind"/> flag and forwards ticks
/// to subscribers via the <see cref="Tick"/> event.
/// </summary>
/// <remarks>
/// UI-thread-only. The underlying <see cref="ITimerService"/> guarantees timer
/// callbacks run on the UI dispatcher, so the pause counter and Tick raise are
/// not thread-safe by design.
/// </remarks>
public sealed class ProjectMonitor : IProjectMonitor
{
    // Default intervals match the values used before Step 3a (issue #48).
    private static readonly TimeSpan DefaultGitStatusInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultGitAutoFetchInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultActivityInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultLinksInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultRunUrlInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<SignalKind, IAppTimer> _timers;
    private int _pauseCount;
    private bool _disposed;

    public event EventHandler<ProjectSignalEventArgs>? Tick;

    public ProjectMonitor(ITimerService timerService)
    {
        ArgumentNullException.ThrowIfNull(timerService);

        _timers = new Dictionary<SignalKind, IAppTimer>
        {
            [SignalKind.GitStatus]    = timerService.CreateTimer(DefaultGitStatusInterval,    () => RaiseTick(SignalKind.GitStatus)),
            [SignalKind.GitAutoFetch] = timerService.CreateTimer(DefaultGitAutoFetchInterval, () => RaiseTick(SignalKind.GitAutoFetch)),
            [SignalKind.Activity]     = timerService.CreateTimer(DefaultActivityInterval,     () => RaiseTick(SignalKind.Activity)),
            [SignalKind.Links]        = timerService.CreateTimer(DefaultLinksInterval,        () => RaiseTick(SignalKind.Links)),
            [SignalKind.RunUrl]       = timerService.CreateTimer(DefaultRunUrlInterval,       () => RaiseTick(SignalKind.RunUrl)),
        };
    }

    public void Start(SignalKind kinds)
    {
        ForEachFlag(kinds, t => t.Start());
    }

    public void Stop(SignalKind kinds)
    {
        ForEachFlag(kinds, t => t.Stop());
    }

    public void SetInterval(SignalKind kind, TimeSpan interval)
    {
        if (!IsSingleFlag(kind))
            throw new ArgumentException("SetInterval requires a single SignalKind flag.", nameof(kind));
        if (!_timers.TryGetValue(kind, out var timer))
            throw new ArgumentException($"Unknown signal: {kind}", nameof(kind));
        timer.Interval = interval;
    }

    public IDisposable Pause()
    {
        _pauseCount++;
        return new PauseToken(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var timer in _timers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }
        _timers.Clear();
        Tick = null;
    }

    private void RaiseTick(SignalKind kind)
    {
        if (_disposed || _pauseCount > 0) return;
        Tick?.Invoke(this, new ProjectSignalEventArgs(kind));
    }

    private void ForEachFlag(SignalKind kinds, Action<IAppTimer> action)
    {
        if (kinds == SignalKind.None) return;
        foreach (var (flag, timer) in _timers)
        {
            if ((kinds & flag) != 0) action(timer);
        }
    }

    private static bool IsSingleFlag(SignalKind kind)
    {
        var v = (int)kind;
        return v != 0 && (v & (v - 1)) == 0;
    }

    private sealed class PauseToken : IDisposable
    {
        private ProjectMonitor? _owner;
        public PauseToken(ProjectMonitor owner) { _owner = owner; }
        public void Dispose()
        {
            if (_owner is null) return;
            if (_owner._pauseCount > 0) _owner._pauseCount--;
            _owner = null;
        }
    }
}
