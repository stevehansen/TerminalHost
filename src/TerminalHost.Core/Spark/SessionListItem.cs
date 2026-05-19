using System;

namespace TerminalHost.Core.Spark;

/// <summary>
/// Lightweight session descriptor for the session picker / multi-mode list.
/// </summary>
public sealed record SessionListItem
{
    public string SessionId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string ProjectPath { get; init; } = "";
    public bool IsLive { get; init; }
    public DateTime StartTime { get; init; }
}
