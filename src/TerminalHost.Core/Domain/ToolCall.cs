using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// State of a tool call execution.
/// </summary>
public enum ToolCallState
{
    Running,
    Complete,
    Error
}

/// <summary>
/// Category of tool by what it does.
/// </summary>
public enum ToolCategory
{
    FileRead,
    FileWrite,
    Shell,
    Search,
    Subagent,
    Web,
    Other
}

/// <summary>
/// Represents a single tool invocation within a Claude Code session.
/// </summary>
public class ToolCall
{
    /// <summary>
    /// Claude's tool_use_id (e.g., "toolu_abc123").
    /// </summary>
    [JsonPropertyName("toolUseId")]
    public string ToolUseId { get; set; } = "";

    /// <summary>
    /// Parent session ID.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Which agent invoked this tool (main or subagent ID).
    /// </summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>
    /// Tool name (Read, Edit, Write, Bash, Grep, Glob, Agent, etc.).
    /// </summary>
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = "";

    /// <summary>
    /// Current execution state.
    /// </summary>
    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ToolCallState State { get; set; } = ToolCallState.Running;

    /// <summary>
    /// Human-readable summary of the tool input.
    /// </summary>
    [JsonPropertyName("inputSummary")]
    public string? InputSummary { get; set; }

    /// <summary>
    /// Human-readable summary of the tool result.
    /// </summary>
    [JsonPropertyName("resultSummary")]
    public string? ResultSummary { get; set; }

    /// <summary>
    /// Estimated token cost of the result.
    /// </summary>
    [JsonPropertyName("tokenCost")]
    public int TokenCost { get; set; }

    /// <summary>
    /// When the tool call started.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the tool call completed.
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Error message if tool failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// File path affected by this tool (for file operations).
    /// </summary>
    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    /// <summary>
    /// Calculated duration.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

    /// <summary>
    /// Tool category derived from tool name.
    /// </summary>
    [JsonIgnore]
    public ToolCategory Category => CategorizeTool(ToolName);

    /// <summary>
    /// Whether this tool call is still running.
    /// </summary>
    [JsonIgnore]
    public bool IsRunning => State == ToolCallState.Running;

    /// <summary>
    /// Marks the tool call as complete.
    /// </summary>
    public void Complete(string? resultSummary = null, int tokenCost = 0, string? filePath = null)
    {
        State = ToolCallState.Complete;
        EndTime = DateTime.UtcNow;
        ResultSummary = resultSummary;
        TokenCost = tokenCost;
        if (filePath != null)
            FilePath = filePath;
    }

    /// <summary>
    /// Marks the tool call as errored.
    /// </summary>
    public void MarkError(string? errorMessage = null)
    {
        State = ToolCallState.Error;
        EndTime = DateTime.UtcNow;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Categorizes a tool by name.
    /// </summary>
    public static ToolCategory CategorizeTool(string toolName)
    {
        return toolName switch
        {
            "Read" => ToolCategory.FileRead,
            "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => ToolCategory.FileWrite,
            "Bash" => ToolCategory.Shell,
            "Grep" or "Glob" => ToolCategory.Search,
            "Agent" or "Task" => ToolCategory.Subagent,
            "WebSearch" or "WebFetch" => ToolCategory.Web,
            _ => ToolCategory.Other
        };
    }

    /// <summary>
    /// Creates a human-readable input summary based on tool name and input data.
    /// </summary>
    public static string? SummarizeInput(string toolName, string? filePath, string? command, string? description)
    {
        return toolName switch
        {
            "Read" => filePath,
            "Write" => filePath,
            "Edit" or "MultiEdit" => filePath,
            "Bash" => command != null ? Truncate(command, 80) : null,
            "Grep" => description, // pattern + path
            "Glob" => description, // pattern
            "Agent" => description != null ? Truncate(description, 80) : null,
            "Task" => description != null ? Truncate(description, 80) : null,
            "WebSearch" => description, // query
            "WebFetch" => description, // url/domain
            _ => description
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }
}
