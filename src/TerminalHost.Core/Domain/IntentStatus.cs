namespace TerminalHost.Core.Domain;

/// <summary>
/// Status of an intent (development goal/feature) in Timeline IDE.
/// </summary>
public enum IntentStatus
{
    /// <summary>Intent is being actively worked on.</summary>
    Active,

    /// <summary>Intent work is temporarily paused.</summary>
    Paused,

    /// <summary>Intent has been completed successfully.</summary>
    Completed,

    /// <summary>Intent was abandoned and won't be continued.</summary>
    Abandoned
}
