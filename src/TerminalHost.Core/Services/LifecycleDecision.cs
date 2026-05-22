using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Services;

public static class LifecycleDecision
{
    /// <summary>
    /// Decides whether fresh activity should un-stick a terminal lifecycle.
    /// Claude Code's Stop hook fires between every turn (not just at session end), so a
    /// session can be flagged Completed/TimedOut while still running — revive on arrival.
    /// </summary>
    public static LifecycleVerdict ClassifyArrival(SessionLifecycle current)
    {
        bool isTerminal = current is SessionLifecycle.Completed
                                  or SessionLifecycle.Failed
                                  or SessionLifecycle.TimedOut;
        return isTerminal
            ? new LifecycleVerdict(Revive: true, NewLifecycle: SessionLifecycle.Active)
            : new LifecycleVerdict(Revive: false, NewLifecycle: null);
    }
}

public readonly record struct LifecycleVerdict(bool Revive, SessionLifecycle? NewLifecycle);
