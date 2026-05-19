namespace TerminalHost.Core.Spark;

/// <summary>
/// Closed union of all messages the JS canvas can send to the host.
/// Each variant corresponds to one <c>{"action": "..."}</c> verb the JS side emits.
/// </summary>
public abstract record CanvasInbound
{
    /// <summary>Canvas finished its initial load and is ready to receive messages.</summary>
    public sealed record Ready : CanvasInbound;

    /// <summary>User picked a session in the JS dropdown.</summary>
    public sealed record SelectSession(string SessionId) : CanvasInbound;

    /// <summary>User clicked the refresh button on the picker.</summary>
    public sealed record RefreshSessions : CanvasInbound;

    /// <summary>User asked to enter multi-session observatory mode.</summary>
    public sealed record RequestMultiMode : CanvasInbound;

    /// <summary>User asked to leave multi-session observatory mode.</summary>
    public sealed record ExitMultiMode : CanvasInbound;

    /// <summary>User changed the theme inside the canvas; host should persist it.</summary>
    public sealed record ThemeChanged(string Theme) : CanvasInbound;
}
