using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Connection status for the Eidet memory service.
/// </summary>
public enum MemoryConnectionStatus
{
    Disabled,
    Connecting,
    Connected,
    Error,
}

/// <summary>
/// Live status snapshot of the memory system.
/// </summary>
public class MemoryStatus
{
    public MemoryConnectionStatus ConnectionStatus { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ServerUrl { get; init; }
    public string? Version { get; init; }
    public int DocumentCount { get; init; }
    public DateTime? ConnectedSince { get; init; }
}

/// <summary>
/// Manages the lifecycle of the Eidet connection.
/// Replaces the old MemoryHostService — no RavenDB, no service graph, no timers.
/// ~120 lines of HTTP plumbing vs ~580 lines of embedded memory orchestration.
/// </summary>
public class EidetClientService
{
    private readonly IConfigurationService _configService;
    private readonly IToastService? _toastService;
    private readonly IDebugLogService? _debugLog;

    private EidetClient? _client;
    private MemoryConnectionStatus _status = MemoryConnectionStatus.Disabled;
    private string? _errorMessage;
    private string? _version;
    private int _documentCount;
    private DateTime? _connectedSince;
    private readonly HashSet<string> _intakedRepos = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>The underlying HTTP client. Null when disconnected.</summary>
    public EidetClient? Client => _client;

    /// <summary>Whether the service is currently connected to Eidet.</summary>
    public bool IsConnected
    {
        get { lock (_lock) return _status == MemoryConnectionStatus.Connected; }
    }

    public EidetClientService(
        IConfigurationService configService,
        IToastService? toastService = null,
        IDebugLogService? debugLog = null)
    {
        _configService = configService;
        _toastService = toastService;
        _debugLog = debugLog;
    }

    /// <summary>Current status snapshot.</summary>
    public MemoryStatus GetStatus()
    {
        var config = _configService.Load();
        lock (_lock)
        {
            return new MemoryStatus
            {
                ConnectionStatus = _status,
                ErrorMessage = _errorMessage,
                ServerUrl = config.Settings.Memory.EidetUrl,
                Version = _version,
                DocumentCount = _documentCount,
                ConnectedSince = _connectedSince,
            };
        }
    }

    /// <summary>
    /// Try to connect to Eidet via health check.
    /// Pass currently-open project paths to trigger auto-intake for restored tabs.
    /// </summary>
    public async Task<bool> TryConnectAsync(IReadOnlyList<string>? openProjectPaths = null, CancellationToken ct = default)
    {
        var config = _configService.Load();
        if (!config.Settings.Memory.Enabled)
        {
            Disconnect();
            return false;
        }

        lock (_lock)
        {
            if (_status == MemoryConnectionStatus.Connected)
                return true;
            _status = MemoryConnectionStatus.Connecting;
            _errorMessage = null;
        }

        try
        {
            var url = config.Settings.Memory.EidetUrl;
            var client = new EidetClient(url);
            var health = await client.CheckHealthAsync(ct);

            if (health is null || !health.IsHealthy)
            {
                client.Dispose();
                lock (_lock)
                {
                    _status = MemoryConnectionStatus.Error;
                    _errorMessage = "Eidet health check failed — is the service running?";
                }
                _toastService?.Show("Eidet: service unreachable", ToastType.Error);
                return false;
            }

            // Dispose previous client if any
            _client?.Dispose();

            lock (_lock)
            {
                _client = client;
                _status = MemoryConnectionStatus.Connected;
                _version = health.Version;
                _connectedSince = DateTime.UtcNow;
                _errorMessage = null;
            }

            _debugLog?.Log("Eidet", $"Connected to {url} (v{health.Version})");
            _toastService?.Show("Eidet memory connected", ToastType.Success);

            // Auto-intake for already-open projects
            if (openProjectPaths is { Count: > 0 })
            {
                _ = Task.Run(async () =>
                {
                    foreach (var path in openProjectPaths)
                    {
                        try { await OnProjectOpenedAsync(path, ct); }
                        catch { /* don't block tab restore */ }
                    }
                }, ct);
            }

            return true;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _status = MemoryConnectionStatus.Error;
                _errorMessage = ex.Message;
            }
            _toastService?.Show($"Eidet: {ex.Message}", ToastType.Error);
            return false;
        }
    }

    /// <summary>Disconnect and clean up.</summary>
    public void Disconnect()
    {
        _client?.Dispose();
        lock (_lock)
        {
            _client = null;
            _status = MemoryConnectionStatus.Disabled;
            _errorMessage = null;
            _version = null;
            _documentCount = 0;
            _connectedSince = null;
        }
    }

    /// <summary>Called when settings change — reconnect if needed.</summary>
    public async Task OnSettingsChangedAsync(CancellationToken ct = default)
    {
        var config = _configService.Load();
        if (!config.Settings.Memory.Enabled)
        {
            if (IsConnected)
            {
                Disconnect();
                _toastService?.Show("Eidet memory disabled", ToastType.Info);
            }
            return;
        }

        if (!IsConnected)
        {
            var openPaths = config.OpenFolders.ToList();
            await TryConnectAsync(openPaths, ct);
        }
    }

    /// <summary>Called when a project tab opens. Triggers intake if first time for this repo.</summary>
    public async Task OnProjectOpenedAsync(string projectPath, CancellationToken ct = default)
    {
        if (!IsConnected || _client is null) return;

        var repoId = RepoIdNormalizer.Normalize(projectPath);
        bool alreadyIntaked;
        lock (_lock) alreadyIntaked = !_intakedRepos.Add(repoId);
        if (alreadyIntaked) return;

        try
        {
            var result = await _client.IntakeAsync(projectPath, ct);
            if (result is { NewCount: > 0 })
                _toastService?.Show($"Eidet: ingested {result.NewCount} entries from {Path.GetFileName(projectPath)}", ToastType.Info);
        }
        catch (Exception ex)
        {
            _debugLog?.Log("Eidet", $"Intake failed for {projectPath}: {ex.Message}");
        }
    }

    /// <summary>Manually trigger intake (command palette).</summary>
    public async Task<string> RunIntakeAsync(string projectPath, CancellationToken ct = default)
    {
        if (!IsConnected || _client is null)
            return "Eidet is not connected.";

        try
        {
            var result = await _client.IntakeAsync(projectPath, ct);
            var msg = $"Intake complete: {result?.NewCount ?? 0} new, {result?.SkippedCount ?? 0} skipped";
            _toastService?.Show(msg, result?.NewCount > 0 ? ToastType.Success : ToastType.Info);

            // Clear the "already intaked" flag
            var repoId = RepoIdNormalizer.Normalize(projectPath);
            lock (_lock) _intakedRepos.Remove(repoId);

            return msg;
        }
        catch (Exception ex)
        {
            var msg = $"Intake failed: {ex.Message}";
            _toastService?.Show(msg, ToastType.Error);
            return msg;
        }
    }

    /// <summary>Get stats for a repo (for UI display).</summary>
    public async Task<EidetStatsResponse?> GetStatsAsync(string repoId, CancellationToken ct = default)
    {
        if (!IsConnected || _client is null) return null;
        try { return await _client.GetStatsAsync(repoId, ct); }
        catch { return null; }
    }

    /// <summary>Test connection to a specific URL (for Settings UI). Uses full /api/status for details.</summary>
    public static async Task<EidetStatusResponse?> TestConnectionAsync(string url, CancellationToken ct = default)
    {
        using var client = new EidetClient(url);
        return await client.GetStatusAsync(ct);
    }
}
