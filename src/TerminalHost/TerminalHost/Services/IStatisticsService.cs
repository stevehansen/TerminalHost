using TerminalHost.Domain;

namespace TerminalHost.Services;

public interface IStatisticsService : IDisposable
{
    void IncrementCharCount(string directory, string terminalType, int charCount);
    UsageStats GetStats();
    void SaveStats();
}
