using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Services.Spark;
using TerminalHost.Core.Spark;
using TerminalHost.Tests.TestAdapters;

namespace TerminalHost.Tests.Services.Spark;

/// <summary>
/// Boundary tests for <see cref="SparkPayloadComposer"/> and the per-variant
/// <see cref="CanvasJsonProtocol"/> wire shape. Runs in-memory against
/// <see cref="FakeSessionCatalog"/> — zero UI / WebView / file I/O.
/// </summary>
public class SparkPayloadComposerTests
{
    private static LiveSessionSnapshot Live(string id, string? model = "claude-opus-4")
    {
        var agents = new Dictionary<string, SnapshotAgent>
        {
            [id] = new SnapshotAgent { Id = id, Name = "main", IsMain = true, State = "Active", Model = model }
        };
        return new LiveSessionSnapshot
        {
            SessionId = id,
            Lifecycle = "Active",
            WorkingDirectory = "/proj/" + id,
            Agents = agents
        };
    }

    private static ReplaySessionSnapshot Replay(string id)
    {
        var agents = new Dictionary<string, SnapshotAgent>
        {
            [id] = new SnapshotAgent { Id = id, Name = "main", IsMain = true, State = "Complete", Model = "claude-opus-4" }
        };
        return new ReplaySessionSnapshot
        {
            SessionId = id,
            Lifecycle = "Completed",
            Agents = agents
        };
    }

    private static PlaceholderSessionSnapshot Placeholder(string id) =>
        new()
        {
            SessionId = id,
            Lifecycle = "Active",
            Agents = new Dictionary<string, SnapshotAgent>
            {
                [id] = new SnapshotAgent { Id = id, Name = "main", IsMain = true, State = "Active" }
            }
        };

    // -------- ComposeOpenAsync --------

    [Fact]
    public async Task ComposeOpenAsync_TrackedSession_EmitsClearThenLoadState()
    {
        var live = Live("s1");
        var catalog = new FakeSessionCatalog().With("s1", live);
        var composer = new SparkPayloadComposer(catalog);

        var messages = await composer.ComposeOpenAsync("s1");

        messages.Count.ShouldBe(2);
        messages[0].ShouldBeOfType<CanvasOutbound.Clear>();
        var loadState = messages[1].ShouldBeOfType<CanvasOutbound.LoadState>();
        loadState.Session.ShouldBeOfType<LiveSessionSnapshot>();
        loadState.Session.SessionId.ShouldBe("s1");
    }

    [Fact]
    public async Task ComposeOpenAsync_UntrackedButTimelineKnowsSession_EmitsClearThenLoadStateWithPlaceholder()
    {
        var catalog = new FakeSessionCatalog().With("s1", Placeholder("s1"));
        var composer = new SparkPayloadComposer(catalog);

        var messages = await composer.ComposeOpenAsync("s1");

        messages.Count.ShouldBe(2);
        messages[0].ShouldBeOfType<CanvasOutbound.Clear>();
        var loadState = messages[1].ShouldBeOfType<CanvasOutbound.LoadState>();
        loadState.Session.ShouldBeOfType<PlaceholderSessionSnapshot>();
        loadState.Session.SessionId.ShouldBe("s1");
    }

    [Fact]
    public async Task ComposeOpenAsync_UnknownSession_EmitsClearThenSetSessionWaiting()
    {
        var catalog = new FakeSessionCatalog();
        var composer = new SparkPayloadComposer(catalog);

        var messages = await composer.ComposeOpenAsync("missing");

        messages.Count.ShouldBe(2);
        messages[0].ShouldBeOfType<CanvasOutbound.Clear>();
        var setSession = messages[1].ShouldBeOfType<CanvasOutbound.SetSession>();
        setSession.Id.ShouldBe("missing");
        setSession.DisplayName.ShouldBe("Waiting for session data...");
    }

    [Fact]
    public async Task ComposeOpenAsync_UnenrichedMainAgent_TriggersEnrichOnce_ThenComposes()
    {
        // The composer owns a per-instance HashSet that tracks which sessions it
        // has already attempted to enrich. The fake catalog records every call so
        // we can verify the dedup is local to ComposeOpenAsync, not orchestrator-level.
        var catalog = new EnrichOnLookupCatalog();
        catalog.SeedUnenriched("s1");
        var composer = new SparkPayloadComposer(catalog);

        await composer.ComposeOpenAsync("s1");
        await composer.ComposeOpenAsync("s1");
        await composer.ComposeOpenAsync("s1");

        catalog.EnrichCallCounts["s1"].ShouldBe(1);
    }

    // -------- ComposeReplayAsync --------

