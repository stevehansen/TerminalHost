namespace TerminalHost.Core.Domain;

/// <summary>
/// Derived display state shown for an agent in the Sessions sidebar. Independent of
/// the stored <see cref="SessionLifecycle"/> (which is retained only for true terminal
/// storage). Computed at read time from per-agent event timestamps; never written.
/// </summary>
public enum AgentDisplayState
{
    Working,
    WaitingPermission,
    Done,
    TimedOut
}
