using TerminalHost.Core.Interfaces;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// Synchronous <see cref="IDispatcherService"/> for tests. Every method runs on the calling thread
/// and <see cref="CheckAccess"/> returns true.
/// </summary>
public sealed class SynchronousDispatcherService : IDispatcherService
{
    public void BeginInvoke(Action action) => action();
    public void Invoke(Action action) => action();
    public Task InvokeAsync(Func<Task> action) => action();
    public bool TryInvoke(Action action, TimeSpan timeout) { action(); return true; }
    public bool CheckAccess() => true;
}
