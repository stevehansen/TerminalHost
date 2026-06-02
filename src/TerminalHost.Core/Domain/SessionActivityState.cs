using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Lifecycle state of a session (richer than ClaudeSessionStatus).
/// </summary>
public enum SessionLifecycle
{
    /// <summary>Agent is actively working.</summary>
    Active,

    /// <summary>Agent is waiting for user permission.</summary>
    WaitingPermission,

    /// <summary>Session ended normally.</summary>
    Completed,

    /// <summary>Session ended with error.</summary>
    Failed,

    /// <summary>No activity for extended period, no Stop hook received.</summary>
    TimedOut
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
    /// Whether this session is still worth tracking in the live set. Derived from M1 input
    /// timestamps via <see cref="DeriveParentDisplay"/>: a session stays "active" (Working,
    /// WaitingPermission, or Done — between turns the user might resume) until the quiet
    /// window past the Stop hook elapses and the derivation flips to TimedOut. Lifecycle is
    /// no longer consulted; it remains only as terminal-storage metadata.
    /// </summary>
    [JsonIgnore]
    public bool IsActive => DeriveParentDisplay(DateTime.UtcNow) != AgentDisplayState.TimedOut;

    /// <summary>
    /// Tool calls grouped by category.
    /// </summary>
    [JsonIgnore]
    public ILookup<ToolCategory, ToolCall> ToolCallsByCategory => ToolCalls.Values.ToLookup(t => t.Category);

    /// <summary>
    /// Quiet window past a Stop hook before an agent's display flips from Done to TimedOut.
    /// Single source of truth for the 2-minute rule shared by M3/M4 readers.
    /// </summary>
    internal static readonly TimeSpan TimedOutThreshold = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Backstop only: if the title classified as "working" (spinner) but no further title
    /// change has arrived within this window, treat the agent as idle anyway. Normal idle
    /// is detected instantly from the idle-icon title change, so this only catches a
    /// dropped idle-title update — it is not the primary done signal. See
    /// <see cref="DeriveParentDisplay"/>.
    /// </summary>
    internal static readonly TimeSpan TerminalTitleActiveWindow = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Classifies an AI terminal title into working / idle / unknown. Claude Code prefixes
    /// the title with an animated braille spinner (U+2800–U+28FF) while a turn is running
    /// and the static Claude icon (U+2733 '✳') when idle/awaiting input. Returns true for a
    /// spinner prefix, false for the idle icon, and null for anything else (empty, or a
    /// prefix we don't recognize) so unrecognized titles defer to the hook-derived state
    /// rather than forcing a wrong verdict — keeps non-Claude assistants from regressing.
    /// </summary>
    internal static bool? ClassifyTerminalTitleWorking(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var lead = char.ConvertToUtf32(title, 0);
        if (lead >= 0x2800 && lead <= 0x28FF) return true;  // braille spinner → working
        if (lead == 0x2733) return false;                   // ✳ Claude idle icon → done
        return null;                                         // unrecognized → defer to hooks
    }

    /// <summary>
    /// Pure read-time derivation of an agent's display state from M1 input timestamps.
    /// Intentionally ignores legacy fields (AgentState, CompleteTime, Lifecycle) — those
    /// remain only for terminal-storage decisions.
    /// </summary>
    public AgentDisplayState DeriveAgentDisplayState(AgentInstance agent, DateTime now)
    {
        switch (agent.LastEventKind)
        {
            case AgentEventKind.PermissionPrompt:
                return AgentDisplayState.WaitingPermission;
            case AgentEventKind.Stop:
                // Stop hook fires between assistant turns; treat the agent as Done until
                // the quiet window elapses, after which we surface a TimedOut indicator.
                // Exactly-at-threshold counts as Done; only strictly past flips to TimedOut.
                var elapsed = agent.LastStopHookTime is { } t ? now - t : TimeSpan.MaxValue;
                return elapsed > TimedOutThreshold ? AgentDisplayState.TimedOut : AgentDisplayState.Done;
            case AgentEventKind.Activity:
                return AgentDisplayState.Working;
            case AgentEventKind.None:
            default:
                return AgentDisplayState.Done;
        }
    }

