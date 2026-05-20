using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Interfaces.Spark;

/// <summary>
/// Unified session-discovery + replay-loading surface for the canvas.
/// Merges what <see cref="ITimelineService"/>, <see cref="ISessionActivityService"/>,
/// and <c>TranscriptParserService</c> expose to the canvas into one query API.
/// </summary>
/// <remarks>
/// The "three diverging serializers" problem dies here: the orchestrator asks the
/// catalog for a <see cref="SnapshotEnvelope"/> and serializes the unified shape once.
/// </remarks>
public interface ISessionCatalog
{
    /// <summary>Merged list of live and activity-tracked sessions, deduped.</summary>
    IReadOnlyList<SessionListItem> List();

    /// <summary>Returns the snapshot for a session, or null if unknown.</summary>
    SnapshotEnvelope? GetSnapshot(string sessionId);

    /// <summary>Loads and parses a JSONL transcript into a snapshot + event list.</summary>
    Task<ReplayLoadResult?> LoadReplayAsync(string jsonlPath, CancellationToken ct);

    /// <summary>
    /// Pulls missing model info from a session's transcript. Best-effort; safe to await.
    /// </summary>
    Task EnrichAsync(string sessionId, CancellationToken ct);
}

/// <summary>
/// Result of loading a JSONL replay. Contains both the synthesized snapshot
/// and the raw event list (needed for the canvas's transcript/feed view).
/// </summary>
public sealed record ReplayLoadResult(ReplaySessionSnapshot Snapshot, IReadOnlyList<EventPayload> Events);
