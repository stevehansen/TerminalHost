using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

public enum ExecutionMode
{
    Foreground,
    Background
}

public enum LifespanType
{
    ShortLived,
    LongLived
}

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
/// Kind of the most recent event observed for an agent. Used by the M2 display-state
/// derivation to disambiguate "agent is working" vs "agent has stopped" vs "agent is
/// waiting on a permission prompt" when only timestamps are available.
/// </summary>
public enum AgentEventKind
{
    None,
    Activity,
    Stop,
    PermissionPrompt
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
    /// Tokens occupying the context window on the most recent assistant turn
    /// (input + cache_read + cache_creation). Set from real message.usage;
    /// zero when only heuristic data is available.
    /// </summary>
    [JsonPropertyName("latestContextTokens")]
    public int LatestContextTokens { get; set; }

    /// <summary>
    /// Cumulative output tokens produced by this agent (cost-like metric).
    /// </summary>
    [JsonPropertyName("totalOutputTokens")]
    public int TotalOutputTokens { get; set; }

    /// <summary>
    /// Transient flag: set to true when a Task/Agent ToolCallEnd attributed this
    /// subagent's tokens to its parent's SubagentResults bucket. The
    /// CompleteSubagent fallback rollup checks this to avoid double-counting.
    /// Not persisted.
    /// </summary>
    [JsonIgnore]
    public bool RolledUpByToolCallEnd { get; set; }

    /// <summary>Semantic role: Explore, Plan, Reviewer, GeneralPurpose, Bash, etc.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Execution mode: Foreground (interactive) or Background (fire-and-forget).</summary>
    [JsonPropertyName("executionMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExecutionMode? ExecutionMode { get; set; }

    /// <summary>Lifecycle type: ShortLived (auto-cleanup) or LongLived (persists until milestone).</summary>
    [JsonPropertyName("lifespanType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LifespanType? LifespanType { get; set; }

    /// <summary>Number of retry attempts for this agent.</summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    /// <summary>Number of times permission was denied.</summary>
    [JsonPropertyName("denialCount")]
    public int DenialCount { get; set; }

    /// <summary>Milestone or task group this agent belongs to.</summary>
    [JsonPropertyName("milestoneId")]
    public string? MilestoneId { get; set; }

    /// <summary>IDs of agents that block this agent.</summary>
    [JsonPropertyName("blockedByAgentIds")]
    public List<string> BlockedByAgentIds { get; set; } = [];

    /// <summary>If this agent is a retry, the ID of the previous attempt.</summary>
    [JsonPropertyName("retryOfAgentId")]
    public string? RetryOfAgentId { get; set; }

    /// <summary>Percentage of work completed (0-100) for partial completion tracking.</summary>
    [JsonPropertyName("completionPercentage")]
    public int? CompletionPercentage { get; set; }

    // New per-agent input timestamps for the derived display state model (M1).
    // All transient — not persisted; recomputed from event flow after restart.
    [JsonIgnore] public DateTime? LastStopHookTime { get; set; }
    [JsonIgnore] public DateTime? LastSubagentStopTime { get; set; }
    [JsonIgnore] public DateTime? LastPermissionPromptTime { get; set; }
    [JsonIgnore] public DateTime? LastActivityEventTime { get; set; }

    // Terminal-title signal for the main agent. Claude Code prefixes the AI terminal's
    // title with an animated braille spinner while a turn is in progress and the static
    // Claude icon (✳) when idle/awaiting input — an authoritative "working vs done" signal
    // independent of the hook stream, whose Stop/Activity events are sometimes missed (the
    // failure that left the main row stuck rendering Working or Done). Only ever set for the
    // main agent; subagents have no terminal of their own. Transient: rebuilt from the live
    // terminal after restart. See SessionActivityState.ClassifyTerminalTitleWorking /
    // DeriveParentDisplay.

    /// <summary>Classification of the most recent title: true = spinner (working),
    /// false = idle icon (done); null = no recognized title signal yet.</summary>
    [JsonIgnore] public bool? TerminalTitleWorking { get; set; }

    /// <summary>When the most recent recognized title change was observed.</summary>
    [JsonIgnore] public DateTime? LastTerminalTitleChangeTime { get; set; }

    /// <summary>
    /// The kind of the most recent event observed for this agent. Derived at read time
    /// from the three event-type timestamps (Activity/Stop/PermissionPrompt) so a late
    /// Activity event with an *older* timestamp than the most recent Stop can never
    /// regress the kind back to Activity (which previously left subagent rows stuck
    /// rendering "Working" after their Task/Agent ToolEnd Stamped them). Tie-break order
    /// when timestamps are equal: PermissionPrompt > Stop > Activity (the more decisive
    /// signal wins).
    /// </summary>
    [JsonIgnore]
    public AgentEventKind LastEventKind
    {
        get
        {
            var a = LastActivityEventTime;
            var s = LastStopHookTime;
            var p = LastPermissionPromptTime;
            if (a is null && s is null && p is null) return AgentEventKind.None;
            // Permission wins ties, then Stop, then Activity. Use DateTime.MinValue as a
            // floor for missing timestamps so `>=` works without nullable-comparison traps
            // (a `DateTime >= (DateTime?)null` is always false in C#).
            var av = a ?? DateTime.MinValue;
            var sv = s ?? DateTime.MinValue;
            var pv = p ?? DateTime.MinValue;
            if (p is not null && pv >= sv && pv >= av) return AgentEventKind.PermissionPrompt;
            if (s is not null && sv >= av) return AgentEventKind.Stop;
            return AgentEventKind.Activity;
        }
    }

    // Stamp helpers: advance-only writes that preserve the Max(...) invariant the M2
    // derivation depends on. Hook events can arrive out of order (IPC + JSONL races),
    // so we reject any timestamp older than what we already recorded for that field.
    // LastEventKind is derived from these timestamps and is never written here — see
    // the property above for why writing it caused subagent rows to get stuck "Working".

    internal void StampActivity(DateTime t)
    {
        if (LastActivityEventTime is null || t > LastActivityEventTime)
            LastActivityEventTime = t;
    }

    internal void StampStop(DateTime t)
    {
        if (LastStopHookTime is null || t > LastStopHookTime)
            LastStopHookTime = t;
    }

    internal void StampPermissionPrompt(DateTime t)
    {
        if (LastPermissionPromptTime is null || t > LastPermissionPromptTime)
            LastPermissionPromptTime = t;
    }

    internal void StampSubagentStop(DateTime t)
    {
        // Informational only on the parent — does NOT participate in LastEventKind, because
        // a child finishing is not the parent itself producing activity.
        if (LastSubagentStopTime is null || t > LastSubagentStopTime)
        {
            LastSubagentStopTime = t;
        }
    }

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
    public static AgentInstance CreateSubagent(string toolUseId, string sessionId, string parentId, string? name, string? task,
        string? role = null, ExecutionMode? executionMode = null, LifespanType? lifespanType = null)
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
            Context = new ContextBreakdown(),
            Role = role,
            ExecutionMode = executionMode,
            LifespanType = lifespanType,
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

        // The "[1m]" suffix (e.g. "claude-opus-4-7[1m]") marks the 1M-context beta.
        // Without it, opus/sonnet/haiku all use the standard 200K window.
        if (model.Contains("[1m]", StringComparison.OrdinalIgnoreCase))
            return 1_000_000;

        return 200_000;
    }
}
