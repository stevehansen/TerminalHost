using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface ISingleInstanceService : IDisposable
{
    /// <summary>
    /// Fired when command line arguments are received from another instance.
    /// </summary>
    event EventHandler<CommandLineArgs>? CommandReceived;

    /// <summary>
    /// Fired when a hook event is received from a Claude Code hook callback.
    /// </summary>
    event EventHandler<HookEvent>? HookEventReceived;

    /// <summary>
    /// Attempts to acquire the single instance mutex.
    /// </summary>
    /// <returns>True if this is the first instance.</returns>
    bool TryAcquireLock();

    /// <summary>
    /// Starts the named pipe server to listen for commands from other instances.
    /// </summary>
    void StartPipeServer();

    /// <summary>
    /// Checks if the main application instance is running (has the mutex).
    /// </summary>
    /// <returns>True if the main instance is running.</returns>
    bool IsMainInstanceRunning();
}
