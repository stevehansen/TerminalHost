using System.Collections.Generic;

namespace TerminalHost.Domain
{
    public class DirectoryUsageStats
    {
        /// <summary>
        /// Key: "yyyy-MM-dd"
        /// </summary>
        public Dictionary<string, long> CustomTerminalCharCountsByDay { get; set; } = new();

        /// <summary>
        /// Key: "yyyy-MM-dd"
        /// </summary>
        public Dictionary<string, long> ShellTerminalCharCountsByDay { get; set; } = new();

        /// <summary>
        /// Key: "yyyy-MM-dd"
        /// </summary>
        public Dictionary<string, long> RunTerminalCharCountsByDay { get; set; } = new();
    }
}