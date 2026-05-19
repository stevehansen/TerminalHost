using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Services.Spark;
using TerminalHost.Core.Spark;
using TerminalHost.Tests.TestAdapters;

namespace TerminalHost.Tests.Services.Spark;

/// <summary>
/// Boundary tests for <see cref="SparkCanvasOrchestrator"/>. The orchestrator runs
/// against the in-memory transport, fake catalog, and in-memory theme store —
/// zero UI framework references.
/// </summary>
public class SparkCanvasOrchestratorTests
{
    private static SessionSnapshot Snap(string id, string lifecycle = "Active", string? model = "claude-opus-4")
    {
        var agents = new Dictionary<string, SnapshotAgent>
        {
            [id] = new SnapshotAgent { Id = id, Name = "main", IsMain = true, State = "Active", Model = model }
        };
        return new SessionSnapshot
        {
            SessionId = id,
            Lifecycle = lifecycle,
            Agents = agents
        };
    }

    private static (SparkCanvasOrchestrator orch, InMemoryCanvasTransport transport, FakeSessionCatalog catalog,
        InMemoryThemeStore theme, FakeSessionActivityService activity)
        BuildSut(params (string id, SessionSnapshot snap)[] sessions)
    {
        var catalog = new FakeSessionCatalog();
        foreach (var (id, snap) in sessions) catalog.With(id, snap);
        var theme = new InMemoryThemeStore("holographic");
        var activity = new FakeSessionActivityService();
        var orch = new SparkCanvasOrchestrator(catalog, activity, theme);
        var transport = new InMemoryCanvasTransport();
        orch.Attach(transport);
        return (orch, transport, catalog, theme, activity);
    }

    // -------- Mode transitions --------

    [Fact]
    public async Task OpenSessionAsync_FromEmpty_EmitsClearAndLoadState_AndTransitionsToSingle()
    {
        var (orch, transport, _, _, _) = BuildSut(("s1", Snap("s1")));
        transport.MarkReady();
        transport.ClearSent();

        await orch.OpenSessionAsync("s1");

        orch.State.ShouldBeOfType<CanvasState.Single>().SessionId.ShouldBe("s1");
        transport.Sent.Select(m => m.GetType().Name).ShouldBe(new[]
        {
            nameof(CanvasOutbound.Clear),
            nameof(CanvasOutbound.LoadState)
        });
    }

    [Fact]
    public void AutoConnect_FirstSessionStart_TransitionsEmptyToSingle()
    {
        var (orch, transport, _, _, activity) = BuildSut(("s1", Snap("s1")));
        transport.MarkReady();
        transport.ClearSent();

        var evt = ActivityEvent.CreateSessionStart("s1", cwd: null, transcriptPath: null);
        activity.RaiseActivityEvent(evt);

        // RaiseActivityEvent → OnActivityEventBackground → transport.Post (sync) →
        // HandleActivityEvent (sync) → TransitionTo (sync). State is set before return.
        orch.State.ShouldBeOfType<CanvasState.Single>().SessionId.ShouldBe("s1");
    }

    [Fact]
    public async Task EnterMultiMode_AfterSingleSession_EmitsClearAndLoadMultiState()
    {
        var (orch, transport, _, _, _) = BuildSut(
            ("s1", Snap("s1")),
            ("s2", Snap("s2")));
        transport.MarkReady();
        await orch.OpenSessionAsync("s1");
        transport.ClearSent();

        await orch.EnterMultiModeAsync();

        orch.State.ShouldBeOfType<CanvasState.Multi>().SessionIds.Count.ShouldBe(2);
        transport.Sent.Select(m => m.GetType().Name).ShouldBe(new[]
        {
            nameof(CanvasOutbound.Clear),
            nameof(CanvasOutbound.LoadMultiState)
        });
    }

