using System.Windows.Threading;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows implementation of IDispatcherService using WPF Dispatcher.
/// </summary>
public sealed class DispatcherService : IDispatcherService
{
    private Dispatcher? Dispatcher => System.Windows.Application.Current?.Dispatcher;

    public void BeginInvoke(Action action)
    {
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

    public bool CheckAccess()
    {
        return Dispatcher?.CheckAccess() ?? true;
    }
}
