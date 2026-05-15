namespace TerminalHost.Core.Workspace;

/// <summary>
/// Raised by <see cref="IProjectMonitor.Tick"/> when one of the underlying
/// periodic signals fires. <see cref="Kind"/> is always a single flag, never a
/// combination — subscribers switch on it to dispatch to the right handler.
/// </summary>
public sealed class ProjectSignalEventArgs : EventArgs
{
    public ProjectSignalEventArgs(SignalKind kind) { Kind = kind; }
    public SignalKind Kind { get; }
}
