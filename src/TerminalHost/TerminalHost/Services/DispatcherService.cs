using Avalonia.Threading;

namespace TerminalHost.Services;

internal sealed class DispatcherService : IDispatcherService
{
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public Task InvokeAsync(Action action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        return Dispatcher.UIThread.InvokeAsync(func).GetTask();
    }

    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }
}
