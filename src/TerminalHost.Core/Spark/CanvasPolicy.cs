using System;
using System.Collections.Generic;
using System.Linq;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Spark;

/// <summary>
/// Pure routing and ready-time policy for the Spark canvas. Sibling to
/// <c>SparkCanvasOrchestrator.Reduce</c>: the reducer governs state transitions,
/// this class governs the rules applied within a state.
/// </summary>
public static class CanvasPolicy
{
    /// <summary>
    /// Should this activity event be forwarded to the canvas in the given state?
    /// <list type="bullet">
    ///   <item><see cref="CanvasState.Single"/>: only events whose SessionId matches the current session.</item>
    ///   <item><see cref="CanvasState.Multi"/>: all events.</item>
    ///   <item><see cref="CanvasState.Empty"/> / <see cref="CanvasState.Replay"/>: none.</item>
    /// </list>
    /// </summary>
    public static bool ShouldForward(CanvasState state, ActivityEvent evt) => state switch
    {
        CanvasState.Single s => string.Equals(s.SessionId, evt.SessionId, StringComparison.Ordinal),
        CanvasState.Multi => true,
        _ => false,
    };

    /// <summary>
    /// On transport-ready, returns the session id to auto-open, or <c>null</c>
    /// if the orchestrator should fall back to
    /// <c>SetSession(null, "Waiting for session...")</c>.
    /// <list type="bullet">
    ///   <item><see cref="CanvasState.Single"/>: re-opens the current session (state survived a panel reload).</item>
    ///   <item><see cref="CanvasState.Empty"/>: first live session, else most-recent session, else null.</item>
    ///   <item><see cref="CanvasState.Multi"/> / <see cref="CanvasState.Replay"/>: null (fall back to waiting card).</item>
    /// </list>
    /// </summary>
    public static string? AutoOpenOnReady(CanvasState state, IReadOnlyList<SessionListItem> sessions) => state switch
    {
        CanvasState.Single s => s.SessionId,
        CanvasState.Empty => (sessions.FirstOrDefault(x => x.IsLive) ?? sessions.FirstOrDefault())?.SessionId,
        _ => null,
    };
}
