namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents the current state of a run terminal.
/// </summary>
public enum RunState
{
    /// <summary>
    /// No process is running.
    /// </summary>
    Stopped,

    /// <summary>
    /// Process is starting up.
    /// </summary>
    Starting,

    /// <summary>
    /// Process is actively running.
    /// </summary>
    Running,

    /// <summary>
    /// Process is being stopped.
    /// </summary>
    Stopping
}
