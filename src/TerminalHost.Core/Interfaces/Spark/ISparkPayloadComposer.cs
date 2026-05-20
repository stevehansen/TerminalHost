using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Interfaces.Spark;

/// <summary>
/// Composes ordered outbound message sequences for the Spark canvas. Sits
/// between <see cref="ISessionCatalog"/> (data) and <c>SparkCanvasOrchestrator</c>
/// (transport + FSM), absorbing the repeated <c>Clear → LoadState/SetSession</c>
/// choreography and the enrichment-retry dance.
/// </summary>
public interface ISparkPayloadComposer
{
    /// <summary>
    /// Returns the ordered <c>Clear</c> + <c>LoadState</c>/<c>SetSession</c>
    /// sequence the orchestrator should send for a session-open. Catalog lookup,
    /// enrichment retry, three-tier fallback (live → placeholder → waiting card)
    /// all happen inside.
    /// </summary>
    ValueTask<IReadOnlyList<CanvasOutbound>> ComposeOpenAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Multi-session observatory payload. Returns the resolved session ids
    /// alongside the messages so the FSM trigger doesn't have to mine them
    /// back out of the message list. Sessions missing from the catalog are skipped.
    /// </summary>
    MultiComposition ComposeMulti(IReadOnlyList<SessionListItem> sessions);

    /// <summary>
    /// Replay payload for a JSONL path. Returns null when no events parse out
    /// of the file; otherwise the resolved session id is returned alongside the
    /// <c>Clear</c> + <c>LoadReplay</c> message pair.
    /// </summary>
    ValueTask<ReplayComposition?> ComposeReplayAsync(string jsonlPath, CancellationToken ct = default);

    /// <summary>Per-event hot path. Projects an <see cref="ActivityEvent"/> onto the JS-facing payload.</summary>
    EventPayload ProjectEvent(ActivityEvent evt);
}

/// <summary>Result of <see cref="ISparkPayloadComposer.ComposeMulti"/> — the
/// resolved session-id set plus the ordered outbound messages.</summary>
public sealed record MultiComposition(IReadOnlySet<string> SessionIds, IReadOnlyList<CanvasOutbound> Messages);

/// <summary>Result of <see cref="ISparkPayloadComposer.ComposeReplayAsync"/> —
/// the session id parsed from the transcript plus the ordered outbound messages.</summary>
public sealed record ReplayComposition(string SessionId, IReadOnlyList<CanvasOutbound> Messages);
