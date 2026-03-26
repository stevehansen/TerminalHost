namespace TerminalHost.Core.Domain;

/// <summary>
/// Status of a Claude Code session in Timeline IDE.
/// </summary>
public enum ClaudeSessionStatus
{
    /// <summary>Session is currently running.</summary>
    Running,

    /// <summary>Session completed successfully.</summary>
    Success,

    /// <summary>Session failed (error or user abort).</summary>
    Failed,

    /// <summary>Session was abandoned in favor of a different approach.</summary>
    Abandoned,

    /// <summary>Session was closed due to inactivity timeout (no Stop hook received).</summary>
    TimedOut
}
