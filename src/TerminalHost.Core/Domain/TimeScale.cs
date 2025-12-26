namespace TerminalHost.Core.Domain;

/// <summary>
/// Time scale for the Timeline IDE view.
/// </summary>
public enum TimeScale
{
    /// <summary>Show timeline in minutes (finest granularity).</summary>
    Minutes,

    /// <summary>Show timeline in hours (default view).</summary>
    Hours,

    /// <summary>Show timeline in days (coarsest granularity).</summary>
    Days
}
