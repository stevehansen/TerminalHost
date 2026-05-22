namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Coalesced notification that a single session has changed in a way the coordinator
/// can attribute to that session. Global live-set changes (which session id is unknown)
/// are not raised through this channel — subscribe to
/// <see cref="ILiveSessionTracker.LiveSessionsChanged"/> directly for those.
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
