using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Central event bus for the API system. Manages subscribers with filter matching
/// and maintains a ring buffer of recent events for SSE reconnection.
/// </summary>
public class EventAggregatorService : IEventAggregatorService
{
    private const int MaxRecentEvents = 100;

    private readonly ConcurrentQueue<ApiEvent> _recentEvents = new();
    private readonly ReaderWriterLockSlim _subscriberLock = new();
    private readonly List<Subscription> _subscribers = new();
    private int _recentEventCount;

    public IReadOnlyList<ApiEvent> RecentEvents
    {
        get => _recentEvents.ToArray();
    }

    public void Publish(ApiEvent apiEvent)
    {
        // Add to ring buffer
        _recentEvents.Enqueue(apiEvent);
        if (Interlocked.Increment(ref _recentEventCount) > MaxRecentEvents)
        {
            _recentEvents.TryDequeue(out _);
            Interlocked.Decrement(ref _recentEventCount);
        }

        // Notify subscribers
        List<Subscription> snapshot;
        _subscriberLock.EnterReadLock();
        try
        {
            snapshot = _subscribers.ToList();
        }
        finally
        {
            _subscriberLock.ExitReadLock();
        }

        foreach (var sub in snapshot)
        {
            if (MatchesFilter(apiEvent.Type, sub.Filter))
            {
                try
                {
                    sub.Handler(apiEvent);
                }
                catch
                {
                    // Don't let subscriber exceptions propagate
                }
            }
        }
    }

    public IDisposable Subscribe(Action<ApiEvent> handler, string? eventFilter = null)
    {
        var subscription = new Subscription(handler, eventFilter ?? "*", this);

        _subscriberLock.EnterWriteLock();
        try
        {
            _subscribers.Add(subscription);
        }
        finally
        {
            _subscriberLock.ExitWriteLock();
        }

        return subscription;
    }

    private void Unsubscribe(Subscription subscription)
    {
        _subscriberLock.EnterWriteLock();
        try
        {
            _subscribers.Remove(subscription);
        }
        finally
        {
            _subscriberLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if an event type matches a filter pattern.
    /// Supports: "*" (all), "repo.*" (prefix), "repo.commit" (exact), comma-separated patterns.
    /// </summary>
    internal static bool MatchesFilter(string eventType, string filter)
    {
        if (string.IsNullOrEmpty(filter) || filter == "*")
            return true;

        var patterns = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pattern in patterns)
        {
            if (pattern == "*")
                return true;

            if (pattern.EndsWith(".*"))
            {
                var prefix = pattern[..^2];
                if (eventType.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase) ||
                    eventType.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (eventType.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly EventAggregatorService _owner;
        private bool _disposed;

        public Action<ApiEvent> Handler { get; }
        public string Filter { get; }

        public Subscription(Action<ApiEvent> handler, string filter, EventAggregatorService owner)
        {
            Handler = handler;
            Filter = filter;
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.Unsubscribe(this);
        }
    }
}
