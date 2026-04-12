namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Lightweight diagnostic logging service for runtime debugging.
/// Messages are stored in a ring buffer and displayed in the Debug Log panel.
/// Replaces Debug.WriteLine for published builds where debugger is not attached.
/// </summary>
public interface IDebugLogService
{
    /// <summary>Log a message from a named source.</summary>
    void Log(string source, string message);

    /// <summary>Log a warning from a named source.</summary>
    void Warn(string source, string message);

    /// <summary>Log an error from a named source.</summary>
    void Error(string source, string message);

    /// <summary>Recent log entries (newest first).</summary>
    IReadOnlyList<DebugLogEntry> RecentEntries { get; }

    /// <summary>Raised when a new entry is added.</summary>
    event Action<DebugLogEntry>? EntryAdded;

    /// <summary>Clear all entries.</summary>
    void Clear();
}

public enum DebugLogLevel { Info, Warning, Error }

public class DebugLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public DebugLogLevel Level { get; init; }
    public string Source { get; init; } = "";
    public string Message { get; init; } = "";

    public override string ToString() => $"[{Timestamp:HH:mm:ss.fff}] [{Source}] {Message}";
}
