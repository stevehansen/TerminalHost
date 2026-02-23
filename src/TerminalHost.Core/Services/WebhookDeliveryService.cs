using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Manages webhook delivery with debounce, retry, batching, and HMAC-SHA256 signing.
/// Subscribes to IEventAggregatorService for incoming events.
/// </summary>
public class WebhookDeliveryService : IWebhookDeliveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly int[] RetryDelaysMs = { 0, 5000, 30000, 300000 }; // immediate, 5s, 30s, 5min

    private readonly IConfigurationService _configService;
    private readonly IEventAggregatorService _eventAggregator;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _concurrencySemaphore = new(5); // Max 5 concurrent deliveries
    private readonly ConcurrentDictionary<string, DebounceState> _debounceTimers = new();
    private readonly ConcurrentDictionary<string, List<ApiEvent>> _batchBuffers = new();
    private readonly ConcurrentDictionary<string, Timer> _batchTimers = new();

    private IDisposable? _subscription;
    private int _totalDelivered;
    private int _totalFailed;
    private int _pendingRetries;
    private readonly ConcurrentDictionary<string, int> _deliveriesByEndpoint = new();
    private readonly ConcurrentDictionary<string, int> _failuresByEndpoint = new();
    private bool _disposed;

    public WebhookDeliveryService(
        IConfigurationService configService,
        IEventAggregatorService eventAggregator)
    {
        _configService = configService;
        _eventAggregator = eventAggregator;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Subscribe to all events
        _subscription = _eventAggregator.Subscribe(OnEventReceived, "*");
    }

    public void Enqueue(ApiEvent apiEvent)
    {
        OnEventReceived(apiEvent);
    }

    public WebhookDeliveryStats GetStats()
    {
        return new WebhookDeliveryStats
        {
            TotalDelivered = _totalDelivered,
            TotalFailed = _totalFailed,
            PendingRetries = _pendingRetries,
            DeliveriesByEndpoint = new Dictionary<string, int>(_deliveriesByEndpoint),
            FailuresByEndpoint = new Dictionary<string, int>(_failuresByEndpoint)
        };
    }

    public async Task TestWebhooksAsync()
    {
        var config = _configService.Load();
        var webhooks = config.Settings.Api.Webhooks.Where(w => w.Enabled).ToList();

        var testEvent = new ApiEvent
        {
            Type = "test.webhook",
            Data = new { message = "Test webhook delivery from TerminalHost" }
        };

        foreach (var webhook in webhooks)
        {
            await DeliverSingleAsync(webhook, testEvent, maxRetries: 0);
        }
    }

    private void OnEventReceived(ApiEvent apiEvent)
    {
        if (_disposed) return;

        var config = _configService.Load();
        if (!config.Settings.Api.EnableWebhooks) return;

        var webhooks = config.Settings.Api.Webhooks.Where(w => w.Enabled).ToList();

        foreach (var webhook in webhooks)
        {
            if (!EventMatchesFilter(apiEvent.Type, webhook.Events))
                continue;

            if (webhook.BatchWindowMs > 0)
            {
                EnqueueBatch(webhook, apiEvent);
            }
            else if (webhook.DebounceMs > 0)
            {
                EnqueueDebounced(webhook, apiEvent);
            }
            else
            {
                _ = Task.Run(() => DeliverWithRetryAsync(webhook, apiEvent));
            }
        }
    }

    private void EnqueueDebounced(WebhookEndpoint webhook, ApiEvent apiEvent)
    {
        var key = $"{webhook.Id}:{apiEvent.Type}:{apiEvent.RepoIndex}";

        var state = _debounceTimers.GetOrAdd(key, _ => new DebounceState());

        lock (state)
        {
            state.LatestEvent = apiEvent;
            state.Timer?.Dispose();
            state.Timer = new Timer(_ =>
            {
                ApiEvent? eventToSend;
                lock (state)
                {
                    eventToSend = state.LatestEvent;
                    state.LatestEvent = null;
                }
                if (eventToSend != null)
                {
                    _ = Task.Run(() => DeliverWithRetryAsync(webhook, eventToSend));
                }
                _debounceTimers.TryRemove(key, out var _removed);
            }, null, webhook.DebounceMs, Timeout.Infinite);
        }
    }

    private void EnqueueBatch(WebhookEndpoint webhook, ApiEvent apiEvent)
    {
        var key = webhook.Id;

        var buffer = _batchBuffers.GetOrAdd(key, _ => new List<ApiEvent>());

        lock (buffer)
        {
            buffer.Add(apiEvent);
        }

        // Start batch timer if not already running
        _batchTimers.GetOrAdd(key, _ => new Timer(_ =>
        {
            List<ApiEvent> batch;
            if (_batchBuffers.TryRemove(key, out var buf))
            {
                lock (buf)
                {
                    batch = buf.ToList();
                    buf.Clear();
                }
            }
            else return;

            _batchTimers.TryRemove(key, out var timer);
            timer?.Dispose();

            if (batch.Count > 0)
            {
                _ = Task.Run(() => DeliverBatchAsync(webhook, batch));
            }
        }, null, webhook.BatchWindowMs, Timeout.Infinite));
    }

    private async Task DeliverWithRetryAsync(WebhookEndpoint webhook, ApiEvent apiEvent)
    {
        var maxRetries = Math.Min(webhook.MaxRetries, RetryDelaysMs.Length - 1);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                Interlocked.Increment(ref _pendingRetries);
                await Task.Delay(RetryDelaysMs[attempt]);
                Interlocked.Decrement(ref _pendingRetries);
            }

            var success = await DeliverSingleAsync(webhook, apiEvent, maxRetries: 0);
            if (success) return;
        }

        // All retries exhausted
        Interlocked.Increment(ref _totalFailed);
        _failuresByEndpoint.AddOrUpdate(webhook.Id, 1, (_, v) => v + 1);
    }

    private async Task DeliverBatchAsync(WebhookEndpoint webhook, List<ApiEvent> events)
    {
        var payload = new { batch = true, events };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await SendHttpPostAsync(webhook, json, "batch");
    }

    private async Task<bool> DeliverSingleAsync(WebhookEndpoint webhook, ApiEvent apiEvent, int maxRetries)
    {
        var json = JsonSerializer.Serialize(apiEvent, JsonOptions);
        return await SendHttpPostAsync(webhook, json, apiEvent.Type);
    }

    private async Task<bool> SendHttpPostAsync(WebhookEndpoint webhook, string json, string eventType)
    {
        await _concurrencySemaphore.WaitAsync();
        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add headers
            content.Headers.Add("X-TerminalHost-Event", eventType);
            content.Headers.Add("X-TerminalHost-Delivery", Guid.NewGuid().ToString("N")[..12]);
            content.Headers.Add("X-TerminalHost-Timestamp",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

            // HMAC-SHA256 signature
            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhook.Secret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
                var signature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
                content.Headers.Add("X-TerminalHost-Signature", signature);
            }

            var response = await _httpClient.PostAsync(webhook.Url, content);

            if (response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _totalDelivered);
                _deliveriesByEndpoint.AddOrUpdate(webhook.Id, 1, (_, v) => v + 1);
                return true;
            }

            // Don't retry client errors (4xx)
            if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
            {
                Interlocked.Increment(ref _totalFailed);
                _failuresByEndpoint.AddOrUpdate(webhook.Id, 1, (_, v) => v + 1);
                return true; // Don't retry
            }

            return false; // Retry on 5xx
        }
        catch
        {
            return false; // Retry on network errors
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private static bool EventMatchesFilter(string eventType, List<string> filters)
    {
        if (filters.Count == 0 || filters.Contains("*"))
            return true;

        foreach (var filter in filters)
        {
            if (filter == "*") return true;

            if (filter.EndsWith(".*"))
            {
                var prefix = filter[..^2];
                if (eventType.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase) ||
                    eventType.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (eventType.Equals(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _subscription?.Dispose();

        foreach (var state in _debounceTimers.Values)
        {
            lock (state) state.Timer?.Dispose();
        }
        _debounceTimers.Clear();

        foreach (var timer in _batchTimers.Values)
        {
            timer?.Dispose();
        }
        _batchTimers.Clear();

        _concurrencySemaphore.Dispose();
        _httpClient.Dispose();
    }

    private sealed class DebounceState
    {
        public Timer? Timer;
        public ApiEvent? LatestEvent;
    }
}
