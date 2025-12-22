namespace TerminalHost.Services;

/// <summary>
/// Abstraction for UI thread dispatching.
/// Replaces WPF Dispatcher and Application.Current.Dispatcher.
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Posts an action to be executed on the UI thread.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Invokes an action on the UI thread and waits for completion.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Invokes a function on the UI thread and returns the result.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>
    /// Checks if currently on the UI thread.
    /// </summary>
    bool CheckAccess();
}
