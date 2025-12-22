namespace TerminalHost.Core.Domain;

public class UsageStats
{
    public Dictionary<string, DirectoryUsageStats> DirectoryStats { get; set; } = new Dictionary<string, DirectoryUsageStats>(StringComparer.OrdinalIgnoreCase);
}
