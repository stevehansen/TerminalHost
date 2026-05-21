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

> ✅ **Effectively complete (delivered as `ICanvasTransport`, 2026-05-20).** The original Section C framing — "View peeks at JSON strings, VM owns the parser switch, WPF and Avalonia reimplement the transport, no typed protocol" — is **stale**. The ports-and-adapters refactor (commit `4bd8337`, PRs #66/#67) shipped the bridge under a different name. The interface Section C sketches is essentially identical to the one in production:

| Section C proposed | Production today |
|---|---|
| `ISparkBridge` port | `ICanvasTransport` (`src/TerminalHost.Core/Interfaces/Spark/ICanvasTransport.cs`) |
| `SparkOutboundMessage` union (8 verbs) | `CanvasOutbound` — 8 sealed records |
| `SparkInboundMessage` union (6 verbs) | `CanvasInbound` — 6 sealed records |
| `WebView2SparkBridge` (WPF) | `WebView2CanvasTransport` (147 LOC) |
| `AvaloniaWebViewSparkBridge` | `AvaloniaWebViewCanvasTransport` (143 LOC) |
| `InMemorySparkBridge` (test) | `InMemoryCanvasTransport` (67 LOC) — `Sent` list + `Inject`/`MarkReady`/`ClearSent` |
| Kill View-side string-peeking | Done — `SparkCanvasView.xaml.cs:13` literally states *"the view has no knowledge of action verb strings or JSON envelope shape"* |
| Single source for protocol | `CanvasJsonProtocol.Serialize` / `TryParse` (153 LOC, all 14 verbs in one switch) |

### Residual smell (low-payoff polish)

What's actually left is much narrower than the original Section C: `WebView2CanvasTransport.cs` and `AvaloniaWebViewCanvasTransport.cs` are ~80% literal duplicates. Lines 22–146 of each share:

- `Queue<CanvasOutbound> _preReadyQueue` + `_gate` lock
- `SendAsync` body (disposed-check → enqueue if not ready → `PostSerialized`)
- `OnWebMessageReceived` body (TryParse → ready-handshake → raise Received)
- `FlushPreReadyQueue` body
- `PostSerialized` body (serialize → Post → JS-post → swallow)
- `Dispose` body

Genuine platform differences amount to three small surfaces:
- UI-thread dispatch (`Dispatcher` vs `Dispatcher.UIThread`)
- Inbound message string extraction (`e.TryGetWebMessageAsString()` vs `e.Message`)
- Outbound post signature (`PostWebMessageAsString(json)` vs `PostWebMessageAsString(json, null)`)

### If picked up

Mechanical extraction — no design exploration needed:

```csharp
// In TerminalHost.Core.Services.Spark (sibling of NullCanvasTransport)
public abstract class WebViewCanvasTransportBase : ICanvasTransport, IDisposable
{
    private readonly Queue<CanvasOutbound> _preReadyQueue = new();
    private readonly object _gate = new();
    protected bool Disposed;

    public bool IsReady { get; private set; }
    public event EventHandler<CanvasInbound>? Received;
    public event EventHandler? Ready;

    public Task SendAsync(CanvasOutbound message) { /* shared body */ }
    public abstract void Post(Action action);

    // Platform hooks (sealed → 3 lines each in subclass)
    protected abstract void PostOutboundJson(string json);
    protected void OnInboundJson(string json) { /* shared TryParse + ready handshake + Received */ }

    public abstract void Dispose();
}
```

Each platform adapter shrinks to ~30 lines:
- `Post` body (`Dispatcher.CheckAccess`/`UIThread.CheckAccess` branch)
- `PostOutboundJson` body (one-arg or two-arg `PostWebMessageAsString` call)
- Constructor subscribing the WebView's `WebMessageReceived` event and calling `OnInboundJson(e.…)`
- `Dispose` unsubscribing

### Test impact
- **No new behavior tests** — `InMemoryCanvasTransport` already exercises the orchestrator through the same interface; the extraction is structural.
- **Optional**: one shared `WebViewCanvasTransportBaseTests` over a minimal fake subclass covering the pre-ready queue / ready handshake / dispose ordering. Currently each platform adapter has zero direct test coverage; this is the cheap way to fix that.

### Honest stakes
~120 LOC of dedup behind a stable, working interface. **Park indefinitely** unless someone fixes a bug in one transport and forgets to mirror it to the other. No design phase needed — when it's done, it's mechanical.

---

## D. Promote routing + ready policy to `CanvasPolicy` (no director)

> ✅ **Design phase complete (2026-05-20).** The original section D framing (extract `ISparkSessionDirector` from a 627-line VM with implicit `_isMultiMode` + `CurrentSessionId` booleans) is **obsolete**: as of `4bd8337` the VM is 100 lines, `SparkCanvasOrchestrator` owns a pure `Reduce` + `Trigger`/`CanvasState` discriminated unions, and 24 boundary tests already cover mode transitions, event routing, and auto-connect. The actual remaining smell is two predicates that live as ad-hoc switches *outside* `Reduce`: the activity-event routing rule (`Single ⇒ id-match · Multi ⇒ true · Replay ⇒ false`) and the ready-policy decision (auto-connect to first live session, fallback to "Waiting…"). Promote both to static functions next to `Reduce`. No director, no interface, no DI changes.

### What's actually entangled

Two methods inside `SparkCanvasOrchestrator` (`HandleActivityEvent` lines 298–332, `OnTransportReady` lines 209–248) interleave three concerns: pure decisions, composer calls, and transport sends. The composer-and-transport parts are fine — they belong to a choreographer. The pure-decision parts are the smell:

```csharp
// Inside HandleActivityEvent — the "shouldForward" switch is policy, not in Reduce:
var shouldForward = State switch {
    CanvasState.Single s => string.Equals(s.SessionId, evt.SessionId, StringComparison.Ordinal),
    CanvasState.Multi    => true,
    CanvasState.Replay   => false,
    _ => false
};
```

```csharp
// Inside OnTransportReady — the auto-connect-on-ready policy is also ad-hoc:
if (State is CanvasState.Single s)        await OpenSessionAsync(s.SessionId);
else if (State is CanvasState.Empty) {
    var first = _availableSessions.FirstOrDefault(x => x.IsLive)
              ?? _availableSessions.FirstOrDefault();
    if (first != null) await OpenSessionAsync(first.SessionId);
    else               await SendAsync(new CanvasOutbound.SetSession(null, "Waiting for session..."));
}
```

Both predicates are pure functions of `(state, …)`. Today they're untestable without a transport + composer + activity-service rig; promoting them to statics makes them direct-test material.

### Chosen design — `CanvasPolicy` static helper

```csharp
namespace TerminalHost.Core.Spark;

public static class CanvasPolicy
{
    /// <summary>Should this activity event be forwarded to the canvas in the given state?</summary>
    public static bool ShouldForward(CanvasState state, ActivityEvent evt) => state switch
    {
        CanvasState.Single s => string.Equals(s.SessionId, evt.SessionId, StringComparison.Ordinal),
        CanvasState.Multi    => true,
        _                    => false,
    };

    /// <summary>
    /// On transport-ready, returns the session id to auto-open, or null if the
    /// orchestrator should fall back to <c>SetSession(null, "Waiting for session...")</c>.
    /// </summary>
    public static string? AutoOpenOnReady(CanvasState state, IReadOnlyList<SessionListItem> sessions) => state switch
    {
        CanvasState.Single s => s.SessionId,
        CanvasState.Empty    => (sessions.FirstOrDefault(x => x.IsLive) ?? sessions.FirstOrDefault())?.SessionId,
        _                    => null,
    };
}
```

The double-check on auto-connect (`State is Empty && evt.Type == SessionStart`, which is checked once in the orchestrator and again in `Reduce`'s `when current is CanvasState.Empty` clause) collapses by letting `Reduce` itself be the gate:

```csharp
private void HandleActivityEvent(ActivityEvent evt)
{
    if (_disposed) return;

    if (evt.Type == ActivityEventType.SessionStart) {
        var next = Reduce(State, new Trigger.ActivityStart(evt.SessionId));
        if (!Equals(next, State)) {
            TransitionTo(next);
            _ = OpenSessionAsyncFireAndForget(evt.SessionId);
            return;
        }
    }

    if (!CanvasPolicy.ShouldForward(State, evt)) return;
    _ = SendAsyncFireAndForget(new CanvasOutbound.Event(_composer.ProjectEvent(evt)));
}

private async void OnTransportReady(object? sender, EventArgs e)
{
    if (_readyHandled) return;
    _readyHandled = true;
    try {
        await SendAsync(new CanvasOutbound.SetTheme(_theme.Load()));
        await RefreshSessionsAsync();
        var openId = CanvasPolicy.AutoOpenOnReady(State, _availableSessions);
        if (openId != null) await OpenSessionAsync(openId);
        else                await SendAsync(new CanvasOutbound.SetSession(null, "Waiting for session..."));
    }
    catch (Exception ex) { _log?.Error(LogSource, $"OnTransportReady failed: {ex.GetType().Name}: {ex.Message}"); }
}
```

### Dependency category
**In-process.** `CanvasPolicy` is a static class with no dependencies. No DI changes, no new constructor parameters.

### Test impact
- **New tests** (~6, all pure, no fakes): `ShouldForward` over each `CanvasState` × matching/non-matching event id; `AutoOpenOnReady` over Empty (live wins, then most-recent, then null) / Single (returns its id) / Multi / Replay (null).
- **Existing tests**: all 24 keep passing verbatim. `Reduce` stays `public static`; the `Reduce_*` tests at the bottom of `SparkCanvasOrchestratorTests` are unchanged.
- **Coverage gap closed**: today the routing predicate and auto-connect-on-ready logic have only end-to-end coverage through the orchestrator+transport+composer fixture. After: direct unit coverage.

### Honest stakes
This is a ~30-LOC refactor — pulling two predicates out of imperative methods into named static functions, plus a one-call-site collapse of the double-checked auto-connect condition. Not strategic. If higher-value work exists, defer indefinitely. If picked up, ship in one PR.

### Designs considered (and rejected)

- **A — `ISparkSessionDirector` returning `Decision(NextState, IReadOnlyList<Effect>)` with 7 effect variants.** Pure effect-log architecture; orchestrator collapses to a ~80-line shell with `Apply(Decision)` + `Run(Effect)` switch. Rejected: ~18 new type declarations (1 interface, Decision, Context, ~8 Input variants, 7 Effect variants) for an FSM with 4 modes and 7 outbound verbs. The orchestrator's remaining bulk is transport-Attach/Dispose ordering, thread-hop via `Post`, fire-and-forget plumbing, and `_cts` lifetime — none of which moves under a director. The "thin shell" benefit is partly illusory. Effect log earns its weight at ~10+ effect kinds; here it's an indirection tax. Reconsider if Spark grows stateful routing (windowed dedup, per-agent filters) or a `Decision` audit trail is needed.
- **B — `ICanvasModeHandler` registry, one handler per `CanvasState` subtype.** Each handler implements `ShouldForward(state, evt)` and `ComposeReadyEntryAsync(state, ctx)`. Adding mode #5 = 1 new file + 1 DI line + 2 small edits. Rejected: ~6 new types (interface + 4 handlers + `IReadyContext`) to distribute 13 lines of `switch` across 4 files. The agent who designed this option self-rejected it: *"With 4 modes today and no concrete ticket for #5, the YAGNI verdict is: ship the handler interface only if a 5th mode is actually queued."* No 5th mode is queued. Re-evaluate if a Thumbnail / Diff / Export mode lands on a real ticket.
- **C — Keep everything in `Reduce` by widening its return.** Considered briefly during agent reconciliation. Rejected: the routing predicate runs on every activity event and would force `Reduce` to take an `ActivityEvent` (or a synthetic trigger per event), polluting the FSM transition table with non-state-changing inputs. `CanvasPolicy` keeps the FSM closed over state transitions and adds a sibling for the running rules.

### Migration path if scale changes
If a future ticket adds a 5th mode AND that mode's routing/ready logic is non-trivial, promote `CanvasPolicy` to an injected `ISparkPolicy` interface. The refactor is mechanical because every call site already goes through `CanvasPolicy.X(State, …)` — find-and-replace + a default implementation.

---

## Suggested ordering if multiple are picked

Status as of 2026-05-21: **A done**, **B done**, **C effectively done** (interface shipped as `ICanvasTransport`; residual is ~120 LOC of WPF/Avalonia transport dedup, no design phase needed — see section C), **D done** (commit `cae10ce`, `CanvasPolicy` promotion + reattach-fallback fix + 12 new tests).

All four candidates from the original `/improve-codebase-architecture` pass on 2026-05-18 are now either shipped or downgraded to "mechanical polish, defer indefinitely." No design exploration is open against the Spark feature.

If `WebViewCanvasTransportBase` extraction (the residual under C) gets picked up later, it's a single-PR mechanical refactor — go straight to `/implement-issue` with the sketch in section C as the spec.
