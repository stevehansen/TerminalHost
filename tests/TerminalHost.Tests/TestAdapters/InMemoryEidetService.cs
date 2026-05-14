using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// In-memory adapter for <see cref="IEidetService"/>. Deterministic — no HTTP,
/// no real timers. Used by boundary tests to exercise the contract.
/// </summary>
public sealed class InMemoryEidetService : IEidetService
{
    private readonly Dictionary<string, EidetMemoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<EidetLayerInfo>> _layersByRepo = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private MemoryConnectionStatus _connectionStatus = MemoryConnectionStatus.Disabled;
    private string? _errorMessage;
    private string _version = "test-0.0";
    private DateTime? _connectedSince;

    /// <summary>If set, the next TryConnectAsync will return Error with this message.</summary>
    public string? SimulatedHealthFailure { get; set; }

    /// <summary>Counts of intake calls by project path — useful for verifying side effects.</summary>
    public Dictionary<string, int> IntakeCallCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many entries each intake call should report as new.</summary>
    public int IntakeNewCount { get; set; } = 0;

    /// <summary>Records of TestConnectionAsync calls.</summary>
    public List<string> TestConnectionUrls { get; } = new();

    public bool Enabled { get; set; } = true;

    public MemoryStatus Status
    {
        get
        {
            lock (_lock)
            {
                return new MemoryStatus
                {
                    ConnectionStatus = _connectionStatus,
                    ErrorMessage = _errorMessage,
                    ServerUrl = "in-memory://eidet",
                    Version = _connectionStatus == MemoryConnectionStatus.Connected ? _version : null,
                    DocumentCount = _connectionStatus == MemoryConnectionStatus.Connected ? _entries.Count : 0,
                    ConnectedSince = _connectedSince,
                };
            }
        }
    }

    public event EventHandler<MemoryStatus>? StatusChanged;

    public bool IsConnected
    {
        get { lock (_lock) return _connectionStatus == MemoryConnectionStatus.Connected; }
    }

    public Task<bool> TryConnectAsync(IReadOnlyList<string>? openProjectPaths = null, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            Disconnect();
            return Task.FromResult(false);
        }

        lock (_lock)
        {
            _connectionStatus = MemoryConnectionStatus.Connecting;
            _errorMessage = null;
        }
        RaiseStatusChanged();

        if (SimulatedHealthFailure != null)
        {
            lock (_lock)
            {
                _connectionStatus = MemoryConnectionStatus.Error;
                _errorMessage = SimulatedHealthFailure;
            }
            RaiseStatusChanged();
            return Task.FromResult(false);
        }

        lock (_lock)
        {
            _connectionStatus = MemoryConnectionStatus.Connected;
            _connectedSince = DateTime.UtcNow;
            _errorMessage = null;
        }
        RaiseStatusChanged();

        if (openProjectPaths is { Count: > 0 })
        {
            foreach (var path in openProjectPaths)
                RecordIntake(path);
        }

