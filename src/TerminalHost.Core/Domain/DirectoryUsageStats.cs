namespace TerminalHost.Core.Domain;

public class DirectoryUsageStats
{
    /// <summary>
    /// Key: "yyyy-MM-dd"
    /// </summary>
    public Dictionary<string, long> CustomTerminalCharCountsByDay { get; set; } = [];

    /// <summary>
    /// Key: "yyyy-MM-dd"
    /// </summary>
    public Dictionary<string, long> ShellTerminalCharCountsByDay { get; set; } = [];

    /// <summary>
    /// Key: "yyyy-MM-dd"
    /// </summary>
    public Dictionary<string, long> RunTerminalCharCountsByDay { get; set; } = [];

    /// <summary>
    /// Focus time in seconds per day (how long the tab was selected/active).
    /// Key: "yyyy-MM-dd"
    /// </summary>
    public Dictionary<string, long> FocusTimeSecondsByDay { get; set; } = [];
}
