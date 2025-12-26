using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Data received from Claude Code hooks via stdin JSON.
/// This represents the raw data from hook events like SessionStart, PostToolUse, and Stop.
/// </summary>
public class HookEventData
{
    /// <summary>
    /// Claude Code's unique session identifier.
    /// Persists across --continue invocations.
    /// </summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Current working directory where Claude Code is running.
    /// Used to match sessions to Intents via worktree paths.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    /// <summary>
    /// Path to the transcript JSONL file containing full conversation history.
    /// Can be parsed for commands executed and agent notes.
    /// </summary>
    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    /// <summary>
    /// The permission mode Claude Code is running in.
    /// </summary>
    [JsonPropertyName("permission_mode")]
    public string? PermissionMode { get; set; }

    /// <summary>
    /// Name of the hook event (e.g., "SessionStart", "PreToolUse", "PostToolUse", "Stop").
    /// </summary>
    [JsonPropertyName("hook_event_name")]
    public string? HookEventName { get; set; }

    /// <summary>
    /// Name of the tool being used (for PreToolUse/PostToolUse hooks).
    /// Examples: "Write", "Edit", "MultiEdit", "Bash", "Read".
    /// </summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    /// <summary>
    /// Tool input data (for PreToolUse/PostToolUse hooks).
    /// Structure depends on tool_name.
    /// </summary>
    [JsonPropertyName("tool_input")]
    public JsonElement? ToolInput { get; set; }

    /// <summary>
    /// Tool response data (for PostToolUse hooks).
    /// </summary>
    [JsonPropertyName("tool_response")]
    public JsonElement? ToolResponse { get; set; }

    /// <summary>
    /// Unique identifier for this tool use.
    /// </summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    /// <summary>
    /// Whether a stop hook is already active (to prevent infinite loops).
    /// </summary>
    [JsonPropertyName("stop_hook_active")]
    public bool? StopHookActive { get; set; }

    /// <summary>
    /// Hook source identifier.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    // Helper methods to extract common data

    /// <summary>
    /// Extracts the file_path from tool_input for Write/Edit/MultiEdit tools.
    /// </summary>
    public string? GetFilePath()
    {
        if (ToolInput == null || ToolInput.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (ToolInput.Value.TryGetProperty("file_path", out var filePath))
            return filePath.GetString();

        return null;
    }

    /// <summary>
    /// Extracts the command from tool_input for Bash tool.
    /// </summary>
    public string? GetBashCommand()
    {
        if (ToolInput == null || ToolInput.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (ToolInput.Value.TryGetProperty("command", out var command))
            return command.GetString();

        return null;
    }

    /// <summary>
    /// Checks if this is a file modification tool (Write, Edit, MultiEdit).
    /// </summary>
    public bool IsFileModificationTool()
    {
        return ToolName is "Write" or "Edit" or "MultiEdit";
    }

    /// <summary>
    /// Parses hook event data from JSON string (stdin content).
    /// </summary>
    public static HookEventData? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<HookEventData>(json);
        }
        catch
        {
            return null;
        }
    }
}
