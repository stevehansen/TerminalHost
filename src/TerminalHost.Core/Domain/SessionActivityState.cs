using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Lifecycle state of a session (richer than ClaudeSessionStatus).
/// </summary>
public enum SessionLifecycle
{
    /// <summary>Agent is actively working.</summary>
    Active,

    /// <summary>Agent is executing a tool.</summary>
    ToolCalling,

    /// <summary>Agent is waiting for user permission.</summary>
    WaitingPermission,

    /// <summary>No activity for >N seconds but session hasn't ended.</summary>
    Idle,

    /// <summary>Session ended normally.</summary>
    Completed,

    /// <summary>Session ended with error.</summary>
    Failed,

    /// <summary>No activity for extended period, no Stop hook received.</summary>
    TimedOut,

    /// <summary>User manually closed/abandoned the session.</summary>
    Abandoned
}

/// <summary>
/// Complete in-memory activity state for a single Claude Code session.
/// This is the main aggregate that ties together all Phase 1 data layers:
/// session lifecycle, tool activity, agent hierarchy, conversation messages, and file access.
/// </summary>
public class SessionActivityState
{
    /// <summary>
    /// Claude Code session ID.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Working directory.
    /// </summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Path to the JSONL transcript file.
    /// </summary>
    [JsonPropertyName("transcriptPath")]
    public string? TranscriptPath { get; set; }

    /// <summary>
    /// When the session started.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the session ended.
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Last activity from any source (hooks, JSONL, etc.).
    /// </summary>
    [JsonPropertyName("lastActivityTime")]
    public DateTime? LastActivityTime { get; set; }

