using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// State of an agent within a session.
/// </summary>
public enum AgentState
{
    Active,
    Thinking,
    ToolCalling,
    WaitingPermission,
    Idle,
    Complete,
    Error
}

/// <summary>
/// Represents an agent (main or subagent) within a Claude Code session.
/// The main agent is always present; subagents are spawned via Agent/Task tool calls.
/// </summary>
public class AgentInstance
{
    /// <summary>
    /// Unique ID. Main agent uses session ID; subagents use the tool_use_id of the Agent/Task call.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Parent session ID.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Display name (e.g., "main", "Explore", "code-simplifier").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "main";

    /// <summary>
    /// Parent agent ID (null for main agent).
    /// </summary>
    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>
    /// Whether this is the main (root) agent.
    /// </summary>
    [JsonPropertyName("isMain")]
    public bool IsMain { get; set; }

    /// <summary>
    /// Current agent state.
    /// </summary>
    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentState State { get; set; } = AgentState.Active;

    /// <summary>
    /// Task description (for subagents, the prompt given to them).
    /// </summary>
    [JsonPropertyName("task")]
    public string? Task { get; set; }

    /// <summary>
    /// Model name (e.g., "claude-opus-4-6", "claude-sonnet-4-6").
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// When the agent was spawned.
    /// </summary>
    [JsonPropertyName("spawnTime")]
    public DateTime SpawnTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the agent completed.
    /// </summary>
    [JsonPropertyName("completeTime")]
    public DateTime? CompleteTime { get; set; }

    /// <summary>
    /// Number of tool calls made by this agent.
    /// </summary>
    [JsonPropertyName("toolCallCount")]
    public int ToolCallCount { get; set; }

    /// <summary>
    /// Currently executing tool use ID (if any).
    /// </summary>
    [JsonPropertyName("currentToolUseId")]
    public string? CurrentToolUseId { get; set; }

    /// <summary>
    /// IDs of spawned child agents.
    /// </summary>
    [JsonPropertyName("childAgentIds")]
    public List<string> ChildAgentIds { get; set; } = [];

    /// <summary>
    /// Context token breakdown.
    /// </summary>
    [JsonPropertyName("context")]
    public ContextBreakdown? Context { get; set; }

    /// <summary>
    /// Duration of agent activity.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration => CompleteTime.HasValue ? CompleteTime.Value - SpawnTime : null;

    /// <summary>
    /// Whether the agent is still active.
    /// </summary>
    [JsonIgnore]
    public bool IsActive => State is AgentState.Active or AgentState.Thinking or AgentState.ToolCalling or AgentState.WaitingPermission;

    /// <summary>
    /// Creates the main agent for a session.
    /// </summary>
    public static AgentInstance CreateMain(string sessionId)
    {
        return new AgentInstance
        {
            Id = sessionId,
            SessionId = sessionId,
            Name = "main",
            IsMain = true,
            State = AgentState.Active,
            SpawnTime = DateTime.UtcNow,
            Context = new ContextBreakdown()
        };
    }

    /// <summary>
    /// Creates a subagent spawned by a parent agent.
    /// </summary>
    public static AgentInstance CreateSubagent(string toolUseId, string sessionId, string parentId, string? name, string? task)
    {
        return new AgentInstance
        {
            Id = toolUseId,
            SessionId = sessionId,
            Name = name ?? "subagent",
            ParentId = parentId,
            IsMain = false,
            State = AgentState.Active,
            Task = task,
            SpawnTime = DateTime.UtcNow,
            Context = new ContextBreakdown()
        };
    }
}

/// <summary>
/// Breakdown of token usage by category within an agent's context.
/// </summary>
public class ContextBreakdown
{
    [JsonPropertyName("systemPrompt")]
    public int SystemPrompt { get; set; }

    [JsonPropertyName("userMessages")]
    public int UserMessages { get; set; }

    [JsonPropertyName("toolResults")]
    public int ToolResults { get; set; }

    [JsonPropertyName("reasoning")]
    public int Reasoning { get; set; }

    [JsonPropertyName("subagentResults")]
    public int SubagentResults { get; set; }

    [JsonIgnore]
    public int Total => SystemPrompt + UserMessages + ToolResults + Reasoning + SubagentResults;
}

/// <summary>
/// Known model context window sizes.
/// </summary>
public static class ModelContextSizes
{
    public static int GetMaxTokens(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return 200_000;

        var lower = model.ToLowerInvariant();
        if (lower.Contains("opus") || lower.Contains("sonnet"))
            return 1_000_000;
        if (lower.Contains("haiku"))
            return 200_000;

        return 200_000;
    }
}