    [Fact]
    public async Task RequestMultiMode_FromInbound_TransitionsToMulti()
    {
        var (orch, transport, _, _, _) = BuildSut(("s1", Snap("s1")));
        transport.MarkReady();
        await orch.OpenSessionAsync("s1");

        transport.Inject(new CanvasInbound.RequestMultiMode());

        orch.State.ShouldBeOfType<CanvasState.Multi>();
    }

    [Fact]
    public async Task SelectSession_FromInbound_FromMulti_TransitionsToSingle()
    {
        var (orch, transport, _, _, _) = BuildSut(
            ("s1", Snap("s1")),
            ("s2", Snap("s2")));
        transport.MarkReady();
        await orch.EnterMultiModeAsync();

        transport.Inject(new CanvasInbound.SelectSession("s2"));

        orch.State.ShouldBeOfType<CanvasState.Single>().SessionId.ShouldBe("s2");
    }

    [Fact]
    public async Task OpenJsonl_FromAnyState_TransitionsToReplay()
    {
        var replaySnap = Snap("replay-session", "Completed");
        var catalog = new FakeSessionCatalog()
            .WithReplay("/tmp/x.jsonl", new ReplayLoadResult(replaySnap, new List<EventPayload>()));
        var theme = new InMemoryThemeStore();
        var activity = new FakeSessionActivityService();
        var orch = new SparkCanvasOrchestrator(catalog, activity, theme);
        var transport = new InMemoryCanvasTransport();
        orch.Attach(transport);
        transport.MarkReady();
        transport.ClearSent();

        await orch.OpenJsonlAsync("/tmp/x.jsonl");

        var replay = orch.State.ShouldBeOfType<CanvasState.Replay>();
        replay.FilePath.ShouldBe("/tmp/x.jsonl");
        replay.SessionId.ShouldBe("replay-session");
        transport.Sent.Select(m => m.GetType().Name).ShouldBe(new[]
        {
            nameof(CanvasOutbound.Clear),
            nameof(CanvasOutbound.LoadReplay)
        });
    }

    [Fact]
    public async Task SelectSession_FromInbound_FromReplay_TransitionsToSingle()
    {
        var replaySnap = Snap("replay-session", "Completed");
        var catalog = new FakeSessionCatalog()
            .With("s1", Snap("s1"))
            .WithReplay("/tmp/x.jsonl", new ReplayLoadResult(replaySnap, new List<EventPayload>()));
        var theme = new InMemoryThemeStore();
        var activity = new FakeSessionActivityService();
        var orch = new SparkCanvasOrchestrator(catalog, activity, theme);
        var transport = new InMemoryCanvasTransport();
        orch.Attach(transport);
        transport.MarkReady();

        await orch.OpenJsonlAsync("/tmp/x.jsonl");
        orch.State.ShouldBeOfType<CanvasState.Replay>();

        transport.Inject(new CanvasInbound.SelectSession("s1"));

        orch.State.ShouldBeOfType<CanvasState.Single>().SessionId.ShouldBe("s1");
    }

    // -------- Event routing --------

    [Fact]
    public async Task EventRouting_Single_OnlyForwardsForCurrentSession()
    {
        var (orch, transport, _, _, activity) = BuildSut(
            ("sA", Snap("sA")),
            ("sB", Snap("sB")));
        transport.MarkReady();
        await orch.OpenSessionAsync("sA");
        transport.ClearSent();

        // Event for sA: forwarded
        activity.RaiseActivityEvent(ActivityEvent.CreateToolCallStart("sA", "sA", "tu1", "Read", null));
        // Event for sB: dropped
        activity.RaiseActivityEvent(ActivityEvent.CreateToolCallStart("sB", "sB", "tu2", "Read", null));

        transport.Sent.Count(m => m is CanvasOutbound.Event).ShouldBe(1);
    }

