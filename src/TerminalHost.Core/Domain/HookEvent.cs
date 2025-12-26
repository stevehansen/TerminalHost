using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Type of hook event from Claude Code.
/// </summary>
public enum HookEventType
{
    /// <summary>Claude Code session started.</summary>
    SessionStart,

    /// <summary>A file was modified (Write/Edit/MultiEdit tool used).</summary>
    FileChanged,

    /// <summary>Claude Code session stopped.</summary>
    SessionStop
}

/// <summary>
/// A hook event to be processed or queued for later processing.
/// Used for IPC to main application and offline queue storage.
/// </summary>
public class HookEvent
{
    /// <summary>
    /// Type of the hook event.
    /// </summary>
    [JsonPropertyName("event")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HookEventType EventType { get; set; }

    /// <summary>
    /// Claude Code session ID.
    /// </summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Working directory where Claude Code is running.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    /// <summary>
    /// Path to transcript file (for session events).
    /// </summary>
    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    /// <summary>
    /// File path that was modified (for FileChanged events).
    /// </summary>
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    /// <summary>
    /// Tool name (for FileChanged events).
    /// </summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a HookEvent from raw hook data for SessionStart events.
    /// </summary>
    public static HookEvent CreateSessionStart(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.SessionStart,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            TranscriptPath = data.TranscriptPath,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for FileChanged events.
    /// </summary>
    public static HookEvent CreateFileChanged(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.FileChanged,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            FilePath = data.GetFilePath(),
            ToolName = data.ToolName,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for SessionStop events.
    /// </summary>
    public static HookEvent CreateSessionStop(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.SessionStop,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            TranscriptPath = data.TranscriptPath,
            Timestamp = DateTime.UtcNow
        };
    }
}