    /// <summary>
    /// Parent (session-level) display is the main agent's own derived state. Subagent
    /// states are intentionally not aggregated — a child finishing or stalling never
    /// changes how the parent renders. WaitingPermission is produced directly by the
    /// main's LastEventKind when a permission prompt is pending.
    /// </summary>
    public AgentDisplayState DeriveParentDisplay(DateTime now)
    {
        var main = Agents.Values.FirstOrDefault(a => a.IsMain);
        if (main is null) return AgentDisplayState.Working;

        // A pending permission prompt is decisive — the title shows the idle icon while a
        // prompt is up, so content classification can't see it; the hook must win here.
        if (main.LastEventKind == AgentEventKind.PermissionPrompt)
            return AgentDisplayState.WaitingPermission;

        // Terminal-title authority: Claude's AI terminal shows an animated braille spinner
        // while a turn runs and the static Claude icon when idle, so the title's
        // classification is an instant, reliable Working/Done signal that doesn't depend on
        // the (sometimes-missed) Stop/Activity hooks. Idle is detected the moment the
        // idle-icon title arrives — no staleness wait. Sessions with no recognized title
        // signal (container/remote, non-Claude assistants, or before the first title) fall
        // through to the unchanged hook-derived state — no regression.
        if (main.TerminalTitleWorking is { } working)
        {
            var sinceTitle = main.LastTerminalTitleChangeTime is { } t ? now - t : TimeSpan.MaxValue;
            // Working only while the spinner is still animating. If the last signal said
            // "working" but the title froze past the window (a dropped idle-title update),
            // fall through to the idle aging path rather than sticking on Working.
            if (working && sinceTitle <= TerminalTitleActiveWindow)
                return AgentDisplayState.Working;

            // Idle icon (or a frozen spinner). Keep the Done→TimedOut quiet window so idle
            // sessions still age out of the live set (IsActive flips false only at TimedOut).
            return sinceTitle > TimedOutThreshold ? AgentDisplayState.TimedOut : AgentDisplayState.Done;
        }

        return DeriveAgentDisplayState(main, now);
    }

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
                    // Trust the AgentId set by the event source (transcript parser or hook).
                    // The previous "attribute to most recently spawned active subagent" heuristic
                    // misrouted the parent's assistant-message tokens to whatever Task subagent
                    // was in flight (symptom: subagent rows showed parent's full context usage).
                    // Subagent tokens are still correctly accumulated via the Agent/Task
                    // ToolCallEnd handler above, which rolls them into the parent's
                    // Context.SubagentResults — message-stream attribution to subagents is not
                    // a requirement today (transcript parser doesn't yet read parent_tool_use_id
                    // from per-subagent JSONL lines).
                    var msgAgentId = evt.AgentId ?? SessionId;
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
    /// Returns a snapshot whose internal dictionaries/lists are freshly cloned so the
    /// caller can enumerate them without risking "Collection was modified" if another
    /// thread mutates the live state concurrently. Element references (AgentInstance,
    /// ToolCall, etc.) are shared — readers only inspect scalar fields, which is
    /// acceptable for display use. Callers that need a fully immutable view must
    /// deep-clone themselves.
    /// </summary>
    public SessionActivityState Snapshot()
    {
        return new SessionActivityState
        {
            SessionId = SessionId,
            WorkingDirectory = WorkingDirectory,
            TranscriptPath = TranscriptPath,
            StartTime = StartTime,
            EndTime = EndTime,
            LastActivityTime = LastActivityTime,
            Lifecycle = Lifecycle,
            InitialPrompt = InitialPrompt,
            Summary = Summary,
            GitBranch = GitBranch,
            Source = Source,
            ContainerName = ContainerName,
            Agents = new Dictionary<string, AgentInstance>(Agents),
            ToolCalls = new Dictionary<string, ToolCall>(ToolCalls),
            Messages = new List<ConversationMessage>(Messages),
            FileActivities = new Dictionary<string, FileActivity>(FileActivities),
            SeenMessageIds = SeenMessageIds,
            SeenToolUseIds = SeenToolUseIds
        };
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
