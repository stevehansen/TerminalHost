namespace TerminalHost.Domain;

/// <summary>
/// Time scale for the timeline view.
/// </summary>
public enum TimeScale
{
    /// <summary>View in minutes (zoomed in).</summary>
    Minutes,

    /// <summary>View in hours (default).</summary>
    Hours,

    /// <summary>View in days (zoomed out).</summary>
    Days
}
