using System.Collections.Generic;

namespace TerminalHost.Domain
{
    public class UsageStats
    {
        public Dictionary<string, DirectoryUsageStats> DirectoryStats { get; set; } = new Dictionary<string, DirectoryUsageStats>(System.StringComparer.OrdinalIgnoreCase);
    }
}
