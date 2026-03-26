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
    public AgentInstance AddSubagent(string toolUseId, string parentId, string? name, string? task)
    {
        var subagent = AgentInstance.CreateSubagent(toolUseId, SessionId, parentId, name, task);
        Agents[toolUseId] = subagent;

        if (Agents.TryGetValue(parentId, out var parent))
        {
            parent.ChildAgentIds.Add(toolUseId);
        }

        return subagent;
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
        }
    }

    /// <summary>
    /// Creates a new SessionActivityState for a session.
    /// </summary>
    public static SessionActivityState Create(string sessionId, string? cwd = null, string? transcriptPath = null)
    {
        var state = new SessionActivityState
        {
            SessionId = sessionId,
            WorkingDirectory = cwd,
            TranscriptPath = transcriptPath,
            StartTime = DateTime.UtcNow,
            Lifecycle = SessionLifecycle.Active
        };

        // Always create the main agent
        var mainAgent = AgentInstance.CreateMain(sessionId);
        state.Agents[sessionId] = mainAgent;

        return state;
    }
}
