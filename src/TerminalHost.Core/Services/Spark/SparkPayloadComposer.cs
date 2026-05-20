using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Services.Spark;

/// <summary>
/// Production <see cref="ISparkPayloadComposer"/>. Reads from <see cref="ISessionCatalog"/>,
/// applies the one-shot enrichment retry, and emits the message sequences the
/// orchestrator forwards to the transport. Per-orchestrator-lifetime by design —
/// the enrichment dedup set lives here, not app-wide. Thread-safe enrichment
/// dedup via <see cref="ConcurrentDictionary{TKey, TValue}"/> so an accidental
/// lifetime widening (Singleton, or two callers from different logical threads)
/// can't race the HashSet.
/// </summary>
public sealed class SparkPayloadComposer : ISparkPayloadComposer
{
    private const string LogSource = "SparkPayloadComposer";

    private readonly ISessionCatalog _catalog;
    private readonly IDebugLogService? _log;
    private readonly ConcurrentDictionary<string, byte> _enrichedSessions = new(StringComparer.OrdinalIgnoreCase);

    public SparkPayloadComposer(ISessionCatalog catalog, IDebugLogService? log = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _log = log;
    }

    public async ValueTask<IReadOnlyList<CanvasOutbound>> ComposeOpenAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId))
            return Array.Empty<CanvasOutbound>();

        var snapshot = _catalog.GetSnapshot(sessionId);
        if (NeedsEnrichment(snapshot))
        {
            await EnrichOnceAsync(sessionId, ct);
            snapshot = _catalog.GetSnapshot(sessionId) ?? snapshot;
        }

        // Three-tier fallback: live → placeholder (degraded skeleton) → waiting card.
        // Placeholder data is surfaced via LoadState so JS can render the synthetic
        // main agent immediately; only a truly-null catalog result falls through to SetSession.
        if (snapshot is LiveSessionSnapshot or PlaceholderSessionSnapshot)
        {
            return new CanvasOutbound[]
            {
                new CanvasOutbound.Clear(),
                new CanvasOutbound.LoadState(snapshot)
            };
        }

        return new CanvasOutbound[]
        {
            new CanvasOutbound.Clear(),
            new CanvasOutbound.SetSession(sessionId, "Waiting for session data...")
        };
    }

    public MultiComposition ComposeMulti(IReadOnlyList<SessionListItem> sessions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = new List<SnapshotEnvelope>();

        foreach (var item in sessions)
        {
            var snap = _catalog.GetSnapshot(item.SessionId);
            if (snap != null && seen.Add(snap.SessionId))
                snapshots.Add(snap);
        }

        var messages = new CanvasOutbound[]
        {
            new CanvasOutbound.Clear(),
            new CanvasOutbound.LoadMultiState(snapshots)
        };
        return new MultiComposition(seen, messages);
    }

    public async ValueTask<ReplayComposition?> ComposeReplayAsync(string jsonlPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(jsonlPath)) return null;

        ReplayLoadResult? result;
        try
        {
            result = await _catalog.LoadReplayAsync(jsonlPath, ct);
        }
        catch (ObjectDisposedException) { return null; }
        catch (OperationCanceledException) { return null; }

        if (result == null) return null;

        var messages = new CanvasOutbound[]
        {
            new CanvasOutbound.Clear(),
            new CanvasOutbound.LoadReplay(result.Snapshot, result.Events)
        };
        return new ReplayComposition(result.Snapshot.SessionId, messages);
    }

    public EventPayload ProjectEvent(ActivityEvent evt)
    {
        return new EventPayload
        {
            Type = evt.Type.ToString(),
            SessionId = evt.SessionId,
            AgentId = evt.AgentId,
            Timestamp = evt.Timestamp,
            // Defensive deep clone — the source may mutate evt.Data after raising the event,
            // and we want the EventPayload snapshot to be stable for downstream serialization.
            Data = DeepCloneDictionary(evt.Data)
        };
    }

    // -------- Helpers --------

    private static bool NeedsEnrichment(SnapshotEnvelope? snapshot)
    {
        if (snapshot is not LiveSessionSnapshot live) return false;
        return live.Agents.Values.Any(a => a.IsMain && a.Model == null);
    }

    private async Task EnrichOnceAsync(string sessionId, CancellationToken ct)
    {
        if (!_enrichedSessions.TryAdd(sessionId, 0)) return;
        try
        {
            await _catalog.EnrichAsync(sessionId, ct);
        }
        catch (ObjectDisposedException)
        {
            // CTS was disposed mid-flight; safe to ignore.
        }
        catch (OperationCanceledException)
        {
            // Cancellation during Dispose — expected.
        }
        catch (Exception ex)
        {
            // best-effort, but make the failure observable.
            _log?.Warn(LogSource, $"EnrichAsync ('{sessionId}') failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Dictionary<string, object?> DeepCloneDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(source.Count);
        foreach (var kv in source)
            clone[kv.Key] = DeepCloneValue(kv.Value);
        return clone;
    }

    private static object? DeepCloneValue(object? value) => value switch
    {
        null => null,
        string or bool or int or long or double or decimal or float or short or byte
            or DateTime or DateTimeOffset or Guid or TimeSpan or Uri
            => value,
        IReadOnlyDictionary<string, object?> dict => DeepCloneDictionary(dict),
        IEnumerable<object?> list => list.Select(DeepCloneValue).ToList(),
        _ => value
    };
}
