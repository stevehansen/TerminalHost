using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// Fluent in-memory <see cref="ISessionCatalog"/> for orchestrator tests.
/// Build with <c>.With(id, snapshot)</c> chains.
/// </summary>
public sealed class FakeSessionCatalog : ISessionCatalog
{
    private readonly Dictionary<string, SnapshotEnvelope> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ReplayLoadResult> _replays = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times <see cref="EnrichAsync"/> was called per session id.</summary>
    public Dictionary<string, int> EnrichCallCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeSessionCatalog With(string sessionId, SnapshotEnvelope snapshot)
    {
        _snapshots[sessionId] = snapshot;
        return this;
    }

    public FakeSessionCatalog WithReplay(string path, ReplayLoadResult result)
    {
        _replays[path] = result;
        return this;
    }

    public IReadOnlyList<SessionListItem> List()
    {
        var items = new List<SessionListItem>();
        foreach (var kv in _snapshots)
        {
            items.Add(new SessionListItem
            {
                SessionId = kv.Key,
                DisplayName = kv.Key,
                ProjectPath = kv.Value.WorkingDirectory ?? "",
                IsLive = kv.Value.Lifecycle == "Active",
                StartTime = kv.Value.StartTime
            });
        }
        return items;
    }

    public SnapshotEnvelope? GetSnapshot(string sessionId) =>
        _snapshots.TryGetValue(sessionId, out var s) ? s : null;

    public Task<ReplayLoadResult?> LoadReplayAsync(string jsonlPath, CancellationToken ct)
    {
        _replays.TryGetValue(jsonlPath, out var r);
        return Task.FromResult<ReplayLoadResult?>(r);
    }

    public Task EnrichAsync(string sessionId, CancellationToken ct)
    {
        EnrichCallCounts.TryGetValue(sessionId, out var count);
        EnrichCallCounts[sessionId] = count + 1;
        return Task.CompletedTask;
    }
}
