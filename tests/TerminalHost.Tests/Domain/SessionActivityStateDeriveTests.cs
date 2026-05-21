using System;
using Shouldly;
using TerminalHost.Core.Domain;
using Xunit;

namespace TerminalHost.Tests.Domain;

/// <summary>
/// M2 — verifies the pure derivation of AgentDisplayState (per-agent and parent
/// aggregate) from the M1 input timestamps on SessionActivityState/AgentInstance.
/// No service interaction; POCOs are constructed and mutated directly.
/// </summary>
public class SessionActivityStateDeriveTests
{
    private const string SessionId = "sess-derive-1";
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SessionActivityState NewStateWithMain()
    {
        // SessionActivityState.Create seeds the main agent, but it also stamps StartTime
        // and other fields with DateTime.UtcNow. For derivation purposes we only care
        // about the Agents dictionary, so we mirror Create's main-agent setup manually.
        var state = new SessionActivityState { SessionId = SessionId };
        var main = AgentInstance.CreateMain(SessionId);
        state.Agents[main.Id] = main;
        return state;
    }

    private static AgentInstance AddSub(SessionActivityState state, string id = "sub-1")
    {
        var sub = AgentInstance.CreateSubagent(id, SessionId, parentId: SessionId, name: "subagent", task: null);
        state.Agents[id] = sub;
        return sub;
    }

    [Fact]
    public void Initial_state_is_Done()
    {
        // No events yet → not active → Done.
        var state = NewStateWithMain();
        state.MainAgent!.LastEventKind.ShouldBe(AgentEventKind.None);

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.Done);
    }

    [Fact]
    public void Stop_within_threshold_is_Done()
    {
        var state = NewStateWithMain();
        var main = state.MainAgent!;
        main.LastStopHookTime = Now.AddSeconds(-30);

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.Done);
    }

    [Fact]
    public void Stop_past_threshold_is_TimedOut()
    {
        var state = NewStateWithMain();
        var main = state.MainAgent!;
        main.LastStopHookTime = Now.AddMinutes(-2).AddSeconds(-30);

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.TimedOut);
    }

    [Fact]
    public void Active_subagent_does_not_make_parent_Working()
    {
        // Parent display = main's own derived state. Subagent activity never aggregates
        // up — a still-Working child cannot un-Done the main.
        var state = NewStateWithMain();
        var main = state.MainAgent!;
        main.LastStopHookTime = Now.AddSeconds(-15);

        var sub = AddSub(state);
        sub.LastActivityEventTime = Now.AddSeconds(-1);

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.Done);
    }

    [Fact]
    public void Stopped_subagent_does_not_keep_parent_Working()
    {
        var state = NewStateWithMain();
        var main = state.MainAgent!;
        main.LastStopHookTime = Now.AddSeconds(-15);

        var sub = AddSub(state);
        sub.LastStopHookTime = Now.AddSeconds(-10);

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.Done);
    }

    [Fact]
    public void Permission_prompt_on_main_yields_WaitingPermission()
    {
        // With no aggregation, this is just the main's own derivation surfacing
        // directly. The child Activity stamp is irrelevant.
        var state = NewStateWithMain();
        var main = state.MainAgent!;
        main.LastPermissionPromptTime = Now.AddSeconds(-5);

        var sub = AddSub(state);
        sub.LastActivityEventTime = Now.AddSeconds(-1);

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.WaitingPermission);
    }

    [Fact]
    public void Subagent_never_renders_WaitingPermission_invariant()
    {
        // Trivially true under the new model: DeriveParentDisplay returns the main's
        // own derived state and never inspects subagents, so a subagent stamped with
        // PermissionPrompt (which would violate the M1 stamping invariant anyway)
        // cannot influence parent display. Kept as a documented guard.
        var state = NewStateWithMain();
        var main = state.MainAgent!;
        main.LastStopHookTime = Now.AddSeconds(-10);

        var sub = AddSub(state);
        sub.LastPermissionPromptTime = Now.AddSeconds(-1);

        var parent = state.DeriveParentDisplay(Now);
        parent.ShouldNotBe(AgentDisplayState.WaitingPermission);
        parent.ShouldBe(state.DeriveAgentDisplayState(main, Now));
    }

    [Fact]
    public void Threshold_exact_boundary_is_Done()
    {
        // Locked behavior: elapsed > TimedOutThreshold flips to TimedOut. Exactly-at
        // the 2-minute mark still reads as Done — the boundary is strict.
        var state = NewStateWithMain();
        var main = state.MainAgent!;
        main.LastStopHookTime = Now.AddMinutes(-2);

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.Done);
    }

    [Fact]
    public void Activity_after_Stop_does_not_regress_to_Working()
    {
        // Regression guard for the bug where LastEventKind was a stored field: a late
        // Activity event with a timestamp newer than LastActivityEventTime but OLDER
        // than the most recent Stop used to flip LastEventKind back to Activity.
        // Now that LastEventKind is derived from Max(timestamps), it can't regress.
        var state = NewStateWithMain();
        var main = state.MainAgent!;

        main.StampActivity(Now.AddSeconds(-100));
        main.StampStop(Now.AddSeconds(-50));
        // Late Activity arrives — newer than the previous Activity stamp but older
        // than the Stop. Must NOT cause the main to read as Working.
        main.StampActivity(Now.AddSeconds(-70));

        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.Done);

        // Sanity: a genuinely newer Activity (resumed working) does flip back.
        main.StampActivity(Now.AddSeconds(-5));
        state.DeriveParentDisplay(Now).ShouldBe(AgentDisplayState.Working);
    }

    [Fact]
    public void Subagent_stays_Done_after_Stop_even_if_old_Activity_arrives_late()
    {
        // Bug-shape regression test: a subagent's Task ToolEnd stamped Stop at +100s,
        // then a late Activity event with timestamp +80s arrives (e.g., transcript
        // replay, AgentMetadataUpdate). Previously this overwrote LastEventKind back
        // to Activity, leaving the subagent row stuck rendering "Working" forever.
        var state = NewStateWithMain();
        var sub = AddSub(state);

        sub.StampActivity(Now.AddSeconds(-150));
        sub.StampStop(Now.AddSeconds(-100));
        sub.StampActivity(Now.AddSeconds(-120)); // late arrival, older than Stop

        state.DeriveAgentDisplayState(sub, Now).ShouldBe(AgentDisplayState.Done);
    }
}
