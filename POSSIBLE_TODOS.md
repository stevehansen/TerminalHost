# Possible TODOs — Spark Canvas architecture

Candidates surfaced by `/improve-codebase-architecture for the Spark feature` on 2026-05-18. Each entry is a deepening opportunity — promote to a GitHub issue when ready to act on it.

Shared context for all four:

- WPF VM: `src/TerminalHost/TerminalHost/ViewModels/SparkCanvasViewModel.cs` (627 lines)
- Avalonia VM: `src/TerminalHost.Avalonia/ViewModels/SparkCanvasViewModel.cs` (621 lines)
- Views: `SparkCanvasView.{xaml,axaml}.cs`, `SparkCanvasWindow.{xaml,axaml}.cs`
- Web assets: `src/TerminalHost/TerminalHost/web/spark/` (canvas.js, events.js, ui.js, fx.js, panels.js, simulation.js, themes.js, style.css, index.html — ~9,500 lines total)
- Spec: `docs/specs/SparkCanvas.md`
- Test coverage today: **zero** (no `*Spark*` or `*Canvas*` tests under `tests/`)

---

## A. Unify WPF / Avalonia `SparkCanvasViewModel` into Core

### Cluster
- `src/TerminalHost/TerminalHost/ViewModels/SparkCanvasViewModel.cs`
- `src/TerminalHost.Avalonia/ViewModels/SparkCanvasViewModel.cs`

### Why coupled
~99% literal duplication. Both files import the same Core namespaces, expose the same `BasePanelViewModel` surface, depend on the same four services (`ISessionActivityService`, `IApiServer`, `ITimelineService`, `IConfigurationService`), and emit the same JSON payloads. The only legitimately platform-specific code lives in the *Views* (WebView2 vs Avalonia WebView bridge plumbing) — not the VM.

### Dependency category
**In-process.** Move the file to `TerminalHost.Core/ViewModels/`. Both apps already reference Core.

### Test impact
Replaces two duplicate (and untested) surfaces with one. Lays the groundwork for adding tests once.

### Honest read
This is deduplication, not a real "deepening." Low architectural payoff but cheap to do and unblocks tests. Best done **after** B/C/D, since each of those would otherwise need to be applied twice.

---

## B. Extract `ISparkPayloadComposer` (DTO mapping layer)

> ✅ **Implemented (2026-05-20).** Shipped as part of the same session that hardened the design.
> History: PR #66/#67 (commit `4bd8337`, ports-and-adapters refactor) already solved ~80% of the original task. The remaining flag-parameter + repeated-choreography smells are now fixed: typed snapshot variants (Live/Replay/Placeholder over `SnapshotEnvelope`), `ISparkPayloadComposer` bundles the `Clear → LoadState/SetSession` sequence + three-tier fallback + one-shot enrichment retry, `MultiComposition` / `ReplayComposition` records carry resolved session ids out of the composer (no message-list mining), JSON wire drops the dead `isReplay` field.

### Current state of play

The 627-line `SparkCanvasViewModel` no longer exists. As of `4bd8337`:

- VM is 100 lines (`src/TerminalHost.Core/ViewModels/SparkCanvasViewModel.cs`). Task A is done.
- `SerializeState`, `SerializeStateForReplay`, `SerializeEvent`, and the inline anonymous DTOs are **gone**.
- Projection from `SessionActivityState` → wire shape now lives in `TimelineSessionCatalog.Project(state, isReplay)` (~80 lines) and `ProjectPlaceholder(LiveSession)` (~25 lines), both private static.
- `EventPayload` projection is `SparkCanvasOrchestrator.ToEventPayload(ActivityEvent)` (internal static).
- Output is typed records: `SessionSnapshot`, `EventPayload`, `SnapshotAgent`, `SnapshotToolCall` — no more anonymous types. JSON envelope is in `CanvasJsonProtocol`.

The original "three diverging serializers inlined in a giant VM" problem is solved. What remains are two narrower smells:

1. **Flag-parameter**: `Project(state, isReplay)` with six in-method conditional branches; `ProjectPlaceholder` sets `IsReplay = false` and is structurally indistinguishable from a real "live" snapshot.
2. **Repeated choreography**: `Clear` → `LoadState`/`SetSession` is repeated at three call sites inside `SparkCanvasOrchestrator`, with the auto-connect fire-and-forget path having already drifted slightly from the canonical sequence.

