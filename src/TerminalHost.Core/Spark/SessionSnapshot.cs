using System;
using System.Collections.Generic;

namespace TerminalHost.Core.Spark;

/// <summary>
/// Canvas-shaped projection of a session. Shared fields for the three concrete
/// variants (<see cref="LiveSessionSnapshot"/>, <see cref="ReplaySessionSnapshot"/>,
/// <see cref="PlaceholderSessionSnapshot"/>) that <see cref="Interfaces.Spark.ISessionCatalog"/>
/// produces.
/// </summary>
/// <remarks>
/// Field names use camelCase via the serializer's naming policy. The shape
/// mirrors what the JS canvas expects today — see <c>web/spark/events.js</c>.
/// </remarks>
public abstract record SnapshotEnvelope
{
    public string SessionId { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public DateTime StartTime { get; init; }
    public string Lifecycle { get; init; } = "Active";

    /// <summary>Agents indexed by id.</summary>
    public IReadOnlyDictionary<string, SnapshotAgent> Agents { get; init; } =
        new Dictionary<string, SnapshotAgent>();

    /// <summary>File-access counters indexed by file path.</summary>
    public IReadOnlyDictionary<string, SnapshotFileActivity> FileActivities { get; init; } =
        new Dictionary<string, SnapshotFileActivity>();
}

/// <summary>Snapshot of a currently-tracked session. Tool calls contain only running entries.</summary>
public sealed record LiveSessionSnapshot : SnapshotEnvelope
{
    public DateTime? EndTime { get; init; }

    /// <summary>Tool calls indexed by toolUseId — running only.</summary>
    public IReadOnlyDictionary<string, SnapshotToolCall> ToolCalls { get; init; } =
        new Dictionary<string, SnapshotToolCall>();

    /// <summary>Recent messages for the feed.</summary>
    public IReadOnlyList<SnapshotMessage> Messages { get; init; } = Array.Empty<SnapshotMessage>();
}

/// <summary>Snapshot synthesized from a parsed JSONL transcript. Tool calls contain all entries.</summary>
public sealed record ReplaySessionSnapshot : SnapshotEnvelope
{
    public DateTime EndTime { get; init; }

    /// <summary>Tool calls indexed by toolUseId — all calls, including completed/errored.</summary>
    public IReadOnlyDictionary<string, SnapshotToolCall> ToolCalls { get; init; } =
        new Dictionary<string, SnapshotToolCall>();
}

/// <summary>Skeleton snapshot for a session known to the timeline but not yet activity-tracked.</summary>
public sealed record PlaceholderSessionSnapshot : SnapshotEnvelope;

public sealed record SnapshotAgent
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsMain { get; init; }
    public string? ParentId { get; init; }
    public string State { get; init; } = "";
    public string? Model { get; init; }
    public string? Task { get; init; }
    public DateTime? SpawnTime { get; init; }
    public DateTime? CompleteTime { get; init; }
    public int ToolCallCount { get; init; }
    public int TokensUsed { get; init; }
    public int LatestContextTokens { get; init; }
    public int TotalOutputTokens { get; init; }
    public int? TokensMax { get; init; }
    public string? CurrentToolUseId { get; init; }
    public SnapshotAgentContext? Context { get; init; }
}

public sealed record SnapshotAgentContext
{
    public int SystemPrompt { get; init; }
    public int UserMessages { get; init; }
    public int ToolResults { get; init; }
    public int Reasoning { get; init; }
    public int SubagentResults { get; init; }
}

public sealed record SnapshotToolCall
{
    public string ToolUseId { get; init; } = "";
    public string? AgentId { get; init; }
    public string ToolName { get; init; } = "";
    public string? InputSummary { get; init; }
    public string? ResultSummary { get; init; }
    public string State { get; init; } = "";
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int? TokenCost { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record SnapshotFileActivity
{
    public int ReadCount { get; init; }
    public int WriteCount { get; init; }
}

public sealed record SnapshotMessage
{
    public string Type { get; init; } = "";
    public string? AgentId { get; init; }
    public string? Content { get; init; }
    public DateTime Timestamp { get; init; }
}