    [Fact]
    public async Task EventRouting_Multi_ForwardsAllSessions()
    {
        var (orch, transport, _, _, activity) = BuildSut(
            ("sA", Snap("sA")),
            ("sB", Snap("sB")));
        transport.MarkReady();
        await orch.EnterMultiModeAsync();
        transport.ClearSent();

        activity.RaiseActivityEvent(ActivityEvent.CreateToolCallStart("sA", "sA", "tu1", "Read", null));
        activity.RaiseActivityEvent(ActivityEvent.CreateToolCallStart("sB", "sB", "tu2", "Read", null));

        transport.Sent.Count(m => m is CanvasOutbound.Event).ShouldBe(2);
    }

    [Fact]
    public async Task EventRouting_Replay_DoesNotForwardLiveEvents()
    {
        var replaySnap = Snap("replay-session", "Completed");
        var catalog = new FakeSessionCatalog()
            .WithReplay("/tmp/x.jsonl", new ReplayLoadResult(replaySnap, new List<EventPayload>()));
        var theme = new InMemoryThemeStore();
        var activity = new FakeSessionActivityService();
        var orch = new SparkCanvasOrchestrator(catalog, activity, theme);
        var transport = new InMemoryCanvasTransport();
        orch.Attach(transport);
        transport.MarkReady();

        await orch.OpenJsonlAsync("/tmp/x.jsonl");
        transport.ClearSent();

        activity.RaiseActivityEvent(ActivityEvent.CreateToolCallStart("replay-session", "x", "tu1", "Read", null));

        transport.Sent.OfType<CanvasOutbound.Event>().ShouldBeEmpty();
    }

    // -------- Protocol bridge --------

    [Fact]
    public async Task OutboundQueuedBeforeReady_FlushOnReady()
    {
        var (orch, transport, _, _, _) = BuildSut(("s1", Snap("s1")));
        // Note: transport is NOT yet ready

        var t = orch.OpenSessionAsync("s1"); // queues Clear + LoadState
        await t;

        transport.Sent.ShouldBeEmpty(); // nothing observed yet
        transport.MarkReady();

        transport.Sent.Select(m => m.GetType().Name).ShouldContain(nameof(CanvasOutbound.Clear));
        transport.Sent.Select(m => m.GetType().Name).ShouldContain(nameof(CanvasOutbound.LoadState));
    }

