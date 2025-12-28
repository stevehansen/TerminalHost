namespace TerminalHost.Domain;

/// <summary>
/// Status of an intent (development goal).
/// </summary>
public enum IntentStatus
{
    /// <summary>Currently being worked on.</summary>
    Active,

    /// <summary>Temporarily paused.</summary>
    Paused,

    /// <summary>Work completed successfully.</summary>
    Completed,

    /// <summary>Work abandoned/cancelled.</summary>
    Abandoned
}
