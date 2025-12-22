using System.Windows.Threading;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows implementation of ITimerService using WPF DispatcherTimer.
/// </summary>
public sealed class TimerService : ITimerService
{
    public IAppTimer CreateTimer(TimeSpan interval, Action callback)
    {
        return new DispatcherTimerWrapper(interval, callback);
    }

    private sealed class DispatcherTimerWrapper : IAppTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _callback;
        private bool _disposed;

        public DispatcherTimerWrapper(TimeSpan interval, Action callback)
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

        public void Start()
        {
            if (!_disposed)
            {
                _timer.Start();
            }
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public bool IsEnabled
        {
            get => _timer.IsEnabled;
            set
            {
                if (value)
                    Start();
                else
                    Stop();
            }
        }

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