    [Fact]
    public async Task RefreshSessions_PushesSessionList()
    {
        var (orch, transport, _, _, _) = BuildSut(
            ("s1", Snap("s1")),
            ("s2", Snap("s2")));
        transport.MarkReady();
        transport.ClearSent();

        await orch.RefreshSessionsAsync();

        var msg = transport.Sent.OfType<CanvasOutbound.SessionList>().ShouldHaveSingleItem();
        msg.Sessions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AllVerbs_RoundTripThroughTransport()
    {
        var (_, transport, _, _, _) = BuildSut();
        transport.MarkReady();
        transport.ClearSent();

        var snap = Snap("s1");
        var verbs = new CanvasOutbound[]
        {
            new CanvasOutbound.Clear(),
            new CanvasOutbound.LoadState(snap),
            new CanvasOutbound.LoadReplay(snap, new List<EventPayload>()),
            new CanvasOutbound.Event(new EventPayload { Type = "ToolCallStart", SessionId = "s1" }),
            new CanvasOutbound.SetTheme("neon"),
            new CanvasOutbound.SetSession("s1", "main"),
            new CanvasOutbound.SessionList(new List<SessionListItem>()),
            new CanvasOutbound.LoadMultiState(new List<SessionSnapshot> { snap })
        };

        foreach (var v in verbs) await transport.SendAsync(v);

        transport.Sent.Count.ShouldBe(verbs.Length);
        for (int i = 0; i < verbs.Length; i++)
            transport.Sent[i].GetType().ShouldBe(verbs[i].GetType());
    }

    [Fact]
    public void JsonProtocol_AllOutboundVerbs_Serialize()
    {
        var snap = Snap("s1");
        // Should not throw and should produce non-empty JSON for every variant.
        foreach (var v in new CanvasOutbound[]
        {
            new CanvasOutbound.Clear(),
            new CanvasOutbound.LoadState(snap),
            new CanvasOutbound.LoadReplay(snap, new List<EventPayload>()),
            new CanvasOutbound.Event(new EventPayload { Type = "ToolCallStart", SessionId = "s1" }),
            new CanvasOutbound.SetTheme("neon"),
            new CanvasOutbound.SetSession("s1", "main"),
            new CanvasOutbound.SessionList(new List<SessionListItem>()),
            new CanvasOutbound.LoadMultiState(new List<SessionSnapshot> { snap })
        })
        {
            var json = CanvasJsonProtocol.Serialize(v);
            json.ShouldNotBeNullOrEmpty();
            json.ShouldContain("\"action\":");
        }
    }

    [Theory]
    [InlineData("{\"action\":\"ready\"}", typeof(CanvasInbound.Ready))]
    [InlineData("{\"action\":\"refreshSessions\"}", typeof(CanvasInbound.RefreshSessions))]
    [InlineData("{\"action\":\"requestMultiMode\"}", typeof(CanvasInbound.RequestMultiMode))]
    [InlineData("{\"action\":\"exitMultiMode\"}", typeof(CanvasInbound.ExitMultiMode))]
    [InlineData("{\"action\":\"selectSession\",\"sessionId\":\"abc\"}", typeof(CanvasInbound.SelectSession))]
    [InlineData("{\"action\":\"themeChanged\",\"theme\":\"neon\"}", typeof(CanvasInbound.ThemeChanged))]
    public void JsonProtocol_AllInboundVerbs_Parse(string json, System.Type expectedType)
    {
        var parsed = CanvasJsonProtocol.TryParse(json);
        parsed.ShouldNotBeNull();
        parsed.GetType().ShouldBe(expectedType);
    }

    // -------- Async edge cases --------

    [Fact]
    public async Task EnrichOnce_PerSessionPerLifetime_WhenModelIsMissing()
    {
        var snapNoModel = Snap("s1", "Active", model: null);
        var catalog = new FakeSessionCatalog().With("s1", snapNoModel);
        var theme = new InMemoryThemeStore();
        var activity = new FakeSessionActivityService();
        var orch = new SparkCanvasOrchestrator(catalog, activity, theme);
        var transport = new InMemoryCanvasTransport();
        orch.Attach(transport);
        transport.MarkReady();

        await orch.OpenSessionAsync("s1");
        await orch.OpenSessionAsync("s1");
        await orch.OpenSessionAsync("s1");

        catalog.EnrichCallCounts["s1"].ShouldBe(1);
    }

    [Fact]
    public async Task ThemeChanged_Inbound_PersistsToStore_AndDoesNotEchoBack()
    {
        var (_, transport, _, theme, _) = BuildSut();
        transport.MarkReady();
        transport.ClearSent();

        transport.Inject(new CanvasInbound.ThemeChanged("neon"));

        theme.Current.ShouldBe("neon");
        transport.Sent.OfType<CanvasOutbound.SetTheme>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Attach_BeforeReady_DoesNotPushReadySequence()
    {
        var (_, transport, _, _, _) = BuildSut(("s1", Snap("s1")));
        // not ready yet — no theme / session list / setSession pushed
        transport.Sent.ShouldBeEmpty();
        await Task.Yield();
    }

    [Fact]
    public void OnReady_PushesThemeAndSessionList()
    {
        var (_, transport, _, _, _) = BuildSut(("s1", Snap("s1")));
        transport.MarkReady();

        transport.Sent.OfType<CanvasOutbound.SetTheme>().ShouldHaveSingleItem().Theme.ShouldBe("holographic");
        transport.Sent.OfType<CanvasOutbound.SessionList>().ShouldNotBeEmpty();
    }

    // -------- Pure FSM (Reduce) --------
    //
    // The reducer is the single source of truth for state transitions. These tests
    // exercise it directly — no transport, no catalog, no I/O — to anchor the FSM
    // shape against the spec.

    [Fact]
    public void Reduce_Empty_HostOpen_TransitionsToSingle()
    {
        var next = SparkCanvasOrchestrator.Reduce(
            new CanvasState.Empty(),
            new SparkCanvasOrchestrator.Trigger.HostOpen("s1"));

        next.ShouldBeOfType<CanvasState.Single>().SessionId.ShouldBe("s1");
    }

    [Fact]
    public void Reduce_Single_HostOpen_TransitionsToNewSingle()
    {
        var next = SparkCanvasOrchestrator.Reduce(
            new CanvasState.Single("s1"),
            new SparkCanvasOrchestrator.Trigger.HostOpen("s2"));

        next.ShouldBeOfType<CanvasState.Single>().SessionId.ShouldBe("s2");
    }

    [Fact]
    public void Reduce_AnyState_HostJsonl_TransitionsToReplay()
    {
        var next = SparkCanvasOrchestrator.Reduce(
            new CanvasState.Multi(new HashSet<string> { "x" }),
            new SparkCanvasOrchestrator.Trigger.HostJsonl("/tmp/y.jsonl", "y"));

        var replay = next.ShouldBeOfType<CanvasState.Replay>();
        replay.FilePath.ShouldBe("/tmp/y.jsonl");
        replay.SessionId.ShouldBe("y");
    }

    [Fact]
    public void Reduce_AnyState_HostMulti_TransitionsToMulti()
    {
        var ids = new HashSet<string> { "a", "b" };
        var next = SparkCanvasOrchestrator.Reduce(
            new CanvasState.Single("z"),
            new SparkCanvasOrchestrator.Trigger.HostMulti(ids));

        next.ShouldBeOfType<CanvasState.Multi>().SessionIds.ShouldBe(ids);
    }

    [Fact]
    public void Reduce_Multi_HostExitMulti_TransitionsToEmpty()
    {
        var next = SparkCanvasOrchestrator.Reduce(
            new CanvasState.Multi(new HashSet<string> { "a" }),
            new SparkCanvasOrchestrator.Trigger.HostExitMulti());

        next.ShouldBeOfType<CanvasState.Empty>();
    }

    [Fact]
    public void Reduce_Empty_HostExitMulti_StaysEmpty()
    {
        // Redundant exit from a state that doesn't need exiting — should land on Empty.
        var next = SparkCanvasOrchestrator.Reduce(
            new CanvasState.Empty(),
            new SparkCanvasOrchestrator.Trigger.HostExitMulti());

        next.ShouldBeOfType<CanvasState.Empty>();
    }

    [Fact]
    public void Reduce_Empty_ActivityStart_TransitionsToSingle()
    {
        var next = SparkCanvasOrchestrator.Reduce(
            new CanvasState.Empty(),
            new SparkCanvasOrchestrator.Trigger.ActivityStart("s1"));

        next.ShouldBeOfType<CanvasState.Single>().SessionId.ShouldBe("s1");
    }

    [Fact]
    public void Reduce_Single_ActivityStart_DoesNotTransition()
    {
        // Activity-start only auto-connects from Empty; in any other state it's ignored.
        var current = new CanvasState.Single("s1");
        var next = SparkCanvasOrchestrator.Reduce(
            current,
            new SparkCanvasOrchestrator.Trigger.ActivityStart("s2"));

        next.ShouldBe(current);
    }

    [Fact]
    public void Reduce_Replay_ActivityStart_DoesNotTransition()
    {
        var current = new CanvasState.Replay("/tmp/x.jsonl", "x");
        var next = SparkCanvasOrchestrator.Reduce(
            current,
            new SparkCanvasOrchestrator.Trigger.ActivityStart("y"));

        next.ShouldBe(current);
    }
}
