using Avalonia.Threading;
using CoreTimer = TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Timer service that implements both Avalonia-specific and Core interfaces.
/// </summary>
internal sealed class TimerService : ITimerService, CoreTimer.ITimerService
{
    public IPlatformTimer CreateTimer(TimeSpan interval, Action callback)
    {
        return new AvaloniaTimer(interval, callback);
    }

    public IPlatformTimer CreateTimer(TimeSpan interval, Func<Task> asyncCallback)
    {
        return new AvaloniaTimer(interval, () =>
        {
            // Fire and forget, but ensure exceptions are logged
            _ = Task.Run(async () =>
            {
                try
                {
                    await asyncCallback();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Timer callback error: {ex}");
                }
            });
        });
    }

    // Implement Core.Interfaces.ITimerService
    CoreTimer.IAppTimer CoreTimer.ITimerService.CreateTimer(TimeSpan interval, Action callback)
    {
        return new AvaloniaTimer(interval, callback);
    }

    private sealed class AvaloniaTimer : IPlatformTimer, CoreTimer.IAppTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _callback;

        public AvaloniaTimer(TimeSpan interval, Action callback)
        {
            _callback = callback;
            _timer = new DispatcherTimer
            {
                Interval = interval
            };
            _timer.Tick += OnTick;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _callback();
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public bool IsRunning => _timer.IsEnabled;

        // For IPlatformTimer
        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        // For Core.IAppTimer
        bool CoreTimer.IAppTimer.IsEnabled
        {
            get => _timer.IsEnabled;
            set
            {
                if (value)
                    _timer.Start();
                else
                    _timer.Stop();
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
