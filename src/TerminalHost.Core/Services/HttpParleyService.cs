using System.Net.Http;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Production adapter for <see cref="IParleyService"/>: talks to the Parley hub's loopback
/// HTTP API and keeps one SSE connection to <c>/api/events</c> open while watching, so
/// consumers learn about new messages without polling.
///
/// Availability: while the watcher runs, queries are skipped (empty result) whenever the
/// feed is disconnected — this keeps UI/API polling cheap when the hub isn't running
/// (it only starts with the first Claude session that launches the Parley shim).
/// Without a watcher, every query simply tries the hub.
/// </summary>
public sealed class HttpParleyService : IParleyService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    // The hub sends a heartbeat every 25s; silence beyond this means the connection is dead.
    private static readonly TimeSpan FeedIdleTimeout = TimeSpan.FromSeconds(60);

    private readonly IConfigurationService _configService;
    private readonly IDebugLogService? _debugLog;
    private readonly HttpClient _http;
    private readonly object _lock = new();

    private ParleySettings? _settings;
    private CancellationTokenSource? _watchCts;
    private string? _watchedUrl;
    private volatile bool _reachable;

    public HttpParleyService(IConfigurationService configService, IDebugLogService? debugLog = null)
        : this(configService, new HttpClientHandler(), debugLog)
    {
    }

    internal HttpParleyService(IConfigurationService configService, HttpMessageHandler handler, IDebugLogService? debugLog = null)
    {
        _configService = configService;
        _debugLog = debugLog;
        // Infinite: the SSE feed never completes; plain requests use RequestTimeout instead.
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public event Action? StateChanged;

    public bool IsAvailable => Settings.Enabled && _reachable;

    private ParleySettings Settings
    {
        get
        {
            lock (_lock)
                return _settings ??= LoadSettings();
        }
    }

    private bool IsWatching
    {
        get { lock (_lock) return _watchCts != null; }
    }

    public void ApplySettings()
    {
        CancellationTokenSource? stopped = null;
        CancellationTokenSource? started = null;
        string url;
        bool enabled;

        lock (_lock)
        {
            _settings = LoadSettings();
            url = _settings.HubUrl;
            enabled = _settings.Enabled;

            var keepRunning = _settings.Enabled && _watchCts != null && _watchedUrl == url;
            if (!keepRunning)
            {
                stopped = _watchCts;
                _watchCts = null;
                _watchedUrl = null;
                if (_settings.Enabled)
                {
                    started = new CancellationTokenSource();
                    _watchCts = started;
                    _watchedUrl = url;
                }
            }
        }

        if (stopped != null)
        {
            stopped.Cancel();
            stopped.Dispose();
        }

        // Anything that changed (new URL, disabled) invalidates what consumers last saw.
        if (stopped != null || started != null || !enabled)
            SetReachable(false);

        if (started != null)
        {
            var ct = started.Token;
            _ = Task.Run(() => WatchLoopAsync(url, ct));
        }
    }

    public async Task<IReadOnlyList<ParleyTopic>> GetTopicsAsync(CancellationToken ct = default)
    {
        var topicsTask = GetJsonAsync<List<ParleyTopic>>("/api/topics", ct);
        var sessionsTask = GetJsonAsync<List<ParleySession>>("/api/sessions", ct);
        var topics = await topicsTask ?? [];
        var sessions = (await sessionsTask ?? []).ToDictionary(s => s.Name, StringComparer.Ordinal);

        foreach (var topic in topics)
        {
            topic.SubscriberDetails = topic.Subscribers
                .Select(name =>
                {
                    sessions.TryGetValue(name, out var s);
                    return new ParleySubscriber
                    {
                        Name = name,
                        ProjectName = s?.ProjectName,
                        WorkingDir = s?.WorkingDir,
                        IsConnected = s?.IsConnected ?? false,
                    };
                })
                .ToList();
        }
        return topics;
    }

    public async Task<IReadOnlyList<ParleySession>> GetSessionsAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<ParleySession>>("/api/sessions", ct) ?? [];

    public async Task<IReadOnlyList<ParleyMessage>> GetRecentMessagesAsync(int count = 30, CancellationToken ct = default)
        => await GetJsonAsync<List<ParleyMessage>>($"/api/messages?count={Math.Clamp(count, 1, 500)}", ct) ?? [];

    public async Task<string?> GetHubVersionAsync(string? hubUrl = null, CancellationToken ct = default)
    {
        var baseUrl = string.IsNullOrWhiteSpace(hubUrl) ? Settings.HubUrl : hubUrl;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.GetAsync(BuildUrl(baseUrl, "/api/health"), cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var health = JsonSerializer.Deserialize<HealthResponse>(json, JsonOptions);
            return string.Equals(health?.Name, "parley", StringComparison.OrdinalIgnoreCase) ? health!.Version ?? "" : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<T?> GetJsonAsync<T>(string pathAndQuery, CancellationToken ct) where T : class
    {
        var settings = Settings;
        if (!settings.Enabled) return null;

        var watching = IsWatching;
        if (watching && !_reachable) return null; // feed is down: don't wait on a dead hub

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.GetAsync(BuildUrl(settings.HubUrl, pathAndQuery), cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            if (!watching) SetReachable(true);
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // something else answers on that port
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (!watching) SetReachable(false);
            return null;
        }
    }

    private async Task WatchLoopAsync(string hubUrl, CancellationToken ct)
    {
        var delay = InitialRetryDelay;
        var url = BuildUrl(hubUrl, "/api/events");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("text/event-stream");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                if (!_reachable) _debugLog?.Log("Parley", $"Connected to hub at {hubUrl}");
                SetReachable(true);
                delay = InitialRetryDelay;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await ReadFeedAsync(stream, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Hub not running, went away, or the feed went silent: retry with backoff.
            }

            if (ct.IsCancellationRequested) return;
            if (_reachable) _debugLog?.Log("Parley", "Hub connection lost; retrying");
            SetReachable(false);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
    }

    /// <summary>Reads SSE frames until the stream ends; raises StateChanged per message/changed event.</summary>
    private async Task ReadFeedAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        string? eventName = null;
        while (true)
        {
            string? line;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                idle.CancelAfter(FeedIdleTimeout);
                line = await reader.ReadLineAsync(idle.Token);
            }
            if (line == null) return; // stream closed by hub

            if (line.Length == 0)
            {
                if (eventName is "message" or "changed")
                    RaiseStateChanged();
                eventName = null;
            }
            else if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
            }
            // "data:", "id:" and ": heartbeat" lines carry nothing we need: consumers re-query.
        }
    }

    private void SetReachable(bool reachable)
    {
        if (_reachable == reachable) return;
        _reachable = reachable;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception ex) { _debugLog?.Warn("Parley", $"StateChanged handler failed: {ex.Message}"); }
    }

    private ParleySettings LoadSettings()
    {
        var settings = _configService.Load().Settings.Parley ?? new ParleySettings();
        settings.EnsureDefaults();
        return settings;
    }

    private static string BuildUrl(string baseUrl, string pathAndQuery) => baseUrl.TrimEnd('/') + pathAndQuery;

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _watchCts;
            _watchCts = null;
        }
        cts?.Cancel();
        cts?.Dispose();
        _http.Dispose();
    }

    private sealed class HealthResponse
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
    }
}