    /// <summary>
    /// Current lifecycle state.
    /// </summary>
    [JsonPropertyName("lifecycle")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionLifecycle Lifecycle { get; set; } = SessionLifecycle.Active;

    /// <summary>
    /// First user prompt.
    /// </summary>
    [JsonPropertyName("initialPrompt")]
    public string? InitialPrompt { get; set; }

    /// <summary>
    /// Session summary.
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Git branch at session start.
    /// </summary>
    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    /// <summary>
    /// Origin of the session (Local, Container, DevContainer).
    /// </summary>
    [JsonPropertyName("source")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionSource Source { get; set; } = SessionSource.Local;

    /// <summary>
    /// Container display name (for Container/DevContainer sessions).
    /// </summary>
    [JsonPropertyName("containerName")]
    public string? ContainerName { get; set; }

    /// <summary>
    /// All agents (main + subagents) keyed by agent ID.
    /// </summary>
    [JsonPropertyName("agents")]
    public Dictionary<string, AgentInstance> Agents { get; set; } = [];

    /// <summary>
    /// All tool calls keyed by tool_use_id.
    /// </summary>
    [JsonPropertyName("toolCalls")]
    public Dictionary<string, ToolCall> ToolCalls { get; set; } = [];

    /// <summary>
    /// Conversation transcript messages in order.
    /// </summary>
    [JsonPropertyName("messages")]
    public List<ConversationMessage> Messages { get; set; } = [];

    /// <summary>
    /// File access tracking keyed by file path.
    /// </summary>
    [JsonPropertyName("fileActivities")]
    public Dictionary<string, FileActivity> FileActivities { get; set; } = [];

    /// <summary>
    /// Set of seen message UUIDs for JSONL deduplication.
    /// </summary>
    [JsonIgnore]
    public HashSet<string> SeenMessageIds { get; set; } = [];

    /// <summary>
    /// Set of seen tool_use_ids for hook/JSONL deduplication.
    /// </summary>
    [JsonIgnore]
    public HashSet<string> SeenToolUseIds { get; set; } = [];

    // Derived properties

    /// <summary>
    /// The main (root) agent.
    /// </summary>
    [JsonIgnore]
    public AgentInstance? MainAgent => Agents.Values.FirstOrDefault(a => a.IsMain);

    /// <summary>
    /// Currently running tool calls.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<ToolCall> ActiveToolCalls => ToolCalls.Values.Where(t => t.IsRunning);

    /// <summary>
    /// Total number of tool calls.
    /// </summary>
    [JsonIgnore]
    public int TotalToolCalls => ToolCalls.Count;

    /// <summary>
    /// Total number of agents (main + subagents).
    /// </summary>
    [JsonIgnore]
    public int TotalAgents => Agents.Count;

    /// <summary>
    /// Total estimated tokens across all messages.
    /// </summary>
    [JsonIgnore]
    public int TotalTokensEstimated => Messages.Sum(m => m.EstimatedTokens);

    /// <summary>
    /// Number of unique files read.
    /// </summary>
    [JsonIgnore]
    public int FilesRead => FileActivities.Values.Count(f => f.ReadCount > 0);

    /// <summary>
    /// Number of unique files written.
    /// </summary>
    [JsonIgnore]
    public int FilesWritten => FileActivities.Values.Count(f => f.WriteCount > 0);

    /// <summary>
    /// Session duration.
    /// </summary>
    [JsonIgnore]
    public TimeSpan Duration => (EndTime ?? LastActivityTime ?? DateTime.UtcNow) - StartTime;

    /// <summary>
    /// Whether this session is still active.
    /// </summary>
    [JsonIgnore]
    public bool IsActive => Lifecycle is SessionLifecycle.Active or SessionLifecycle.ToolCalling or SessionLifecycle.WaitingPermission or SessionLifecycle.Idle;

    /// <summary>
    /// Tool calls grouped by category.
    /// </summary>
    [JsonIgnore]
    public ILookup<ToolCategory, ToolCall> ToolCallsByCategory => ToolCalls.Values.ToLookup(t => t.Category);

    /// <summary>
    /// Top files by total access count.
    /// </summary>
    public IEnumerable<FileActivity> GetTopFiles(int count = 10)
    {
        return FileActivities.Values
            .OrderByDescending(f => f.TotalAccesses)
            .Take(count);
    }

    /// <summary>
    /// Records a tool call start.
    /// </summary>
    public ToolCall RecordToolCallStart(string toolUseId, string toolName, string? agentId, string? inputSummary, string? filePath)
    {
        if (!SeenToolUseIds.Add(toolUseId))
        {
            // Already seen — return existing
            return ToolCalls.TryGetValue(toolUseId, out var existing) ? existing : new ToolCall { ToolUseId = toolUseId };
        }

        var toolCall = new ToolCall
        {
            ToolUseId = toolUseId,
            SessionId = SessionId,
            AgentId = agentId,
            ToolName = toolName,
            InputSummary = inputSummary,
            FilePath = filePath,
            State = ToolCallState.Running,
            StartTime = DateTime.UtcNow
        };

        ToolCalls[toolUseId] = toolCall;
        LastActivityTime = DateTime.UtcNow;

        // Update agent state
        if (agentId != null && Agents.TryGetValue(agentId, out var agent))
        {
            agent.State = AgentState.ToolCalling;
            agent.CurrentToolUseId = toolUseId;
            agent.ToolCallCount++;
        }

        return toolCall;
    }

    /// <summary>
    /// Records a tool call completion.
    /// </summary>
    public void RecordToolCallEnd(string toolUseId, string? resultSummary, int tokenCost, string? error, string? filePath)
    {
        LastActivityTime = DateTime.UtcNow;

        if (ToolCalls.TryGetValue(toolUseId, out var toolCall))
        {
            if (!string.IsNullOrEmpty(error))
                toolCall.MarkError(error);
            else
                toolCall.Complete(resultSummary, tokenCost, filePath);
        }

        // Update agent state back to Active
        foreach (var agent in Agents.Values.Where(a => a.CurrentToolUseId == toolUseId))
        {
            agent.State = AgentState.Active;
            agent.CurrentToolUseId = null;
        }
    }

    /// <summary>
    /// Records file access from a tool call.
    /// </summary>
    public void RecordFileAccess(string filePath, string accessType)
    {
        if (!FileActivities.TryGetValue(filePath, out var activity))
        {
            activity = new FileActivity { FilePath = filePath };
            FileActivities[filePath] = activity;
        }
        activity.RecordAccess(accessType);
    }

    /// <summary>
    /// Adds a subagent to this session.
    /// </summary>
    public AgentInstance AddSubagent(string toolUseId, string parentId, string? name, string? task, string? role = null)
    {
        var subagent = AgentInstance.CreateSubagent(toolUseId, SessionId, parentId, name, task, role: role);
        Agents[toolUseId] = subagent;

        if (Agents.TryGetValue(parentId, out var parent))
        {
            parent.ChildAgentIds.Add(toolUseId);
        }

        return subagent;
    }

    /// <summary>
    /// Applies a single ActivityEvent to this state, updating agents, tool calls, messages, and file activities.
    /// Used for both live transcript enrichment and offline JSONL replay.
    /// </summary>
    public void ApplyEvent(ActivityEvent evt)
    {
        switch (evt.Type)
        {
            case ActivityEventType.ToolCallStart:
            {
                var toolUseId = evt.GetString("toolUseId") ?? "";
                var toolName = evt.GetString("toolName") ?? "unknown";
                var inputSummary = evt.GetString("inputSummary");
                RecordToolCallStart(toolUseId, toolName, evt.AgentId, inputSummary, null);
                break;
            }
            case ActivityEventType.ToolCallEnd:
            {
                var toolUseId = evt.GetString("toolUseId") ?? "";
                var resultSummary = evt.GetString("resultSummary");
                var tokenCost = evt.GetInt("tokenCost");
                var error = evt.GetString("error");
                RecordToolCallEnd(toolUseId, resultSummary, tokenCost, error, null);

                // Accumulate token cost into context breakdown
                if (tokenCost > 0)
                {
                    var toolName = evt.GetString("toolName");
                    // Prefer the tool call record's agent attribution over the event's
                    var resolvedAgentId = evt.AgentId ?? SessionId;
                    if (ToolCalls.TryGetValue(toolUseId, out var existingTc) && existingTc.AgentId != null)
                        resolvedAgentId = existingTc.AgentId;
                    // Update the event so downstream consumers (e.g. JS canvas) see the correct agent
                    evt.AgentId = resolvedAgentId;

                    // Subagent completion: attribute tokens to parent agent's SubagentResults
                    if (toolName is "Agent" or "Task" && Agents.ContainsKey(toolUseId))
                    {
                        // Find the parent agent (the one that spawned this subagent)
                        if (Agents.TryGetValue(toolUseId, out var subagent) &&
                            subagent.ParentId != null &&
                            Agents.TryGetValue(subagent.ParentId, out var parentAgent))
                        {
                            parentAgent.Context ??= new ContextBreakdown();
                            parentAgent.Context.SubagentResults += tokenCost;
                            // Mark the subagent so CompleteSubagent doesn't double-roll-up.
                            subagent.RolledUpByToolCallEnd = true;
                        }
                    }
                    else if (Agents.TryGetValue(resolvedAgentId, out var toolAgent))
                    {
                        toolAgent.Context ??= new ContextBreakdown();
                        toolAgent.Context.ToolResults += tokenCost;
                    }
                }
                break;
            }
            case ActivityEventType.AgentSpawn:
            {
                var agentId = evt.GetString("agentId") ?? "";
                var parentId = evt.GetString("parentId");
                var name = evt.GetString("name") ?? "subagent";
                var task = evt.GetString("task");
                var isMain = evt.Data.TryGetValue("isMain", out var isMainObj) && isMainObj is true;
                if (!isMain && !Agents.ContainsKey(agentId))
                    AddSubagent(agentId, parentId ?? SessionId, name, task);
                break;
            }
            case ActivityEventType.AgentComplete:
            {
                var agentId = evt.GetString("agentId") ?? "";
                CompleteSubagent(agentId);
                break;
            }

            case ActivityEventType.AgentDeleted:
            {
                var agentId = evt.GetString("agentId") ?? "";
                if (Agents.TryGetValue(agentId, out var agent))
                {
                    // Remove from parent's child list
                    if (agent.ParentId != null && Agents.TryGetValue(agent.ParentId, out var parent))
                    {
                        parent.ChildAgentIds.Remove(agentId);
                    }
                    Agents.Remove(agentId);
                }
                break;
            }

            case ActivityEventType.AgentMetadataUpdate:
            {
                var agentId = evt.GetString("agentId") ?? "";
                if (Agents.TryGetValue(agentId, out var agent))
                {
                    var role = evt.GetString("role");
                    if (role != null) agent.Role = role;

                    var execMode = evt.GetString("executionMode");
                    if (execMode != null && Enum.TryParse<ExecutionMode>(execMode, true, out var em))
                        agent.ExecutionMode = em;

                    var lifespan = evt.GetString("lifespanType");
                    if (lifespan != null && Enum.TryParse<LifespanType>(lifespan, true, out var lt))
                        agent.LifespanType = lt;

                    if (evt.Data.ContainsKey("retryCount"))
                        agent.RetryCount = evt.GetInt("retryCount");

                    if (evt.Data.ContainsKey("denialCount"))
                        agent.DenialCount = evt.GetInt("denialCount");

                    var milestoneId = evt.GetString("milestoneId");
                    if (milestoneId != null) agent.MilestoneId = milestoneId;

                    if (evt.Data.ContainsKey("completionPercentage"))
                        agent.CompletionPercentage = evt.GetInt("completionPercentage");

                    var retryOf = evt.GetString("retryOfAgentId");
                    if (retryOf != null) agent.RetryOfAgentId = retryOf;

                    if (evt.Data.TryGetValue("blockedByAgentIds", out var blockedObj) && blockedObj is List<string> blockedList)
                        agent.BlockedByAgentIds = blockedList;
                }
                break;
            }

            case ActivityEventType.FileAccessed:
            {
                var filePath = evt.GetString("filePath");
                var accessType = evt.GetString("accessType") ?? "read";
                if (!string.IsNullOrEmpty(filePath))
                    RecordFileAccess(filePath, accessType);
                break;
            }
            case ActivityEventType.UserMessage:
            case ActivityEventType.AssistantMessage:
            case ActivityEventType.ThinkingBlock:
            {
                var content = evt.GetString("content");
                var tokens = evt.GetInt("estimatedTokens");
                var msgType = evt.Type switch
                {
                    ActivityEventType.UserMessage => MessageType.UserMessage,
                    ActivityEventType.AssistantMessage => MessageType.AssistantText,
                    ActivityEventType.ThinkingBlock => MessageType.Thinking,
                    _ => MessageType.SystemMessage
                };

                // Prefer the real per-turn usage when available; otherwise synthesize
                // a heuristic from the existing token estimate (InputTokens slot only).
                var usage = evt.Data.TryGetValue("usage", out var u) ? u as UsageBreakdown : null;
                if (usage == null)
                {
                    usage = new UsageBreakdown
                    {
                        InputTokens = tokens,
                        FromHeuristic = true
                    };
                }

                var msg = new ConversationMessage
                {
                    Uuid = Guid.NewGuid().ToString(),
                    SessionId = SessionId,
                    AgentId = evt.AgentId,
                    Type = msgType,
                    Role = evt.Type == ActivityEventType.UserMessage ? "user" : "assistant",
                    Content = content,
                    Timestamp = evt.Timestamp,
                    EstimatedTokens = tokens,
                    Usage = usage
                };
                Messages.Add(msg);
                // Accumulate tokens into agent context breakdown
                if (tokens > 0)
                {
                    // Attribute to most recently active subagent if event doesn't specify
                    var msgAgentId = evt.AgentId ?? SessionId;
                    if (msgAgentId == SessionId)
                    {
                        // Check for active subagent that should receive these tokens
                        var activeSubagent = Agents.Values
                            .Where(a => !a.IsMain && a.State == AgentState.Active)
                            .OrderByDescending(a => a.SpawnTime)
                            .FirstOrDefault();
                        if (activeSubagent != null)
                            msgAgentId = activeSubagent.Id;
                    }
                    // Update the event so downstream consumers (e.g. JS canvas) see the correct agent
                    evt.AgentId = msgAgentId;
                    if (Agents.TryGetValue(msgAgentId, out var msgAgent))
                    {
                        msgAgent.Context ??= new ContextBreakdown();
                        if (evt.Type == ActivityEventType.ThinkingBlock)
                            msgAgent.Context.Reasoning += tokens;
                        else
                            msgAgent.Context.UserMessages += tokens;

                        // Real usage → snapshot the true context fill and accumulate output tokens.
                        // Assistant turns only (UserMessage has no usage; ThinkingBlock usage
                        // is attached to the same assistant turn as the text block and we don't
                        // want to double-apply it per block).
                        if (evt.Type == ActivityEventType.AssistantMessage && !usage.FromHeuristic)
                        {
                            msgAgent.LatestContextTokens = usage.TotalContextTokens;
                            msgAgent.TotalOutputTokens += usage.OutputTokens;
                        }
                    }
                }
                break;
            }
            case ActivityEventType.ModelDetected:
            {
                var agentId = evt.GetString("agentId") ?? SessionId;
                var model = evt.GetString("model");
                if (model != null && Agents.TryGetValue(agentId, out var agent))
                    agent.Model = model;
                break;
            }
        }
    }

    /// <summary>
    /// Marks a subagent as complete.
    /// </summary>
    public void CompleteSubagent(string agentId)
    {
        if (Agents.TryGetValue(agentId, out var agent))
        {
            agent.State = AgentState.Complete;
            agent.CompleteTime = DateTime.UtcNow;

            // Roll up subagent's total tokens into parent's subagentResults.
            // Fallback for SubagentStop-without-ToolCallEnd (cancelled/orphaned subagents).
            // On the happy path, the Task/Agent ToolCallEnd handler above already attributed
            // tokens and set RolledUpByToolCallEnd; skip here to avoid double-counting.
            if (agent.ParentId != null && agent.Context != null &&
                Agents.TryGetValue(agent.ParentId, out var parent))
            {
                parent.Context ??= new ContextBreakdown();
                if (!agent.RolledUpByToolCallEnd)
                    parent.Context.SubagentResults += agent.Context.Total;
            }
        }
    }

    /// <summary>
    /// Creates a new SessionActivityState for a session.
    /// </summary>
    public static SessionActivityState Create(string sessionId, string? cwd = null, string? transcriptPath = null,
        SessionSource source = SessionSource.Local, string? containerName = null)
    {
        var state = new SessionActivityState
        {
            SessionId = sessionId,
            WorkingDirectory = cwd,
            TranscriptPath = transcriptPath,
            StartTime = DateTime.UtcNow,
            Lifecycle = SessionLifecycle.Active,
            Source = source,
            ContainerName = containerName
        };

        // Always create the main agent
        var mainAgent = AgentInstance.CreateMain(sessionId);
        state.Agents[sessionId] = mainAgent;

        return state;
    }
}
