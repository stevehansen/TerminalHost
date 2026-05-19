using System;
using System.Collections.Generic;

namespace TerminalHost.Core.Spark;

/// <summary>
/// Canvas-shaped projection of an <see cref="Core.Domain.ActivityEvent"/>.
/// Field names match what the JS canvas's event handlers expect.
/// </summary>
public sealed record EventPayload
{
    public string Type { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string? AgentId { get; init; }
    public DateTime Timestamp { get; init; }
    public IReadOnlyDictionary<string, object?> Data { get; init; } =
        new Dictionary<string, object?>();
}
