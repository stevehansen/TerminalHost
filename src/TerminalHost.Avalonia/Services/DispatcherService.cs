using Avalonia.Threading;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

internal sealed class DispatcherService : IDispatcherService
{
    public void BeginInvoke(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(action).Wait();
        }
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }

    // Additional Avalonia-specific overloads
    public void Post(Action action) => BeginInvoke(action);

    public async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        return await Dispatcher.UIThread.InvokeAsync(func);
    }
}
