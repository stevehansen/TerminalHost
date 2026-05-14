using System.Net.Http;
using System.Text;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Production adapter for <see cref="IEidetService"/>.
/// Wraps the Eidet REST API and owns the connection-state machine,
/// intake tracking, and user-facing toasts/debug-log integration.
/// Folds the former EidetClient + EidetClientService into a single deep module.
/// </summary>
public sealed class HttpEidetService : IEidetService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConfigurationService _configService;
    private readonly IToastService? _toastService;
    private readonly IDebugLogService? _debugLog;

    private HttpClient? _http;
    private MemoryConnectionStatus _connectionStatus = MemoryConnectionStatus.Disabled;
    private string? _errorMessage;
    private string? _version;
    private int _documentCount;
    private DateTime? _connectedSince;
    private readonly HashSet<string> _intakedRepos = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public HttpEidetService(
        IConfigurationService configService,
        IToastService? toastService = null,
        IDebugLogService? debugLog = null)
    {
        _configService = configService;
        _toastService = toastService;
        _debugLog = debugLog;
    }

    public MemoryStatus Status
    {
        get
        {
            var url = _configService.Load().Settings.Memory.EidetUrl;
            lock (_lock)
            {
                return new MemoryStatus
                {
                    ConnectionStatus = _connectionStatus,
                    ErrorMessage = _errorMessage,
                    ServerUrl = url,
                    Version = _version,
                    DocumentCount = _documentCount,
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
            if (_connectionStatus == MemoryConnectionStatus.Connected)
                return true;
            _connectionStatus = MemoryConnectionStatus.Connecting;
            _errorMessage = null;
        }
        RaiseStatusChanged();

        try
        {
            var url = config.Settings.Memory.EidetUrl;
            var client = CreateHttpClient(url);
            var health = await CheckHealthAsync(client, ct);

            if (health is null || !health.IsHealthy)
            {
                client.Dispose();
                lock (_lock)
                {
                    _connectionStatus = MemoryConnectionStatus.Error;
                    _errorMessage = "Eidet health check failed — is the service running?";
                }
                RaiseStatusChanged();
                _toastService?.Show("Eidet: service unreachable", ToastType.Error);
                return false;
            }

            HttpClient? previous;
            lock (_lock)
            {
                previous = _http;
                _http = client;
                _connectionStatus = MemoryConnectionStatus.Connected;
                _version = health.Version;
                _connectedSince = DateTime.UtcNow;
                _errorMessage = null;
            }
            previous?.Dispose();
            RaiseStatusChanged();

            _debugLog?.Log("Eidet", $"Connected to {url} (v{health.Version})");
            _toastService?.Show("Eidet memory connected", ToastType.Success);

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
                _connectionStatus = MemoryConnectionStatus.Error;
                _errorMessage = ex.Message;
            }
            RaiseStatusChanged();
            _toastService?.Show($"Eidet: {ex.Message}", ToastType.Error);
            return false;
        }
    }

    public void Disconnect()
    {
        HttpClient? previous;
        lock (_lock)
        {
            previous = _http;
            _http = null;
            _connectionStatus = MemoryConnectionStatus.Disabled;
            _errorMessage = null;
            _version = null;
            _documentCount = 0;
            _connectedSince = null;
        }
        previous?.Dispose();
        RaiseStatusChanged();
    }

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

    public async Task OnProjectOpenedAsync(string projectPath, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null) return;

        var repoId = RepoIdNormalizer.Normalize(projectPath);
        bool alreadyIntaked;
        lock (_lock) alreadyIntaked = !_intakedRepos.Add(repoId);
        if (alreadyIntaked) return;

        try
        {
            var result = await IntakeAsync(http, projectPath, ct);
            if (result is { NewCount: > 0 })
                _toastService?.Show($"Eidet: ingested {result.NewCount} entries from {Path.GetFileName(projectPath)}", ToastType.Info);
        }
        catch (Exception ex)
        {
            _debugLog?.Log("Eidet", $"Intake failed for {projectPath}: {ex.Message}");
        }
    }

    public async Task<string> RunIntakeAsync(string projectPath, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null)
            return "Eidet is not connected.";

        try
        {
            var result = await IntakeAsync(http, projectPath, ct);
            var msg = $"Intake complete: {result?.NewCount ?? 0} new, {result?.SkippedCount ?? 0} skipped";
            _toastService?.Show(msg, result?.NewCount > 0 ? ToastType.Success : ToastType.Info);

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

    public async Task<EidetStatsResponse?> GetStatsAsync(string repoId, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null) return null;
        try
        {
            var response = await http.GetAsync($"/api/eidet/stats?repo={Uri.EscapeDataString(repoId)}", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<EidetStatsResponse>(json, JsonOptions);
        }
        catch { return null; }
    }

    public async Task<EidetSearchResponse?> SearchAsync(string repoId, string query, string? type = null, int limit = 100, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null) return null;
        var url = $"/api/eidet/search?repo={Uri.EscapeDataString(repoId)}&q={Uri.EscapeDataString(query)}&limit={limit}";
        if (!string.IsNullOrEmpty(type))
            url += $"&type={Uri.EscapeDataString(type)}";
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetSearchResponse>(json, JsonOptions);
    }

    public async Task<EidetMemoriesResponse?> BrowseAsync(string repoId, string? type = null, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null) return null;
        var url = $"/api/eidet/browse?repo={Uri.EscapeDataString(repoId)}&take=200";
        if (!string.IsNullOrEmpty(type))
            url += $"&type={Uri.EscapeDataString(type)}";
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetMemoriesResponse>(json, JsonOptions);
    }

    public async Task<EidetLayersResponse?> GetLayersAsync(string repoId, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null) return null;
        var response = await http.GetAsync($"/api/eidet/layers?repo={Uri.EscapeDataString(repoId)}", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetLayersResponse>(json, JsonOptions);
    }

    public async Task<bool> ForgetAsync(string id, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null) return false;
        var response = await http.DeleteAsync($"/api/eidet/{Uri.EscapeDataString(id)}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<(int StatusCode, string Body, string ContentType)> ProxyGetAsync(string pathAndQuery, CancellationToken ct = default)
    {
        var http = GetHttpOrNull();
        if (http is null)
            return (503, JsonSerializer.Serialize(new { error = "Eidet memory service not connected." }), "application/json");

        try
        {
            var response = await http.GetAsync(pathAndQuery, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return ((int)response.StatusCode, body, contentType);
        }
        catch (Exception ex)
        {
            return (502, JsonSerializer.Serialize(new { error = ex.Message }), "application/json");
        }
    }

    public async Task<EidetStatusResponse?> TestConnectionAsync(string url, CancellationToken ct = default)
    {
        using var probe = CreateHttpClient(url);
        try
        {
            var response = await probe.GetAsync("/api/status", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<EidetStatusResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient(string baseUrl) => new()
    {
        BaseAddress = new Uri(baseUrl.TrimEnd('/')),
        Timeout = TimeSpan.FromSeconds(10),
    };

    private HttpClient? GetHttpOrNull()
    {
        lock (_lock) return _connectionStatus == MemoryConnectionStatus.Connected ? _http : null;
    }

    private static async Task<EidetHealthResponse?> CheckHealthAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            var response = await http.GetAsync("/api/health", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<EidetHealthResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<EidetIntakeResponse?> IntakeAsync(HttpClient http, string projectPath, CancellationToken ct)
    {
        var repo = RepoIdNormalizer.Normalize(projectPath);
        var url = $"/api/eidet/intake?repo={Uri.EscapeDataString(repo)}&path={Uri.EscapeDataString(projectPath)}";
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<EidetIntakeResponse>(json, JsonOptions);
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, Status);

    public void Dispose()
    {
        HttpClient? previous;
        lock (_lock)
        {
            previous = _http;
            _http = null;
        }
        previous?.Dispose();
    }
}