### Proposed interface (chosen)

A small layer between `ISessionCatalog` (data) and `SparkCanvasOrchestrator` (transport). Combines **type-driven variants** for the snapshot envelope with **common-case bundling** for outbound message sequences.

```csharp
namespace TerminalHost.Core.Spark;

// Split SessionSnapshot into three sealed variants over a shared base.
public abstract record SnapshotEnvelope
{
    public string SessionId { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public DateTime StartTime { get; init; }
    public string Lifecycle { get; init; } = "Active";
    public IReadOnlyDictionary<string, SnapshotAgent> Agents { get; init; }
        = new Dictionary<string, SnapshotAgent>();
    public IReadOnlyDictionary<string, SnapshotFileActivity> FileActivities { get; init; }
        = new Dictionary<string, SnapshotFileActivity>();
}

public sealed record LiveSessionSnapshot : SnapshotEnvelope
{
    public DateTime? EndTime { get; init; }
    public IReadOnlyDictionary<string, SnapshotToolCall> ToolCalls { get; init; }  // running only
        = new Dictionary<string, SnapshotToolCall>();
    public IReadOnlyList<SnapshotMessage> Messages { get; init; } = Array.Empty<SnapshotMessage>();
}

public sealed record ReplaySessionSnapshot : SnapshotEnvelope
{
    public DateTime EndTime { get; init; }                                          // non-null
    public IReadOnlyDictionary<string, SnapshotToolCall> ToolCalls { get; init; }  // all calls
        = new Dictionary<string, SnapshotToolCall>();
}

public sealed record PlaceholderSessionSnapshot : SnapshotEnvelope;

// SnapshotToolCall stays unified — splitting it adds churn without killing a bug class.
```

```csharp
namespace TerminalHost.Core.Interfaces.Spark;

public interface ISparkPayloadComposer
{
    /// <summary>The 95% path. Returns the ordered Clear+LoadState/SetSession sequence
    /// the orchestrator should send for a session-open. Catalog lookup + placeholder
    /// fallback + waiting-card emission all happen inside.</summary>
    ValueTask<IReadOnlyList<CanvasOutbound>> ComposeOpenAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Multi-session observatory payload. Skipped sessions degrade to PlaceholderSessionSnapshot.</summary>
    IReadOnlyList<CanvasOutbound> ComposeMulti(IReadOnlyList<SessionListItem> sessions);

    /// <summary>Replay payload from a JSONL path. Null if no events parsed.</summary>
    ValueTask<IReadOnlyList<CanvasOutbound>?> ComposeReplayAsync(string jsonlPath, CancellationToken ct = default);

    /// <summary>Per-event hot path. Replaces SparkCanvasOrchestrator.ToEventPayload.</summary>
    EventPayload ProjectEvent(ActivityEvent evt);
}
```

`CanvasOutbound` payload types tighten:

```csharp
public sealed record LoadState(LiveSessionSnapshot Session) : CanvasOutbound;
public sealed record LoadReplay(ReplaySessionSnapshot Session, IReadOnlyList<EventPayload> Events) : CanvasOutbound;
public sealed record LoadMultiState(IReadOnlyList<SnapshotEnvelope> Sessions) : CanvasOutbound;
// SetSession unchanged — that IS the placeholder-or-waiting path.
```

`CanvasJsonProtocol.Serialize` adds three small per-variant projection helpers that each emit the existing wire shape (including the `isReplay` boolean for JS). Wire bit-identical; the bool exists exactly once, at the serializer.

### Usage — orchestrator after

```csharp
public async Task OpenSessionAsync(string sessionId)
{
    if (string.IsNullOrEmpty(sessionId)) return;
    var messages = await _composer.ComposeOpenAsync(sessionId, _cts.Token);
    TransitionTo(Reduce(State, new Trigger.HostOpen(sessionId)));
    foreach (var m in messages) await SendAsync(m);
}
```

`OpenSessionAsyncFireAndForget` collapses to the same body minus `TransitionTo`. `EnterMultiModeAsync` becomes `foreach over _composer.ComposeMulti(_availableSessions)`. `OpenJsonlAsync` becomes `ComposeReplayAsync` + foreach.

