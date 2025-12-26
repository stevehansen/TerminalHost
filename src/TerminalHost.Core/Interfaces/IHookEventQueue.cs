using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for queuing Claude Code hook events when the main application isn't running.
/// Events are persisted to disk and processed when the app starts.
/// </summary>
public interface IHookEventQueue
{
    /// <summary>
    /// Enqueues a hook event to the persistent queue.
    /// Thread-safe and uses file locking for concurrent access.
    /// </summary>
    /// <param name="hookEvent">The event to enqueue.</param>
    Task EnqueueAsync(HookEvent hookEvent);

    /// <summary>
    /// Dequeues all pending events from the queue.
    /// Returns events in FIFO order.
    /// </summary>
    /// <returns>List of queued events.</returns>
    Task<IReadOnlyList<HookEvent>> DequeueAllAsync();

    /// <summary>
    /// Gets the count of pending events without removing them.
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Clears all events from the queue.
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// Gets the path to the queue file.
    /// </summary>
    string QueueFilePath { get; }
}
