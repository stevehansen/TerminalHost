namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Coalesced notification that a single session has changed in a way the coordinator
/// can attribute to that session. Global live-set changes (no specific session id)
/// flow through <see cref="ISessionLifecycleCoordinator.SessionsChanged"/> instead.
/// </summary>
public sealed record SessionChanged(string SessionId, SessionChangeKind Kind, SessionView After);

public enum SessionChangeKind
{
    Created,
    LifecycleChanged,
    Revived,
    Ended,
    Touched
}
