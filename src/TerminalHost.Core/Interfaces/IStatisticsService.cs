using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IStatisticsService : IDisposable
{
    void IncrementCharCount(string directory, string terminalType, int charCount);
    UsageStats GetStats();
    void SaveStats();

    /// <summary>
    /// Records focus time (seconds the tab was active) for a directory.
    /// </summary>
    void RecordFocusTime(string directory, int seconds);

    /// <summary>
    /// Gets the total focus time (in seconds) for a directory over the specified number of days.
    /// </summary>
    long GetFocusTimeForPeriod(string directory, int days);

    /// <summary>
    /// Gets the total character count (all terminal types) for a directory over the specified number of days.
    /// </summary>
    long GetCharCountForPeriod(string directory, int days);
}
