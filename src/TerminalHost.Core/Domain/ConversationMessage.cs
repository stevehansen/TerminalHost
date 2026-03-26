using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Type of conversation message.
/// </summary>
public enum MessageType
{
    UserMessage,
    AssistantText,
    Thinking,
    ToolCall,
    ToolResult,
    SystemMessage
}

/// <summary>
/// Represents a message in the Claude Code conversation transcript.
/// </summary>
public class ConversationMessage
{
    /// <summary>
    /// UUID from JSONL (deduplication key).
    /// </summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    /// <summary>
    /// Parent session ID.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Producing/receiving agent ID.
    /// </summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>
    /// Message type.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageType Type { get; set; }

    /// <summary>
    /// Role: "user", "assistant", "system".
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    /// <summary>
    /// Text content (may be truncated for large tool results).
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// For tool_call/tool_result messages, the tool use ID.
    /// </summary>
    [JsonPropertyName("toolUseId")]
    public string? ToolUseId { get; set; }

    /// <summary>
    /// For tool_call messages, the tool name.
    /// </summary>
    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    /// <summary>
    /// Timestamp from JSONL.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Estimated token count for this message.
    /// </summary>
    [JsonPropertyName("estimatedTokens")]
    public int EstimatedTokens { get; set; }
}
