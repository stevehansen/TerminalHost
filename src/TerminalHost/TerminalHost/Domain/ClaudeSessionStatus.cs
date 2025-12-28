namespace TerminalHost.Domain;

/// <summary>
/// Status of a Claude Code session.
/// </summary>
public enum ClaudeSessionStatus
{
    /// <summary>Session is currently running.</summary>
    Running,

    /// <summary>Session completed successfully.</summary>
    Success,

    /// <summary>Session failed with errors.</summary>
    Failed,

    /// <summary>Session was abandoned by user.</summary>
    Abandoned
}
