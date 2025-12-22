using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IStatisticsService : IDisposable
{
    void IncrementCharCount(string directory, string terminalType, int charCount);
    UsageStats GetStats();
    void SaveStats();
}