    [Fact]
    public async Task ComposeReplayAsync_NoEventsParsed_ReturnsNull()
    {
        var catalog = new FakeSessionCatalog(); // no replay registered → returns null
        var composer = new SparkPayloadComposer(catalog);

        var result = await composer.ComposeReplayAsync("/tmp/missing.jsonl");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ComposeReplayAsync_HappyPath_EmitsClearThenLoadReplay()
    {
        var replay = Replay("rs1");
        var events = new List<EventPayload>
        {
            new EventPayload { Type = "ToolCallStart", SessionId = "rs1" }
        };
        var catalog = new FakeSessionCatalog()
            .WithReplay("/tmp/x.jsonl", new ReplayLoadResult(replay, events));
        var composer = new SparkPayloadComposer(catalog);

        var result = await composer.ComposeReplayAsync("/tmp/x.jsonl");

        result.ShouldNotBeNull();
        result!.SessionId.ShouldBe("rs1");
        result.Messages.Count.ShouldBe(2);
        result.Messages[0].ShouldBeOfType<CanvasOutbound.Clear>();
        var loadReplay = result.Messages[1].ShouldBeOfType<CanvasOutbound.LoadReplay>();
        loadReplay.Session.ShouldBeOfType<ReplaySessionSnapshot>();
        loadReplay.Session.SessionId.ShouldBe("rs1");
        loadReplay.Events.Count.ShouldBe(1);
    }

    // -------- ComposeMulti --------

    [Fact]
    public void ComposeMulti_FromAvailableSessions_EmitsClearThenLoadMultiState()
    {
        // Catalog has sA + sC. The list includes sA, sB (unknown — skipped per impl), sC.
        var catalog = new FakeSessionCatalog()
            .With("sA", Live("sA"))
            .With("sC", Live("sC"));
        var composer = new SparkPayloadComposer(catalog);

        var list = new List<SessionListItem>
        {
            new SessionListItem { SessionId = "sA" },
            new SessionListItem { SessionId = "sB" }, // not in catalog
            new SessionListItem { SessionId = "sC" }
        };

        var result = composer.ComposeMulti(list);

        result.SessionIds.ShouldBe(new HashSet<string> { "sA", "sC" }, ignoreOrder: true);
        result.Messages.Count.ShouldBe(2);
        result.Messages[0].ShouldBeOfType<CanvasOutbound.Clear>();
        var multi = result.Messages[1].ShouldBeOfType<CanvasOutbound.LoadMultiState>();
        // Implementation skips nulls (does not substitute placeholders).
        multi.Sessions.Select(s => s.SessionId).ShouldBe(new[] { "sA", "sC" });
    }

    // -------- ProjectEvent --------

    [Fact]
    public void ProjectEvent_ActivityEvent_ProducesEventPayload_WithDeepClonedData()
    {
        var nested = new Dictionary<string, object?> { ["k"] = "original" };
        var evt = new ActivityEvent
        {
            Type = ActivityEventType.ToolCallStart,
            SessionId = "s1",
            AgentId = "a1",
            Data = new Dictionary<string, object?>
            {
                ["toolName"] = "Read",
                ["nested"] = nested
            }
        };

        var catalog = new FakeSessionCatalog();
        var composer = new SparkPayloadComposer(catalog);

        var payload = composer.ProjectEvent(evt);

        // Mutate the source AFTER composing — the payload must not observe the mutation.
        evt.Data["toolName"] = "MUTATED";
        evt.Data["new-key"] = "added-after";
        nested["k"] = "mutated";

        payload.Type.ShouldBe("ToolCallStart");
        payload.SessionId.ShouldBe("s1");
        payload.AgentId.ShouldBe("a1");
        payload.Data["toolName"].ShouldBe("Read");
        payload.Data.ContainsKey("new-key").ShouldBeFalse();

        var clonedNested = payload.Data["nested"].ShouldBeAssignableTo<IReadOnlyDictionary<string, object?>>();
        clonedNested!["k"].ShouldBe("original");
    }

    // -------- CanvasJsonProtocol wire shape --------

    [Fact]
    public void Serialize_DropsIsReplayFromWire()
    {
        var live = Live("s1");
        var replay = Replay("rs1");
        var placeholder = Placeholder("ph1");

        var liveJson = CanvasJsonProtocol.Serialize(new CanvasOutbound.LoadState(live));
        var replayJson = CanvasJsonProtocol.Serialize(new CanvasOutbound.LoadReplay(replay, new List<EventPayload>()));
        var multiJson = CanvasJsonProtocol.Serialize(new CanvasOutbound.LoadMultiState(new List<SnapshotEnvelope> { live, replay, placeholder }));

        liveJson.ShouldNotContain("isReplay");
        replayJson.ShouldNotContain("isReplay");
        multiJson.ShouldNotContain("isReplay");
    }

    [Fact]
    public void Serialize_PreservesJsConsumedFields()
    {
        // The JS canvas reads state.workingDirectory, state.agents, state.toolCalls,
        // and state.sessionId. Confirm each field exists at the expected path.
        var live = Live("s1");
        // Give it a tool call so toolCalls is non-empty (still serializes as {} if empty).
        var toolCalls = new Dictionary<string, SnapshotToolCall>
        {
            ["tu1"] = new SnapshotToolCall { ToolUseId = "tu1", ToolName = "Read", State = "Running" }
        };
        live = live with { ToolCalls = toolCalls };

        var json = CanvasJsonProtocol.Serialize(new CanvasOutbound.LoadState(live));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("action").GetString().ShouldBe("loadState");
        var state = root.GetProperty("state");
        state.GetProperty("sessionId").GetString().ShouldBe("s1");
        state.GetProperty("workingDirectory").GetString().ShouldBe("/proj/s1");
        state.GetProperty("agents").ValueKind.ShouldBe(JsonValueKind.Object);
        state.GetProperty("toolCalls").ValueKind.ShouldBe(JsonValueKind.Object);
        state.GetProperty("toolCalls").TryGetProperty("tu1", out _).ShouldBeTrue();
    }

    // The "live variant excludes completed tool calls" rule is enforced by
    // TimelineSessionCatalog.ProjectLive — it filters out non-Running calls when
    // constructing the LiveSessionSnapshot. The composer + JSON protocol layers
    // do NOT re-filter (they trust the snapshot is already correctly shaped).
    //
    // Since the filter happens at the catalog projection (a layer outside this
    // file's contract), we exercise it at the data-shape level: a LiveSessionSnapshot's
    // ToolCalls dictionary, by contract, should only contain Running entries — and
    // whatever is in that dictionary lands intact in the serialized state.toolCalls.
    [Fact]
    public void Serialize_LiveVariant_PreservesToolCallsDictionaryAsIs()
    {
        // Filter happens at TimelineSessionCatalog.ProjectLive — outside the composer.
        // The composer/serializer faithfully forward whatever the catalog produced.
        // This test pins that contract: if the snapshot says toolCalls = {tu1:Running},
        // the wire JSON contains exactly that. (A future regression where the protocol
        // started dropping fields would be caught here.)
        var running = new SnapshotToolCall { ToolUseId = "tu1", ToolName = "Read", State = "Running" };
        var completed = new SnapshotToolCall { ToolUseId = "tu2", ToolName = "Write", State = "Complete" };
        var live = Live("s1") with
        {
            ToolCalls = new Dictionary<string, SnapshotToolCall>
            {
                ["tu1"] = running,
                ["tu2"] = completed
            }
        };

        var json = CanvasJsonProtocol.Serialize(new CanvasOutbound.LoadState(live));

        using var doc = JsonDocument.Parse(json);
        var toolCalls = doc.RootElement.GetProperty("state").GetProperty("toolCalls");
        toolCalls.TryGetProperty("tu1", out _).ShouldBeTrue();
        // Both entries pass through at this layer; the catalog is the layer that filters.
        toolCalls.TryGetProperty("tu2", out _).ShouldBeTrue();
    }

    // -------- Helpers --------

    /// <summary>
    /// Catalog variant that returns an unenriched snapshot (model == null on main agent)
    /// until <see cref="EnrichAsync"/> is called — then upgrades it. Used to verify
    /// the composer's one-shot enrichment dedup.
    /// </summary>
    private sealed class EnrichOnLookupCatalog : ISessionCatalog
    {
        private readonly Dictionary<string, bool> _enriched = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> EnrichCallCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void SeedUnenriched(string sessionId) => _enriched[sessionId] = false;

        public IReadOnlyList<SessionListItem> List() => Array.Empty<SessionListItem>();

        public SnapshotEnvelope? GetSnapshot(string sessionId)
        {
            if (!_enriched.TryGetValue(sessionId, out var enriched)) return null;
            var model = enriched ? "claude-opus-4" : null;
            return new LiveSessionSnapshot
            {
                SessionId = sessionId,
                Lifecycle = "Active",
                Agents = new Dictionary<string, SnapshotAgent>
                {
                    [sessionId] = new SnapshotAgent { Id = sessionId, Name = "main", IsMain = true, State = "Active", Model = model }
                }
            };
        }

        public Task<ReplayLoadResult?> LoadReplayAsync(string jsonlPath, CancellationToken ct) =>
            Task.FromResult<ReplayLoadResult?>(null);

        public Task EnrichAsync(string sessionId, CancellationToken ct)
        {
            EnrichCallCounts.TryGetValue(sessionId, out var n);
            EnrichCallCounts[sessionId] = n + 1;
            _enriched[sessionId] = true;
            return Task.CompletedTask;
        }
    }
}
