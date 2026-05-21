using System;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

/// <summary>
/// M1 — verifies that SessionActivityService stamps the per-agent input timestamps
/// (LastActivityEventTime, LastStopHookTime, LastPermissionPromptTime, LastSubagentStopTime,
/// LastEventKind) that the M2 derivation will consume. Purely additive: existing
/// Lifecycle/AgentState behavior is not asserted here.
/// </summary>
public class SessionActivityServiceTimestampsTests
{
    private const string SessionId = "sess-ts-1";
    private const string SubagentId = "subagent-1";

    private static HookEvent Make(HookEventType type, DateTime ts, string? toolName = null, string? toolUseId = null, string? agentId = null, string? notificationType = null) => new()
    {
        EventType = type,
        SessionId = SessionId,
        Timestamp = ts,
        ToolName = toolName,
        ToolUseId = toolUseId,
        AgentId = agentId,
        NotificationType = notificationType,
        Cwd = "/tmp",
    };

    [Fact]
    public void StopAfterToolCalls_StampsActivityAndStop_OnMainAgent()
    {
        var svc = new SessionActivityService();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        svc.ProcessHookEvent(Make(HookEventType.SessionStart, t0));
        svc.ProcessHookEvent(Make(HookEventType.ToolStart, t0.AddSeconds(1), toolName: "Read", toolUseId: "tool-1"));
        svc.ProcessHookEvent(Make(HookEventType.ToolEnd, t0.AddSeconds(2), toolName: "Read", toolUseId: "tool-1"));
        svc.ProcessHookEvent(Make(HookEventType.SessionStop, t0.AddSeconds(3)));

        var main = svc.GetState(SessionId)!.MainAgent!;
        main.LastActivityEventTime.ShouldBe(t0.AddSeconds(2));
        main.LastStopHookTime.ShouldBe(t0.AddSeconds(3));
        main.LastEventKind.ShouldBe(AgentEventKind.Stop);
    }

    [Fact]
    public void SubagentStart_StampsActivity_OnSubagentAndMainAgent()
    {
        var svc = new SessionActivityService();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        svc.ProcessHookEvent(Make(HookEventType.SessionStart, t0));
        svc.ProcessHookEvent(Make(HookEventType.SubagentStart, t0.AddSeconds(5), agentId: SubagentId));

        var state = svc.GetState(SessionId)!;
        state.Agents[SubagentId].LastActivityEventTime.ShouldBe(t0.AddSeconds(5));
        state.Agents[SubagentId].LastEventKind.ShouldBe(AgentEventKind.Activity);
        state.MainAgent!.LastActivityEventTime.ShouldBe(t0.AddSeconds(5));
        state.MainAgent.LastEventKind.ShouldBe(AgentEventKind.Activity);
    }

    [Fact]
    public void PermissionPrompt_StampsMainAgentOnly_NeverSubagent()
    {
        var svc = new SessionActivityService();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        svc.ProcessHookEvent(Make(HookEventType.SessionStart, t0));
        svc.ProcessHookEvent(Make(HookEventType.SubagentStart, t0.AddSeconds(1), agentId: SubagentId));
        svc.ProcessHookEvent(Make(HookEventType.Notification, t0.AddSeconds(2), notificationType: "permission_prompt"));

        var state = svc.GetState(SessionId)!;
        state.MainAgent!.LastPermissionPromptTime.ShouldBe(t0.AddSeconds(2));
        state.MainAgent.LastEventKind.ShouldBe(AgentEventKind.PermissionPrompt);
        // Invariant: subagent must NEVER be stamped with a permission prompt.
        state.Agents[SubagentId].LastPermissionPromptTime.ShouldBeNull();
        state.Agents[SubagentId].LastEventKind.ShouldNotBe(AgentEventKind.PermissionPrompt);
    }

    [Fact]
    public void OutOfOrderToolEnd_DoesNotRegressActivityTimestamp()
    {
        var svc = new SessionActivityService();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        svc.ProcessHookEvent(Make(HookEventType.SessionStart, t0));
        // Newer ToolEnd arrives first…
        svc.ProcessHookEvent(Make(HookEventType.ToolEnd, t0.AddSeconds(10), toolName: "Read", toolUseId: "tool-a"));
        // …then an out-of-order older ToolEnd.
        svc.ProcessHookEvent(Make(HookEventType.ToolEnd, t0.AddSeconds(5), toolName: "Read", toolUseId: "tool-b"));

        var main = svc.GetState(SessionId)!.MainAgent!;
        main.LastActivityEventTime.ShouldBe(t0.AddSeconds(10));
        main.LastEventKind.ShouldBe(AgentEventKind.Activity);
    }
}
