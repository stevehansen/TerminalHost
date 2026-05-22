using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Read-only projection of a tracked Claude Code session, unifying the two
/// underlying data sources (rich activity state, hook-driven live presence).
/// <para>
/// <c>LiveSession</c> is nullable: transcript-only sessions (no hook ever fired)
/// have no live entry. <c>IsLive</c> is derived — the live session's
/// <see cref="LiveSession.IsActive"/> wins when present; otherwise the activity
/// state's derived display state must be Working or WaitingPermission.
/// </para>
/// </summary>
public sealed record SessionView(
    string SessionId,
    SessionSource Source,
    string? ContainerName,
    SessionLifecycle Lifecycle,
    bool IsLive,
    DateTime StartTime,
    DateTime? EndTime,
    SessionActivityState ActivityState,
    LiveSession? LiveSession);