### Dependency category
**In-process.** Composer takes `ISessionCatalog` as a ctor dependency; no I/O on the composer itself. Enrichment retry stays on the catalog (it's a freshness concern, not a protocol-composition concern) — kept out of the composer per Ousterhout's "different layer, different abstraction."

### Test impact
- **New boundary tests**: snapshot/contract tests against `CanvasJsonProtocol.Serialize(LoadState(live))`, `(LoadReplay(replay, …))`, and `(SetSession(id, "Waiting…"))` — locks the JS-facing shape and the per-variant divergence. Plus `ComposeOpenAsync` tests for the 3-tier fallback (tracked → placeholder → waiting).
- **Old tests to delete**: none yet (current coverage is only `SparkCanvasOrchestratorTests`, which keeps validating its slice of behavior unchanged).
- The flag-parameter branches inside `Project` disappear from coverage maps — each variant's compose method only produces fields that variant has.

### Honest stakes
This is a "kill a flag, consolidate a 3-line sequence" refactor on already-decent code. **Not strategic.** If higher-value work exists, defer indefinitely. If picked up: the type splits (D) are the part most worth keeping; the `ComposeOpenAsync` bundle (C) is polish that pays back at the auto-connect site.

### Designs considered (and rejected)

- **A — Minimize the interface.** Three overloads `Compose(SessionActivityState)`, `Compose(LiveSession)`, `Compose(ActivityEvent)`; use `state.Lifecycle == Completed` as the implicit live/replay discriminator. Rejected: cheats elegantly but doesn't kill any bug class — a future "replay an Active session" feature breaks the contract. Net: rename, not redesign.
- **B — Maximize flexibility.** `ISnapshotProjector` registry keyed by string `Mode`, last-write-wins; new modes register without modifying the composer. Rejected: ~5 new types for a 3-mode problem. Author of the candidate conceded "hold off until mode #5 is on a real ticket." Park the design until thumbnail/diff/export modes are scheduled.
- **C — Optimize the common case (standalone).** `ComposeOpenAsync` returning `IReadOnlyList<CanvasOutbound>` collapses orchestrator branches and absorbs enrichment retry. Kept for the bundle method, but enrichment retry left on the catalog where it belongs.
- **D — Type-driven variants (standalone).** Split `SessionSnapshot` into Live/Replay/Placeholder records; `LoadState(LiveSessionSnapshot)` enforces correctness at compile time. Kept for the type splits, with the candidate author's own caveat — leave `SnapshotToolCall` unified rather than splitting it to `.Live` / `.Replay` variants.

---

## C. Extract `ISparkBridge` port (typed C#↔JS protocol)

### Cluster
- `SparkCanvasView.OnWebMessageReceived` (string-peeking like `message.Contains("\"action\":\"ready\"")`)
- `SparkCanvasView.OnSendMessageToCanvas`
- `SparkCanvasViewModel.PostToCanvas`
- `SparkCanvasViewModel.OnCanvasMessage` switch
- WPF and Avalonia copies of all the above
- Parallel JS action handling in `web/spark/events.js`

### Why coupled
~14 protocol action verbs spread across three layers:
- **C# → JS**: `clear`, `loadState`, `loadReplay`, `setTheme`, `setSession`, `sessionList`, `loadMultiState`, `event`
- **JS → C#**: `ready`, `selectSession`, `refreshSessions`, `requestMultiMode`, `exitMultiMode`, `themeChanged`

The View peeks at JSON strings (a protocol leak from the VM into the View), the VM owns the parser switch, and `events.js` handles the JS side independently. No versioning, no schema, no documentation file. WPF and Avalonia re-implement the same transport.

### Dependency category
**Ports & adapters (remote but owned).** The "remote" boundary is the WebView2/WebView IPC channel.

### Proposed interface (sketch — not a commitment)
```csharp
public interface ISparkBridge
{
    Task SendAsync(SparkOutboundMessage message);
    event EventHandler<SparkInboundMessage> MessageReceived;
    event EventHandler? CanvasReady;
}

public abstract record SparkOutboundMessage
{
    public sealed record Clear : SparkOutboundMessage;
    public sealed record LoadState(object Payload) : SparkOutboundMessage;
    public sealed record LoadReplay(object State, IReadOnlyList<object> Events) : SparkOutboundMessage;
    public sealed record Event(object Payload) : SparkOutboundMessage;
    public sealed record SetTheme(string Theme) : SparkOutboundMessage;
    // ...etc
}

public abstract record SparkInboundMessage
{
    public sealed record SelectSession(string SessionId) : SparkInboundMessage;
    public sealed record RefreshSessions : SparkInboundMessage;
    public sealed record RequestMultiMode : SparkInboundMessage;
    public sealed record ExitMultiMode : SparkInboundMessage;
    public sealed record ThemeChanged(string Theme) : SparkInboundMessage;
}
```

Production adapters: `WebView2SparkBridge` (WPF), `AvaloniaWebViewSparkBridge`. Test adapter: `InMemorySparkBridge` with `Send` recording + `RaiseInbound(message)` helper.

### Test impact
- **New boundary tests**: pump fake inbound messages into the VM via the in-memory bridge, assert outbound message sequence. Covers all 14 protocol verbs.
- **Old tests to delete**: none (no existing coverage).
- Eliminates View-side string-peeking entirely. Protocol can grow a `version` field. JS side can be regenerated from a single C# source of truth.

---

## D. Extract `SparkSessionDirector` (mode state machine)

### Cluster
Inside `SparkCanvasViewModel`:
- Fields: `_isMultiMode`, `CurrentSessionId`
- Methods: `OpenSession`, `LoadMultiMode`, `AutoConnectToActiveSession`, `LoadJsonlFileAsync`, `OnCanvasReady`
- Auto-connect heuristic + mode-based filter inside `OnActivityEvent` (lines 530–560)

### Why coupled
A real state machine — `NoSession → Single(sessionId) → Multi | JsonlReplay(filePath)` — is encoded as two implicit booleans (`_isMultiMode`, `CurrentSessionId == null`) with transitions scattered across six methods. Every event handler re-derives "should I forward this event?" from those booleans:

```csharp
if (!_isMultiMode && CurrentSessionId == null && evt.Type == ActivityEventType.SessionStart)
    OpenSession(evt.SessionId);  // auto-connect side effect inside the filter
if (!_isMultiMode && (CurrentSessionId == null || evt.SessionId != CurrentSessionId)) return;
```

This is the highest-friction part of the VM and the most likely place for "events going to the wrong session" / "auto-connect fires when it shouldn't" bugs.

### Dependency category
**In-process.** Pure state machine over injected `ISessionActivityService` + `ITimelineService`.

### Proposed interface (sketch — not a commitment)
```csharp
public interface ISparkSessionDirector
{
    SparkMode CurrentMode { get; }
    event EventHandler<SparkOutboundMessage>? Emit;

    void OnCanvasReady();
    void SelectSession(string sessionId);
    void EnterMultiMode();
    void ExitMultiMode();
    Task LoadJsonlAsync(string path);
    void OnActivityEvent(ActivityEvent evt);
}

public abstract record SparkMode
{
    public sealed record None : SparkMode;
    public sealed record Single(string SessionId) : SparkMode;
    public sealed record Multi : SparkMode;
    public sealed record Replay(string FilePath) : SparkMode;
}
```

The director decides, for each inbound activity event, whether to forward it (and as what shape) based on `CurrentMode`. The VM becomes a UI binding shell that wires the director to the bridge.

### Test impact
- **New boundary tests**: every mode transition (`None→Single` via auto-connect, `Single→Multi`, `Multi→Single` on selection, `*→Replay`, etc.); per-mode event-routing rules ("in Multi mode, an event from an unknown session fabricates a placeholder agent"; "in Single mode, events for other sessions are dropped"); auto-connect-on-`SessionStart` heuristic.
- **Old tests to delete**: none (no existing coverage).
- The hardest-to-reason-about part of Spark becomes the most testable.

---

## Suggested ordering if multiple are picked

1. **B** first — it's the smallest, removes ~200 lines from the VM, and de-risks the other two.
2. **D** next — extracts the actual logic complexity. With B already done, the director can emit pre-shaped DTOs instead of raw domain objects.
3. **C** after — by now the VM is a thin shell, so the bridge port replaces only a small amount of code per platform.
4. **A** last — once B/C/D are in Core, deleting the Avalonia VM duplicate is a one-commit cleanup.

Doing **D + C** together (without B) would also work and gives Spark a properly deep core: typed protocol on the outside, state machine on the inside, VM reduced to a UI binding adapter.