        return Task.FromResult(true);
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _connectionStatus = MemoryConnectionStatus.Disabled;
            _errorMessage = null;
            _connectedSince = null;
        }
        RaiseStatusChanged();
    }

    public async Task OnSettingsChangedAsync(CancellationToken ct = default)
    {
        if (!Enabled)
        {
            if (IsConnected) Disconnect();
            return;
        }
        if (!IsConnected) await TryConnectAsync(null, ct);
    }

    public Task OnProjectOpenedAsync(string projectPath, CancellationToken ct = default)
    {
        if (!IsConnected) return Task.CompletedTask;
        RecordIntake(projectPath);
        return Task.CompletedTask;
    }

    public Task<string> RunIntakeAsync(string projectPath, CancellationToken ct = default)
    {
        if (!IsConnected) return Task.FromResult("Eidet is not connected.");
        RecordIntake(projectPath);
        return Task.FromResult($"Intake complete: {IntakeNewCount} new, 0 skipped");
    }

    /// <summary>Test helper: seed an entry directly without going through any HTTP layer.</summary>
    public void Seed(EidetMemoryEntry entry)
    {
        lock (_lock) _entries[entry.Id] = entry;
    }

    /// <summary>Test helper: seed layer info for a repo.</summary>
    public void SeedLayers(string repoId, IEnumerable<EidetLayerInfo> layers)
    {
        lock (_lock) _layersByRepo[repoId] = layers.ToList();
    }

    public Task<EidetStatsResponse?> GetStatsAsync(string repoId, CancellationToken ct = default)
    {
        if (!IsConnected) return Task.FromResult<EidetStatsResponse?>(null);
        var counts = new Dictionary<string, int>();
        List<EidetMemoryEntry> repoEntries;
        lock (_lock) repoEntries = _entries.Values.Where(e => string.Equals(e.RepoId, repoId, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var e in repoEntries)
        {
            var key = e.Type.ToLowerInvariant();
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return Task.FromResult<EidetStatsResponse?>(new EidetStatsResponse
        {
            Repo = repoId,
            Summary = $"{repoEntries.Count} entries",
            Counts = counts,
            Total = repoEntries.Count,
        });
    }

    public Task<EidetSearchResponse?> SearchAsync(string repoId, string query, string? type = null, int limit = 100, CancellationToken ct = default)
    {
        List<EidetMemoryEntry> matches;
        lock (_lock)
        {
            matches = _entries.Values
                .Where(e => string.Equals(e.RepoId, repoId, StringComparison.OrdinalIgnoreCase))
                .Where(e => type is null || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
                .Where(e => e.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || (e.Summary ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();
        }
        var results = matches.Select(e => new EidetSearchResult
        {
            Id = e.Id,
            RepoId = e.RepoId,
            Type = e.Type,
            Content = e.Content,
            Summary = e.Summary,
            OneLiner = e.OneLiner,
            Tags = e.Tags,
            Entities = e.Entities,
            Importance = e.Importance,
            CreatedAt = e.CreatedAt,
            Score = 1.0f,
            LayerSource = e.LayerId,
        }).ToList();
        return Task.FromResult<EidetSearchResponse?>(new EidetSearchResponse { Results = results });
    }

    public Task<EidetMemoriesResponse?> BrowseAsync(string repoId, string? type = null, CancellationToken ct = default)
    {
        List<EidetMemoryEntry> matches;
        lock (_lock)
        {
            matches = _entries.Values
                .Where(e => string.Equals(e.RepoId, repoId, StringComparison.OrdinalIgnoreCase))
                .Where(e => type is null || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return Task.FromResult<EidetMemoriesResponse?>(new EidetMemoriesResponse
        {
            Repo = repoId,
            Count = matches.Count,
            Entries = matches,
        });
    }

    public Task<EidetLayersResponse?> GetLayersAsync(string repoId, CancellationToken ct = default)
    {
        List<EidetLayerInfo> layers;
        lock (_lock) layers = _layersByRepo.TryGetValue(repoId, out var l) ? l.ToList() : new();
        return Task.FromResult<EidetLayersResponse?>(new EidetLayersResponse { Layers = layers });
    }

    public Task<bool> ForgetAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_entries.Remove(id));
    }

    public Task<(int StatusCode, string Body, string ContentType)> ProxyGetAsync(string pathAndQuery, CancellationToken ct = default)
    {
        if (!IsConnected)
            return Task.FromResult((503, JsonSerializer.Serialize(new { error = "not connected" }), "application/json"));
        return Task.FromResult((200, JsonSerializer.Serialize(new { proxied = pathAndQuery }), "application/json"));
    }

    public Task<EidetStatusResponse?> TestConnectionAsync(string url, CancellationToken ct = default)
    {
        TestConnectionUrls.Add(url);
        if (SimulatedHealthFailure != null)
            return Task.FromResult<EidetStatusResponse?>(null);
        return Task.FromResult<EidetStatusResponse?>(new EidetStatusResponse
        {
            Version = _version,
            Status = "running",
            Database = new EidetDatabaseInfo { Name = "test", DocumentCount = _entries.Count, IndexExists = true },
        });
    }

    private void RecordIntake(string path)
    {
        lock (_lock) IntakeCallCounts[path] = IntakeCallCounts.GetValueOrDefault(path) + 1;
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, Status);
}
