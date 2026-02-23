using System;
using System.Collections.Generic;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Aggregates events from various TerminalHost subsystems
/// and distributes them to SSE clients and webhook endpoints.
/// </summary>
public interface IEventAggregatorService
{
    /// <summary>Publish an event to all subscribers.</summary>
    void Publish(ApiEvent apiEvent);

    /// <summary>
    /// Subscribe to events with optional filter.
    /// Filter supports: "*" = all, "repo.*" = prefix match, "repo.commit" = exact match.
    /// Multiple patterns can be comma-separated.
    /// </summary>
    IDisposable Subscribe(Action<ApiEvent> handler, string? eventFilter = null);

    /// <summary>Recent event buffer for SSE Last-Event-ID reconnection (last 100 events).</summary>
    IReadOnlyList<ApiEvent> RecentEvents { get; }
}
