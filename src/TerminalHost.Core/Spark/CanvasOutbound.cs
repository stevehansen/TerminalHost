using System.Collections.Generic;

namespace TerminalHost.Core.Spark;

/// <summary>
/// Closed union of all messages the host can send to the JS canvas.
/// Each variant maps to one <c>{"action": "..."}</c> envelope verb the JS side
/// already handles in <c>web/spark/events.js</c>.
/// </summary>
public abstract record CanvasOutbound
{
    /// <summary>Tell the canvas to clear its current visualization.</summary>
    public sealed record Clear : CanvasOutbound;

    /// <summary>Load a single session's live state.</summary>
    public sealed record LoadState(SessionSnapshot Session) : CanvasOutbound;

    /// <summary>Load a session and all its events for replay playback.</summary>
    public sealed record LoadReplay(SessionSnapshot Session, IReadOnlyList<EventPayload> Events) : CanvasOutbound;

    /// <summary>Forward a single live activity event.</summary>
    public sealed record Event(EventPayload Payload) : CanvasOutbound;

    /// <summary>Set the canvas theme (e.g., "holographic").</summary>
    public sealed record SetTheme(string Theme) : CanvasOutbound;

    /// <summary>Set the session label/id displayed in the canvas UI (id may be null while waiting).</summary>
    public sealed record SetSession(string? Id, string? DisplayName) : CanvasOutbound;

    /// <summary>Push the picker's session list.</summary>
    public sealed record SessionList(IReadOnlyList<SessionListItem> Sessions) : CanvasOutbound;

    /// <summary>Load multi-session observatory state.</summary>
    public sealed record LoadMultiState(IReadOnlyList<SessionSnapshot> Sessions) : CanvasOutbound;
}
