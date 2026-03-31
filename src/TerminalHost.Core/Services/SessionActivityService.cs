using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Maintains in-memory SessionActivityState per active Claude Code session.
/// Processes hook events and transcript data into rich activity tracking.
/// </summary>
public class SessionActivityService : ISessionActivityService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionActivityState> _states = [];
    private readonly TranscriptParserService _transcriptParser = new();

    public event EventHandler<ActivityEvent>? ActivityEventProcessed;
    public event EventHandler<(string SessionId, SessionLifecycle NewState)>? LifecycleChanged;

    public SessionActivityState? GetState(string sessionId)
    {
        lock (_lock)
        {
            return _states.GetValueOrDefault(sessionId);
        }
    }

    public IReadOnlyList<SessionActivityState> GetActiveStates()
    {
        lock (_lock)
        {
            return _states.Values.Where(s => s.IsActive).ToList();
        }
    }

    public IReadOnlyList<SessionActivityState> GetAllStates()
    {
        lock (_lock)
        {
            return _states.Values.ToList();
        }
    }

    public SessionActivityState GetOrCreateState(string sessionId, string? cwd = null, string? transcriptPath = null,
        SessionSource source = SessionSource.Local, string? containerName = null)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(sessionId, out var existing))
            {
                // Update paths if provided
                if (cwd != null) existing.WorkingDirectory = cwd;
                if (transcriptPath != null) existing.TranscriptPath = transcriptPath;
                if (source != SessionSource.Local) existing.Source = source;
                if (containerName != null) existing.ContainerName = containerName;
                return existing;
            }

            var state = SessionActivityState.Create(sessionId, cwd, transcriptPath, source, containerName);
            _states[sessionId] = state;
            return state;
        }
    }

    public void RemoveState(string sessionId)
    {
        lock (_lock)
        {
            _states.Remove(sessionId);
        }
    }

    public void ProcessHookEvent(HookEvent hookEvent, HookEventData? rawData = null)
    {
        // Prefer RawData stored on the event itself (from API/IPC path)
        rawData ??= hookEvent.RawData;

        var events = new List<ActivityEvent>();

        lock (_lock)
        {
            switch (hookEvent.EventType)
            {
                case HookEventType.SessionStart:
                    events.AddRange(ProcessSessionStart(hookEvent));
                    break;

                case HookEventType.ToolStart:
                    events.AddRange(ProcessToolStart(hookEvent, rawData));
                    break;

                case HookEventType.ToolEnd:
                    events.AddRange(ProcessToolEnd(hookEvent, rawData));
                    break;

                case HookEventType.ToolError:
                    events.AddRange(ProcessToolError(hookEvent, rawData));
                    break;

                case HookEventType.SessionStop:
                case HookEventType.SessionEnd:
                    events.AddRange(ProcessSessionStop(hookEvent));
                    break;

                case HookEventType.SubagentStart:
                    events.AddRange(ProcessSubagentStart(hookEvent));
                    break;

                case HookEventType.SubagentStop:
                    events.AddRange(ProcessSubagentStop(hookEvent));
                    break;

                case HookEventType.Notification:
                    events.AddRange(ProcessNotification(hookEvent));
                    break;

                case HookEventType.FileChanged:
                    events.AddRange(ProcessFileChanged(hookEvent));
                    break;
            }
        }

        // Fire events outside lock
        foreach (var evt in events)
        {
            ActivityEventProcessed?.Invoke(this, evt);
        }
    }

    public void ProcessTranscriptEvents(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null)
    {
        if (events.Count == 0 && summary == null && model == null)
            return;

        lock (_lock)
        {
            if (!_states.TryGetValue(sessionId, out var state))
                return;

            foreach (var evt in events)
            {
                ApplyEventToState(state, evt);
            }

            if (summary != null)
                state.Summary = summary;

            if (model != null && state.MainAgent != null)
                state.MainAgent.Model = model;
        }

        // Fire events outside lock
        foreach (var evt in events)
        {
            ActivityEventProcessed?.Invoke(this, evt);
        }
    }

    public async Task EnrichFromTranscriptAsync(string sessionId)
    {
        string? transcriptPath;
        HashSet<string>? seenMessageIds;
        HashSet<string>? seenToolUseIds;

        lock (_lock)
        {
            if (!_states.TryGetValue(sessionId, out var state))
                return;
            if (string.IsNullOrEmpty(state.TranscriptPath))
                return;

            transcriptPath = state.TranscriptPath;
            seenMessageIds = state.SeenMessageIds;
            seenToolUseIds = state.SeenToolUseIds;
        }

        var result = await _transcriptParser.ParseTranscriptRichAsync(
            transcriptPath, sessionId, seenMessageIds, seenToolUseIds);

        if (!result.ParsedSuccessfully)
            return;

        var lifecycleEvents = new List<(string SessionId, SessionLifecycle NewState)>();

        lock (_lock)
        {
            if (!_states.TryGetValue(sessionId, out var state))
                return;

            // Apply events to state
            foreach (var evt in result.Events)
            {
                ApplyEventToState(state, evt);
            }

            // Update summary and model
            if (result.Summary != null)
                state.Summary = result.Summary;

            if (result.Model != null && state.MainAgent != null)
                state.MainAgent.Model = result.Model;
        }

        // Fire events outside lock
        foreach (var evt in result.Events)
        {
            ActivityEventProcessed?.Invoke(this, evt);
        }
    }

    public (int Total, int FileReads, int FileWrites, int ShellCommands, int Subagents) GetToolCallStats(string sessionId)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(sessionId, out var state))
                return (0, 0, 0, 0, 0);

            var toolCalls = state.ToolCalls.Values;
            return (
                toolCalls.Count,
                toolCalls.Count(t => t.Category == ToolCategory.FileRead),
                toolCalls.Count(t => t.Category == ToolCategory.FileWrite),
                toolCalls.Count(t => t.Category == ToolCategory.Shell),
                toolCalls.Count(t => t.Category == ToolCategory.Subagent)
            );
        }
    }

    public IReadOnlyList<FileActivity> GetTopFiles(string sessionId, int count = 10)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(sessionId, out var state))
                return [];

            return state.GetTopFiles(count).ToList();
        }
    }

    // Private processing methods (called under lock)

    private List<ActivityEvent> ProcessSessionStart(HookEvent hookEvent)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, hookEvent.Cwd, hookEvent.TranscriptPath,
            hookEvent.Source, hookEvent.ContainerName);

        var evt = ActivityEvent.CreateSessionStart(hookEvent.SessionId, hookEvent.Cwd, hookEvent.TranscriptPath);
        evt.Timestamp = hookEvent.Timestamp;
        events.Add(evt);

        // Spawn main agent event
        events.Add(ActivityEvent.CreateAgentSpawn(
            hookEvent.SessionId, hookEvent.SessionId, null, "main", true, null, null));

        return events;
    }

    private List<ActivityEvent> ProcessToolStart(HookEvent hookEvent, HookEventData? rawData)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, hookEvent.Cwd,
            source: hookEvent.Source, containerName: hookEvent.ContainerName);

        var toolUseId = hookEvent.ToolUseId ?? Guid.NewGuid().ToString();
        var toolName = hookEvent.ToolName ?? "unknown";

        // Build input summary — prefer pre-computed (survives IPC), fallback to raw data
        string? inputSummary = hookEvent.InputSummary;
        string? filePath = hookEvent.FilePath;
        string? description = null;

        if (inputSummary == null && rawData?.ToolInput != null && rawData.ToolInput.Value.ValueKind == JsonValueKind.Object)
        {
            var input = rawData.ToolInput.Value;
            filePath ??= TryGetJsonString(input, "file_path");
            var command = TryGetJsonString(input, "command");
            description = TryGetJsonString(input, "description")
                ?? TryGetJsonString(input, "prompt")
                ?? TryGetJsonString(input, "query")
                ?? TryGetJsonString(input, "pattern")
                ?? TryGetJsonString(input, "url");

            inputSummary = ToolCall.SummarizeInput(toolName, filePath, command, description);
        }

        // Determine which agent is making this call.
        // Prefer explicit agent_id from the hook payload (Claude Code includes it on tool hooks).
        // Fall back to heuristic if not present (older Claude Code versions).
        var agentId = !string.IsNullOrEmpty(hookEvent.AgentId) && state.Agents.ContainsKey(hookEvent.AgentId)
            ? hookEvent.AgentId
            : ResolveToolCallAgent(state, toolName);

        // Don't spawn subagent here for Agent/Task tools — the subagent-start hook
        // handles that with the correct agent_id. Creating one here with toolUseId as ID
        // causes duplicate nodes on the canvas.

        // Record tool call
        state.RecordToolCallStart(toolUseId, toolName, agentId, inputSummary, filePath);

        events.Add(ActivityEvent.CreateToolCallStart(
            hookEvent.SessionId, agentId, toolUseId, toolName, inputSummary));

        // Track file access
        if (!string.IsNullOrEmpty(filePath))
        {
            var accessType = ToolCall.CategorizeTool(toolName) == ToolCategory.FileWrite ? "write"
                : ToolCall.CategorizeTool(toolName) == ToolCategory.Search ? "search"
                : "read";
            state.RecordFileAccess(filePath, accessType);
            events.Add(ActivityEvent.CreateFileAccessed(
                hookEvent.SessionId, agentId, filePath, accessType, toolUseId));
        }

        return events;
    }

    private List<ActivityEvent> ProcessToolEnd(HookEvent hookEvent, HookEventData? rawData)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, hookEvent.Cwd,
            source: hookEvent.Source, containerName: hookEvent.ContainerName);

        var toolUseId = hookEvent.ToolUseId ?? "";
        var toolName = hookEvent.ToolName ?? "unknown";

        // Extract result summary and token cost from raw data
        string? resultSummary = null;
        int tokenCost = 0;
        string? error = null;

        if (rawData?.ToolResponse != null)
        {
            var responseText = rawData.ToolResponse.Value.ValueKind == JsonValueKind.String
                ? rawData.ToolResponse.Value.GetString()
                : rawData.ToolResponse.Value.GetRawText();

            tokenCost = TokenEstimator.EstimateToolResult(toolName, responseText);
            resultSummary = responseText != null && responseText.Length > 100
                ? responseText[..100] + "..."
                : responseText;
        }

        var filePath = hookEvent.FilePath;

        // Resolve agent: explicit from hook > original tool call record > main agent
        var agentId = !string.IsNullOrEmpty(hookEvent.AgentId) ? hookEvent.AgentId
            : state.ToolCalls.TryGetValue(toolUseId, out var existingCall) ? existingCall.AgentId
            : (state.MainAgent?.Id ?? hookEvent.SessionId);

        state.RecordToolCallEnd(toolUseId, resultSummary, tokenCost, error, filePath);
        state.LastActivityTime = DateTime.UtcNow;

        events.Add(ActivityEvent.CreateToolCallEnd(
            hookEvent.SessionId, agentId, toolUseId, toolName, resultSummary, tokenCost, error));

        // Accumulate token cost into agent context breakdown
        if (tokenCost > 0)
        {
            // Subagent completion: attribute tokens to parent agent's SubagentResults
            if (toolName is "Agent" or "Task" && state.Agents.ContainsKey(toolUseId))
            {
                if (state.Agents.TryGetValue(toolUseId, out var subagent) &&
                    subagent.ParentId != null &&
                    state.Agents.TryGetValue(subagent.ParentId, out var parentAgent))
                {
                    parentAgent.Context ??= new ContextBreakdown();
                    parentAgent.Context.SubagentResults += tokenCost;
                }
            }
            else if (state.Agents.TryGetValue(agentId, out var toolAgent))
            {
                toolAgent.Context ??= new ContextBreakdown();
                toolAgent.Context.ToolResults += tokenCost;
            }
        }

        // Track file write for file modification tools
        if (!string.IsNullOrEmpty(filePath) && (rawData?.IsFileModificationTool() ?? false))
        {
            state.RecordFileAccess(filePath, "write");
            events.Add(ActivityEvent.CreateFileAccessed(
                hookEvent.SessionId, agentId, filePath, "write", toolUseId));
        }

        // Detect subagent completion
        if (toolName is "Agent" or "Task" && state.Agents.ContainsKey(toolUseId))
        {
            state.CompleteSubagent(toolUseId);
            events.Add(ActivityEvent.CreateAgentComplete(hookEvent.SessionId, toolUseId));
        }

        return events;
    }

    private List<ActivityEvent> ProcessSessionStop(HookEvent hookEvent)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        var previousLifecycle = state.Lifecycle;
        state.EndTime = DateTime.UtcNow;

        // Determine status from activity data
        state.Lifecycle = DetermineEndStatus(state, "explicit");

        // Complete main agent
        if (state.MainAgent != null)
        {
            state.MainAgent.State = state.Lifecycle == SessionLifecycle.Failed ? AgentState.Error : AgentState.Complete;
            state.MainAgent.CompleteTime = DateTime.UtcNow;
        }

        // Complete any still-running tool calls
        foreach (var tc in state.ToolCalls.Values.Where(t => t.IsRunning))
        {
            tc.Complete();
        }

        events.Add(ActivityEvent.CreateSessionEnd(hookEvent.SessionId, "explicit"));
        events.Add(ActivityEvent.CreateAgentComplete(hookEvent.SessionId, hookEvent.SessionId));

        if (previousLifecycle != state.Lifecycle)
        {
            LifecycleChanged?.Invoke(this, (hookEvent.SessionId, state.Lifecycle));
        }

        return events;
    }

    /// <summary>
    /// Determines the end status of a session from its activity data.
    /// </summary>
    public static SessionLifecycle DetermineEndStatus(SessionActivityState state, string endReason)
    {
        if (endReason == "timeout")
            return SessionLifecycle.TimedOut;

        // Check for errors: tool calls that ended with errors and no subsequent success
        var hasErrors = state.ToolCalls.Values.Any(t => t.State == ToolCallState.Error);
        var hasFileWrites = state.FileActivities.Values.Any(f => f.WriteCount > 0);
        var hasToolCalls = state.ToolCalls.Count > 0;

        // If last tool calls had errors and no file writes produced, mark as Failed
        if (hasErrors && !hasFileWrites)
            return SessionLifecycle.Failed;

        // Explicit stop with file writes = successful productive session
        if (hasFileWrites)
            return SessionLifecycle.Completed; // Success — has file changes

        // Explicit stop with tool calls but no writes = completed (e.g., research/Q&A)
        if (hasToolCalls)
            return SessionLifecycle.Completed;

        // Explicit stop with no activity at all
        return SessionLifecycle.Completed;
    }

    private List<ActivityEvent> ProcessFileChanged(HookEvent hookEvent)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        if (!string.IsNullOrEmpty(hookEvent.FilePath))
        {
            state.RecordFileAccess(hookEvent.FilePath, "write");
            state.LastActivityTime = DateTime.UtcNow;
            events.Add(ActivityEvent.CreateFileAccessed(
                hookEvent.SessionId, null, hookEvent.FilePath, "write", null));
        }

        return events;
    }

    private List<ActivityEvent> ProcessToolError(HookEvent hookEvent, HookEventData? rawData)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, hookEvent.Cwd,
            source: hookEvent.Source, containerName: hookEvent.ContainerName);

        var toolUseId = hookEvent.ToolUseId ?? "";
        var toolName = hookEvent.ToolName ?? "unknown";

        // Extract error message from raw data
        string? errorMessage = null;
        if (rawData?.ToolResponse != null)
        {
            errorMessage = rawData.ToolResponse.Value.ValueKind == JsonValueKind.String
                ? rawData.ToolResponse.Value.GetString()
                : rawData.ToolResponse.Value.GetRawText();
        }

        // Resolve agent: explicit from hook > original tool call record > main agent
        var agentId = !string.IsNullOrEmpty(hookEvent.AgentId) ? hookEvent.AgentId
            : state.ToolCalls.TryGetValue(toolUseId, out var existingCall) ? existingCall.AgentId
            : (state.MainAgent?.Id ?? hookEvent.SessionId);

        state.RecordToolCallEnd(toolUseId, null, 0, errorMessage ?? "Tool execution failed", hookEvent.FilePath);
        state.LastActivityTime = DateTime.UtcNow;

        events.Add(ActivityEvent.CreateToolCallEnd(
            hookEvent.SessionId, agentId, toolUseId, toolName, null, 0, errorMessage ?? "Tool execution failed"));

        return events;
    }

    private List<ActivityEvent> ProcessSubagentStart(HookEvent hookEvent)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, hookEvent.Cwd,
            source: hookEvent.Source, containerName: hookEvent.ContainerName);

        var agentId = hookEvent.AgentId ?? Guid.NewGuid().ToString();
        var parentId = state.MainAgent?.Id ?? hookEvent.SessionId;
        var name = hookEvent.AgentType ?? "subagent";

        if (!state.Agents.ContainsKey(agentId))
        {
            state.AddSubagent(agentId, parentId, name, hookEvent.AgentType);
        }
        state.LastActivityTime = DateTime.UtcNow;

        events.Add(ActivityEvent.CreateAgentSpawn(
            hookEvent.SessionId, agentId, parentId, name, false, hookEvent.AgentType, null));

        return events;
    }

    private List<ActivityEvent> ProcessSubagentStop(HookEvent hookEvent)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        var agentId = hookEvent.AgentId ?? "";
        state.CompleteSubagent(agentId);
        state.LastActivityTime = DateTime.UtcNow;

        events.Add(ActivityEvent.CreateAgentComplete(hookEvent.SessionId, agentId));

        return events;
    }

    private List<ActivityEvent> ProcessNotification(HookEvent hookEvent)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        state.LastActivityTime = DateTime.UtcNow;

        // Permission prompts transition the agent to WaitingPermission state
        if (hookEvent.NotificationType is "permission_prompt" or "permission")
        {
            var previousLifecycle = state.Lifecycle;
            state.Lifecycle = SessionLifecycle.WaitingPermission;

            if (state.MainAgent != null)
                state.MainAgent.State = AgentState.WaitingPermission;

            events.Add(new ActivityEvent
            {
                Type = ActivityEventType.AgentStateChange,
                SessionId = hookEvent.SessionId,
                AgentId = state.MainAgent?.Id,
                Timestamp = DateTime.UtcNow,
                Source = EventSource.Hook,
                Data = new Dictionary<string, object?>
                {
                    ["agentId"] = state.MainAgent?.Id,
                    ["newState"] = "WaitingPermission",
                    ["previousState"] = previousLifecycle.ToString(),
                    ["message"] = hookEvent.Message
                }
            });

            if (previousLifecycle != state.Lifecycle)
            {
                LifecycleChanged?.Invoke(this, (hookEvent.SessionId, state.Lifecycle));
            }
        }

        return events;
    }

    /// <summary>
    /// Applies a transcript-derived ActivityEvent to the session state.
    /// Delegates to SessionActivityState.ApplyEvent().
    /// </summary>
    private static void ApplyEventToState(SessionActivityState state, ActivityEvent evt)
    {
        state.ApplyEvent(evt);
    }

    // Helpers

    private SessionActivityState GetOrCreateStateLocked(string sessionId, string? cwd = null, string? transcriptPath = null,
        SessionSource source = SessionSource.Local, string? containerName = null)
    {
        if (_states.TryGetValue(sessionId, out var existing))
        {
            if (cwd != null) existing.WorkingDirectory = cwd;
            if (transcriptPath != null) existing.TranscriptPath = transcriptPath;
            // Upgrade source if a more specific one is provided
            if (source != SessionSource.Local) existing.Source = source;
            if (containerName != null) existing.ContainerName = containerName;
            existing.LastActivityTime = DateTime.UtcNow;
            return existing;
        }

        var state = SessionActivityState.Create(sessionId, cwd, transcriptPath, source, containerName);
        _states[sessionId] = state;
        return state;
    }

    /// <summary>
    /// Resolves which agent is making a tool call using heuristics.
    /// "Agent"/"Task" tool calls are always from the main agent (spawning).
    /// Other calls: attribute to the most recently spawned active subagent,
    /// since the main agent blocks while subagents work.
    /// </summary>
    private static string ResolveToolCallAgent(SessionActivityState state, string toolName)
    {
        var mainId = state.MainAgent?.Id ?? state.SessionId;

        // Agent/Task spawning is always from the parent (main agent)
        if (toolName is "Agent" or "Task")
            return mainId;

        // Find active subagents (not main, not completed)
        AgentInstance? bestSubagent = null;
        foreach (var agent in state.Agents.Values)
        {
            if (agent.IsMain || !agent.IsActive) continue;
            // Prefer the most recently spawned active subagent
            if (bestSubagent == null || agent.SpawnTime > bestSubagent.SpawnTime)
                bestSubagent = agent;
        }

        return bestSubagent?.Id ?? mainId;
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
