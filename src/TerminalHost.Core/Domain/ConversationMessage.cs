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

    /// <summary>
    /// Real per-turn usage breakdown (from message.usage in the JSONL / SDK response).
    /// Null when only heuristic estimates are available (e.g. user messages, legacy data).
    /// </summary>
    [JsonPropertyName("usage")]
    public UsageBreakdown? Usage { get; set; }
}

/// <summary>
/// Per-turn token usage, mirroring Anthropic's message.usage object.
/// <para>
/// <see cref="TotalContextTokens"/> represents the real context window fill on the
/// producing turn (input + cache_read + cache_creation). This is the quantity the
/// Spark Canvas uses to render the agent's context usage ring — unlike
/// <see cref="TotalTokens"/>, it excludes output tokens (which leave the window
/// once emitted) and unlike raw input_tokens it accounts for cache re-use.
/// </para>
/// </summary>
public class UsageBreakdown
{
    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("cacheReadInputTokens")]
    public int CacheReadInputTokens { get; set; }

    [JsonPropertyName("cacheCreationInputTokens")]
    public int CacheCreationInputTokens { get; set; }

    /// <summary>
    /// True if this usage was synthesized from a heuristic token estimate
    /// (e.g. the UTF-8 length / 4 fallback) rather than read from a real
    /// message.usage payload. Consumers should treat heuristic data as a
    /// lower bound and must NOT use it to set latestContextTokens.
    /// </summary>
    [JsonPropertyName("fromHeuristic")]
    public bool FromHeuristic { get; set; }

    /// <summary>
    /// Real context window fill on the producing turn:
    /// input + cache_read + cache_creation. Excludes output because
    /// emitted output tokens are not part of the context window.
    /// </summary>
    [JsonIgnore]
    public int TotalContextTokens => InputTokens + CacheReadInputTokens + CacheCreationInputTokens;

    /// <summary>
    /// All tokens involved in this turn (context + output). Kept for cost-like metrics.
    /// </summary>
    [JsonIgnore]
    public int TotalTokens => InputTokens + OutputTokens + CacheReadInputTokens + CacheCreationInputTokens;
}
