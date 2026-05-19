using System.Collections.Generic;

namespace TerminalHost.Core.Spark;

/// <summary>
/// Discriminated union of the canvas's current view-state. Replaces the implicit
/// state machine encoded in the old VM's <c>_isMultiMode</c> + <c>CurrentSessionId</c>
/// booleans.
/// </summary>
public abstract record CanvasState
{
    /// <summary>No session selected; waiting for the user or for first SessionStart event.</summary>
    public sealed record Empty : CanvasState;

    /// <summary>Watching one live session.</summary>
    public sealed record Single(string SessionId) : CanvasState;

    /// <summary>Observatory mode — watching all sessions simultaneously.</summary>
    public sealed record Multi(IReadOnlySet<string> SessionIds) : CanvasState;

    /// <summary>Replaying a JSONL transcript from disk.</summary>
    public sealed record Replay(string FilePath, string SessionId) : CanvasState;
}
