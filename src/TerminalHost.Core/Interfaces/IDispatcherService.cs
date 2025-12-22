namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for UI thread dispatching.
/// Allows ViewModels to marshal calls to the UI thread without direct WPF dependencies.
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Executes the action asynchronously on the UI thread (fire-and-forget).
    /// </summary>
    void BeginInvoke(Action action);

    /// <summary>
    /// Executes the action synchronously on the UI thread.
    /// If already on UI thread, executes immediately.
    /// </summary>
    void Invoke(Action action);

    /// <summary>
    /// Executes the async action on the UI thread and returns a task.
    /// </summary>
    Task InvokeAsync(Func<Task> action);

    /// <summary>
    /// Returns true if the calling thread is the UI thread.
    /// </summary>
    bool CheckAccess();
}
