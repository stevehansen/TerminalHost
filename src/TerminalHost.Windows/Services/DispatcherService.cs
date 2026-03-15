using System.Threading;
using System.Windows.Threading;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows implementation of IDispatcherService using WPF Dispatcher.
/// </summary>
public sealed class DispatcherService : IDispatcherService
{
    private Dispatcher? Dispatcher => System.Windows.Application.Current?.Dispatcher;

    public void BeginInvoke(Action action)
    {
        Interlocked.Increment(ref IoCounters.DispatcherBeginInvokes);
        var dispatcher = Dispatcher;
        if (dispatcher == null)
        {
            // No dispatcher available (e.g., in tests), run directly
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    public void Invoke(Action action)
    {
        Interlocked.Increment(ref IoCounters.DispatcherInvokes);
        var dispatcher = Dispatcher;
        if (dispatcher == null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher == null)
        {
            await action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            await action();
        }
        else
        {
            await dispatcher.InvokeAsync(action).Task.Unwrap();
        }
    }

    public bool TryInvoke(Action action, TimeSpan timeout)
    {
        var dispatcher = Dispatcher;
        if (dispatcher == null)
        {
            action();
            return true;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return true;
        }

        var op = dispatcher.BeginInvoke(action);
        var status = op.Wait(timeout);
        return status == System.Windows.Threading.DispatcherOperationStatus.Completed;
    }

    public bool CheckAccess()
    {
        return Dispatcher?.CheckAccess() ?? true;
    }
}
