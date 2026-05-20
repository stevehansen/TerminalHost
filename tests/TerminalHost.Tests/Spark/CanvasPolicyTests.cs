using System.Collections.Generic;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Spark;

namespace TerminalHost.Tests.Spark;

/// <summary>
/// Direct tests for <see cref="CanvasPolicy"/>'s pure routing and ready-time
/// decisions. No transport, no catalog, no fakes — just (state, input) → output.
/// </summary>
public class CanvasPolicyTests
{
    private static SessionListItem Item(string id, bool isLive) =>
        new() { SessionId = id, IsLive = isLive };

    // -------- ShouldForward --------

    [Fact]
    public void ShouldForward_Single_MatchingSessionId_ReturnsTrue()
    {
        var state = new CanvasState.Single("s1");
        var evt = ActivityEvent.CreateToolCallStart("s1", "s1", "tu1", "Read", null);

        CanvasPolicy.ShouldForward(state, evt).ShouldBeTrue();
    }

    [Fact]
    public void ShouldForward_Single_NonMatchingSessionId_ReturnsFalse()
    {
        var state = new CanvasState.Single("s1");
        var evt = ActivityEvent.CreateToolCallStart("s2", "s2", "tu1", "Read", null);

        CanvasPolicy.ShouldForward(state, evt).ShouldBeFalse();
    }

    [Fact]
    public void ShouldForward_Multi_ForwardsEventForSessionNotInSet()
    {
        // The director forwards in Multi mode; placeholder creation for unknown
        // sessions is the canvas's job.
        var state = new CanvasState.Multi(new HashSet<string> { "s1", "s2" });
        var evt = ActivityEvent.CreateToolCallStart("s3", "s3", "tu1", "Read", null);

        CanvasPolicy.ShouldForward(state, evt).ShouldBeTrue();
    }

    [Fact]
    public void ShouldForward_Replay_ReturnsFalse()
    {
        var state = new CanvasState.Replay("/tmp/x.jsonl", "rsid");
        var evt = ActivityEvent.CreateToolCallStart("rsid", "rsid", "tu1", "Read", null);

        CanvasPolicy.ShouldForward(state, evt).ShouldBeFalse();
    }

    [Fact]
    public void ShouldForward_Empty_ReturnsFalse()
    {
        // Auto-connect from Empty is the reducer's job, not ShouldForward's.
        var state = new CanvasState.Empty();
        var evt = ActivityEvent.CreateSessionStart("any", null, null);

        CanvasPolicy.ShouldForward(state, evt).ShouldBeFalse();
    }

    // -------- AutoOpenOnReady --------

    [Fact]
    public void AutoOpenOnReady_Single_ReturnsItsSessionId()
    {
        var state = new CanvasState.Single("s1");
        var sessions = new List<SessionListItem> { Item("other", isLive: true) };

        CanvasPolicy.AutoOpenOnReady(state, sessions).ShouldBe("s1");
    }

    [Fact]
    public void AutoOpenOnReady_Empty_WithLiveSession_ReturnsFirstLiveSessionId()
    {
        var state = new CanvasState.Empty();
        var sessions = new List<SessionListItem>
        {
            Item("a", isLive: false),
            Item("b", isLive: true),
            Item("c", isLive: true),
        };

        CanvasPolicy.AutoOpenOnReady(state, sessions).ShouldBe("b");
    }

    [Fact]
    public void AutoOpenOnReady_Empty_NoLiveSessions_ReturnsFirstSessionId()
    {
        var state = new CanvasState.Empty();
        var sessions = new List<SessionListItem>
        {
            Item("a", isLive: false),
            Item("b", isLive: false),
        };

        CanvasPolicy.AutoOpenOnReady(state, sessions).ShouldBe("a");
    }

    [Fact]
    public void AutoOpenOnReady_Empty_NoSessions_ReturnsNull()
    {
        var state = new CanvasState.Empty();
        var sessions = new List<SessionListItem>();

        CanvasPolicy.AutoOpenOnReady(state, sessions).ShouldBeNull();
    }

    [Fact]
    public void AutoOpenOnReady_Multi_ReturnsNull()
    {
        var state = new CanvasState.Multi(new HashSet<string> { "s1", "s2" });
        var sessions = new List<SessionListItem> { Item("s1", isLive: true) };

        CanvasPolicy.AutoOpenOnReady(state, sessions).ShouldBeNull();
    }

    [Fact]
    public void AutoOpenOnReady_Replay_ReturnsNull()
    {
        var state = new CanvasState.Replay("/tmp/x.jsonl", "rsid");
        var sessions = new List<SessionListItem> { Item("rsid", isLive: false) };

        CanvasPolicy.AutoOpenOnReady(state, sessions).ShouldBeNull();
    }
}
