using System.Text.Json;
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

    /// <summary>Claude Code session stopped (Stop hook).</summary>
    SessionStop,

    /// <summary>A tool use started (PreToolUse hook).</summary>
    ToolStart,

    /// <summary>A tool use completed (PostToolUse hook for all tools).</summary>
    ToolEnd,

    /// <summary>A tool use failed (PostToolUseFailure hook).</summary>
    ToolError,

    /// <summary>A subagent was spawned (SubagentStart hook).</summary>
    SubagentStart,

    /// <summary>A subagent completed (SubagentStop hook).</summary>
    SubagentStop,

    /// <summary>A notification was sent (Notification hook, e.g., permission prompt).</summary>
    Notification,

    /// <summary>Claude Code session ended (SessionEnd hook — may fire instead of/in addition to Stop).</summary>
    SessionEnd
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
    /// Tool name (for FileChanged, ToolStart, and ToolEnd events).
    /// </summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    /// <summary>
    /// Unique identifier for the tool use (for ToolStart/ToolEnd correlation).
    /// </summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // --- Subagent fields ---

    /// <summary>
    /// Subagent ID (for SubagentStart/SubagentStop events).
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; set; }

    /// <summary>
    /// Subagent type (for SubagentStart events).
    /// </summary>
    [JsonPropertyName("agent_type")]
    public string? AgentType { get; set; }

    /// <summary>
    /// Path to subagent transcript file.
    /// </summary>
    [JsonPropertyName("agent_transcript_path")]
    public string? AgentTranscriptPath { get; set; }

    // --- Notification fields ---

    /// <summary>
    /// Type of notification (e.g., "permission_prompt").
    /// </summary>
    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    /// <summary>
    /// Notification message text.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Original parsed hook data (for rich tool input/output extraction).
    /// Not serialized — only available within the same process.
    /// </summary>
    [JsonIgnore]
    public HookEventData? RawData { get; set; }

    /// <summary>
    /// Human-readable summary of tool input (file path, command, pattern, etc.).
    /// Computed at creation time so it survives JSON serialization over IPC.
    /// </summary>
    [JsonPropertyName("input_summary")]
    public string? InputSummary { get; set; }

    /// <summary>
    /// Human-readable summary of tool result (for ToolEnd events).
    /// </summary>
    [JsonPropertyName("result_summary")]
    public string? ResultSummary { get; set; }

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

    /// <summary>
    /// Creates a HookEvent from raw hook data for ToolStart events (PreToolUse).
    /// </summary>
    public static HookEvent CreateToolStart(HookEventData data)
    {
        var (inputSummary, filePath) = ExtractToolSummary(data);
        return new HookEvent
        {
            EventType = HookEventType.ToolStart,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            ToolName = data.ToolName,
            ToolUseId = data.ToolUseId,
            AgentId = data.AgentId,
            FilePath = filePath ?? data.GetFilePath(),
            InputSummary = inputSummary,
            RawData = data,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for ToolEnd events (PostToolUse).
    /// </summary>
    public static HookEvent CreateToolEnd(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.ToolEnd,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            ToolName = data.ToolName,
            ToolUseId = data.ToolUseId,
            AgentId = data.AgentId,
            FilePath = data.GetFilePath(),
            ResultSummary = ExtractResultSummary(data),
            RawData = data,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for ToolError events (PostToolUseFailure).
    /// </summary>
    public static HookEvent CreateToolError(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.ToolError,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            ToolName = data.ToolName,
            ToolUseId = data.ToolUseId,
            AgentId = data.AgentId,
            FilePath = data.GetFilePath(),
            ResultSummary = ExtractResultSummary(data),
            RawData = data,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Extracts a human-readable summary from tool input data.
    /// Returns (inputSummary, filePath).
    /// </summary>
    private static (string? Summary, string? FilePath) ExtractToolSummary(HookEventData data)
    {
        if (data.ToolInput == null || data.ToolInput.Value.ValueKind != JsonValueKind.Object)
            return (null, null);

        var input = data.ToolInput.Value;
        var filePath = TryGetString(input, "file_path");
        var command = TryGetString(input, "command");
        var description = TryGetString(input, "description")
            ?? TryGetString(input, "prompt")
            ?? TryGetString(input, "query")
            ?? TryGetString(input, "pattern")
            ?? TryGetString(input, "url");

        // Make file paths relative to workspace for readability
        var displayPath = MakeRelative(filePath, data.Cwd);
        var summary = ToolCall.SummarizeInput(data.ToolName ?? "", displayPath, command, description);
        return (summary, filePath);
    }

    /// <summary>
    /// Extracts a human-readable summary from tool response data.
    /// </summary>
    private static string? ExtractResultSummary(HookEventData data)
    {
        if (data.ToolResponse == null) return null;
        var text = data.ToolResponse.Value.ValueKind == JsonValueKind.String
            ? data.ToolResponse.Value.GetString()
            : data.ToolResponse.Value.GetRawText();
        return text != null && text.Length > 150 ? text[..150] + "..." : text;
    }

    private static string? TryGetString(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
    }

    /// <summary>Strips workspace directory prefix from file paths for readability.</summary>
    private static string? MakeRelative(string? path, string? cwd)
    {
        if (path == null || string.IsNullOrEmpty(cwd)) return path;
        var cwdNorm = cwd.Replace('/', '\\').TrimEnd('\\') + '\\';
        if (path.StartsWith(cwdNorm, StringComparison.OrdinalIgnoreCase))
            return path[cwdNorm.Length..];
        // Also try forward slashes
        var cwdFwd = cwd.Replace('\\', '/').TrimEnd('/') + '/';
        if (path.StartsWith(cwdFwd, StringComparison.OrdinalIgnoreCase))
            return path[cwdFwd.Length..];
        return path;
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for SubagentStart events.
    /// </summary>
    public static HookEvent CreateSubagentStart(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.SubagentStart,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            AgentId = data.AgentId,
            AgentType = data.AgentType,
            AgentTranscriptPath = data.AgentTranscriptPath,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for SubagentStop events.
    /// </summary>
    public static HookEvent CreateSubagentStop(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.SubagentStop,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            AgentId = data.AgentId,
            AgentTranscriptPath = data.AgentTranscriptPath,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for Notification events.
    /// </summary>
    public static HookEvent CreateNotification(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.Notification,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            NotificationType = data.NotificationType,
            Message = data.Message,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a HookEvent from raw hook data for SessionEnd events.
    /// </summary>
    public static HookEvent CreateSessionEnd(HookEventData data)
    {
        return new HookEvent
        {
            EventType = HookEventType.SessionEnd,
            SessionId = data.SessionId,
            Cwd = data.Cwd,
            TranscriptPath = data.TranscriptPath,
            Timestamp = DateTime.UtcNow
        };
    }
}
