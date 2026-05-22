using System.Collections.Generic;
using System.Linq;
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
            return _states.Values.Where(s => s.IsActive).Select(s => s.Snapshot()).ToList();
        }
    }

    public IReadOnlyList<SessionActivityState> GetAllStates()
    {
        lock (_lock)
        {
            return _states.Values.Select(s => s.Snapshot()).ToList();
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
        var lifecycleChanges = new List<(string SessionId, SessionLifecycle NewState)>();

        lock (_lock)
        {
            switch (hookEvent.EventType)
            {
                case HookEventType.SessionStart:
                    events.AddRange(ProcessSessionStart(hookEvent, lifecycleChanges));
                    break;

                case HookEventType.ToolStart:
                    events.AddRange(ProcessToolStart(hookEvent, rawData, lifecycleChanges));
                    break;

                case HookEventType.ToolEnd:
                    events.AddRange(ProcessToolEnd(hookEvent, rawData, lifecycleChanges));
                    break;

                case HookEventType.ToolError:
                    events.AddRange(ProcessToolError(hookEvent, rawData, lifecycleChanges));
                    break;

                case HookEventType.SessionStop:
                case HookEventType.SessionEnd:
                    events.AddRange(ProcessSessionStop(hookEvent, lifecycleChanges));
                    break;

                case HookEventType.SubagentStart:
                    events.AddRange(ProcessSubagentStart(hookEvent, lifecycleChanges));
                    break;

                case HookEventType.SubagentStop:
                    events.AddRange(ProcessSubagentStop(hookEvent, lifecycleChanges));
                    break;

                case HookEventType.Notification:
                    events.AddRange(ProcessNotification(hookEvent, lifecycleChanges));
                    break;

                case HookEventType.FileChanged:
                    events.AddRange(ProcessFileChanged(hookEvent, lifecycleChanges));
                    break;

                case HookEventType.AgentMetadataUpdate:
                    events.AddRange(ProcessAgentMetadataUpdate(hookEvent, lifecycleChanges));
                    break;

                case HookEventType.AgentDeleted:
                    events.AddRange(ProcessAgentDeleted(hookEvent, lifecycleChanges));
                    break;
            }
        }

        // Fire events outside lock
        foreach (var evt in events)
        {
            ActivityEventProcessed?.Invoke(this, evt);
        }
        foreach (var lc in lifecycleChanges)
        {
            LifecycleChanged?.Invoke(this, lc);
        }
    }

    public void ProcessTranscriptEvents(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null)
    {
        if (events.Count == 0 && summary == null && model == null)
            return;

        var lifecycleChanges = new List<(string SessionId, SessionLifecycle NewState)>();

        lock (_lock)
        {
            if (!_states.TryGetValue(sessionId, out var state))
                return;

            ReviveIfTerminal(state, lifecycleChanges);
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
        foreach (var lc in lifecycleChanges)
        {
            LifecycleChanged?.Invoke(this, lc);
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

    private List<ActivityEvent> ProcessSessionStart(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, lifecycleChanges, hookEvent.Cwd, hookEvent.TranscriptPath,
            hookEvent.Source, hookEvent.ContainerName);

        // No event has happened yet from the agent's POV — leave timestamps null.
        // LastEventKind is derived from those timestamps and resolves to None when all
        // three are null, so no explicit reset is needed here.

        var evt = ActivityEvent.CreateSessionStart(hookEvent.SessionId, hookEvent.Cwd, hookEvent.TranscriptPath);
        evt.Timestamp = hookEvent.Timestamp;
        events.Add(evt);

        // Spawn main agent event
        events.Add(ActivityEvent.CreateAgentSpawn(
            hookEvent.SessionId, hookEvent.SessionId, null, "main", true, null, null));

        return events;
    }

    private List<ActivityEvent> ProcessToolStart(HookEvent hookEvent, HookEventData? rawData, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, lifecycleChanges, hookEvent.Cwd,
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
        // Use explicit agent_id if the agent is already registered. Otherwise attribute to main.
        // Agents are only created via SubagentStart — never from tool calls (avoids ghost nodes).
        var mainId = state.MainAgent?.Id ?? state.SessionId;
        var agentId = !string.IsNullOrEmpty(hookEvent.AgentId) && state.Agents.ContainsKey(hookEvent.AgentId)
            ? hookEvent.AgentId
            : mainId;

        // Don't spawn subagent here for Agent/Task tools — the subagent-start hook
        // handles that with the correct agent_id. Creating one here with toolUseId as ID
        // causes duplicate nodes on the canvas.

        // Record tool call
        state.RecordToolCallStart(toolUseId, toolName, agentId, inputSummary, filePath);

        if (state.Agents.TryGetValue(agentId, out var startAgent))
            startAgent.StampActivity(hookEvent.Timestamp);

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

    private List<ActivityEvent> ProcessToolEnd(HookEvent hookEvent, HookEventData? rawData, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, lifecycleChanges, hookEvent.Cwd,
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

        if (agentId != null && state.Agents.TryGetValue(agentId, out var endAgent))
            endAgent.StampActivity(hookEvent.Timestamp);

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
            if (state.Agents.TryGetValue(toolUseId, out var spawnedSubagent))
                spawnedSubagent.StampStop(hookEvent.Timestamp);
            state.CompleteSubagent(toolUseId);
            events.Add(ActivityEvent.CreateAgentComplete(hookEvent.SessionId, toolUseId));
        }

        return events;
    }

    private List<ActivityEvent> ProcessSessionStop(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        state.EndTime = DateTime.UtcNow;

        // Session-level Stop hook stamps the main agent. SubagentStop is handled separately.
        if (state.MainAgent != null)
            state.MainAgent.StampStop(hookEvent.Timestamp);

        FinalizeSessionEnd(state, hookEvent.SessionId, "explicit", events, lifecycleChanges);
        return events;
    }

    /// <summary>
    /// Performs the actual lifecycle transition + main-agent completion + tool-call cleanup
    /// for a session that has reached its true end (no more subagents pending).
    /// Shared by the synchronous path (ProcessSessionStop) and the deferred path
    /// (ProcessSubagentStop, after the last subagent finishes).
    /// </summary>
    private void FinalizeSessionEnd(SessionActivityState state, string sessionId, string endReason, List<ActivityEvent> events,
        List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var previousLifecycle = state.Lifecycle;
        state.Lifecycle = DetermineEndStatus(state, endReason);

        if (state.MainAgent != null)
        {
            state.MainAgent.State = state.Lifecycle == SessionLifecycle.Failed ? AgentState.Error : AgentState.Complete;
            state.MainAgent.CompleteTime = DateTime.UtcNow;
        }

        foreach (var tc in state.ToolCalls.Values.Where(t => t.IsRunning))
        {
            tc.Complete();
        }

        events.Add(ActivityEvent.CreateSessionEnd(sessionId, endReason));
        events.Add(ActivityEvent.CreateAgentComplete(sessionId, sessionId));

        if (previousLifecycle != state.Lifecycle)
        {
            lifecycleChanges.Add((sessionId, state.Lifecycle));
        }
    }

    /// <summary>
    /// Determines the end status of a session from its end reason. Main-session Failed is
    /// reserved for fatal end signals ("error"/"crash") — per-tool errors don't poison the
    /// session verdict, because the main session typically retries past tool failures and
    /// only rarely gets truly stuck. Today no caller emits a fatal reason, so explicit stops
    /// always become Completed; a fatal channel can be wired later.
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

    /// <summary>
    /// Un-sticks a session whose lifecycle was previously marked terminal
    /// (Completed/Failed/TimedOut) when fresh activity arrives. Claude Code's
    /// Stop hook fires between every assistant turn — not only at session end — and the
    /// inactivity tracker can flip a still-running session to TimedOut while a long tool
    /// produces no transcript writes. Without this revive, the tree would show "Done" or
    /// "Timed out" forever once either fires, even as new tool calls continue.
    /// </summary>
    private void ReviveIfTerminal(SessionActivityState state, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var verdict = LifecycleDecision.ClassifyArrival(state.Lifecycle);
        if (!verdict.Revive || verdict.NewLifecycle is not { } newLifecycle)
            return;

        state.Lifecycle = newLifecycle;
        state.EndTime = null;

        if (state.MainAgent is { } main)
        {
            main.CompleteTime = null;
            if (main.State is AgentState.Complete or AgentState.Error)
                main.State = AgentState.Active;
        }

        lifecycleChanges.Add((state.SessionId, state.Lifecycle));
    }

    private List<ActivityEvent> ProcessFileChanged(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        ReviveIfTerminal(state, lifecycleChanges);
        if (!string.IsNullOrEmpty(hookEvent.FilePath))
        {
            state.RecordFileAccess(hookEvent.FilePath, "write");
            state.LastActivityTime = DateTime.UtcNow;
            events.Add(ActivityEvent.CreateFileAccessed(
                hookEvent.SessionId, null, hookEvent.FilePath, "write", null));
        }

        return events;
    }

    private List<ActivityEvent> ProcessToolError(HookEvent hookEvent, HookEventData? rawData, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, lifecycleChanges, hookEvent.Cwd,
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

        if (agentId != null && state.Agents.TryGetValue(agentId, out var errorAgent))
            errorAgent.StampActivity(hookEvent.Timestamp);

        events.Add(ActivityEvent.CreateToolCallEnd(
            hookEvent.SessionId, agentId, toolUseId, toolName, null, 0, errorMessage ?? "Tool execution failed"));

        return events;
    }

    private List<ActivityEvent> ProcessSubagentStart(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();
        var state = GetOrCreateStateLocked(hookEvent.SessionId, lifecycleChanges, hookEvent.Cwd,
            source: hookEvent.Source, containerName: hookEvent.ContainerName);

        var agentId = hookEvent.AgentId ?? Guid.NewGuid().ToString();
        var parentId = state.MainAgent?.Id ?? hookEvent.SessionId;
        var name = hookEvent.AgentType ?? "subagent";

        // Look up the parent's running Agent/Task tool call to extract role and task from the description
        string? role = null;
        string? task = hookEvent.AgentType;
        var agentToolCall = state.ToolCalls.Values
            .Where(t => t.ToolName is "Agent" or "Task"
                    && t.AgentId == parentId
                    && t.State == ToolCallState.Running)
            .OrderByDescending(t => t.StartTime)
            .FirstOrDefault();

        // Fallback: if no running tool call found (race condition with parallel agents),
        // try the most recently started Agent/Task tool call regardless of state
        agentToolCall ??= state.ToolCalls.Values
            .Where(t => t.ToolName is "Agent" or "Task"
                    && t.AgentId == parentId)
            .OrderByDescending(t => t.StartTime)
            .FirstOrDefault();

        if (agentToolCall?.InputSummary is { } description)
        {
            task = description;
            var colonIndex = description.IndexOf(':');
            if (colonIndex > 0)
            {
                role = description[..colonIndex].Trim();
            }
        }

        if (!state.Agents.ContainsKey(agentId))
        {
            state.AddSubagent(agentId, parentId, name, task, role: role);
        }
        else
        {
            // Agent was auto-registered by an early tool call — upgrade with real metadata
            var agent = state.Agents[agentId];
            agent.Name = name;
            agent.Task = task;
            agent.Role = role;
            agent.ParentId = parentId;
        }

        state.LastActivityTime = DateTime.UtcNow;

        if (state.Agents.TryGetValue(agentId, out var newSubagent))
            newSubagent.StampActivity(hookEvent.Timestamp);
        // Parent is doing work — it just spawned a subagent.
        if (state.MainAgent != null)
            state.MainAgent.StampActivity(hookEvent.Timestamp);

        events.Add(ActivityEvent.CreateAgentSpawn(
            hookEvent.SessionId, agentId, parentId, name, false, task, null, role: role));

        return events;
    }

    private List<ActivityEvent> ProcessSubagentStop(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        ReviveIfTerminal(state, lifecycleChanges);
        var agentId = hookEvent.AgentId ?? "";

        if (state.Agents.TryGetValue(agentId, out var stoppedSubagent))
            stoppedSubagent.StampStop(hookEvent.Timestamp);
        // Informational only on the main agent — subagent stopping is not main activity,
        // so it goes into LastSubagentStopTime (which never feeds LastEventKind).
        if (state.MainAgent != null)
            state.MainAgent.StampSubagentStop(hookEvent.Timestamp);

        state.CompleteSubagent(agentId);
        state.LastActivityTime = DateTime.UtcNow;

        events.Add(ActivityEvent.CreateAgentComplete(hookEvent.SessionId, agentId));

        return events;
    }

    private List<ActivityEvent> ProcessAgentMetadataUpdate(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        ReviveIfTerminal(state, lifecycleChanges);
        var agentId = hookEvent.AgentId ?? "";
        if (!state.Agents.ContainsKey(agentId))
            return events;

        // Extract metadata fields from the hook event's extra data
        string? role = null, executionMode = null, lifespanType = null, milestoneId = null, retryOfAgentId = null;
        int? retryCount = null, denialCount = null, completionPercentage = null;

        if (hookEvent.ExtraData != null)
        {
            if (hookEvent.ExtraData.TryGetValue("role", out var r)) role = r?.ToString();
            if (hookEvent.ExtraData.TryGetValue("executionMode", out var em)) executionMode = em?.ToString();
            if (hookEvent.ExtraData.TryGetValue("lifespanType", out var lt)) lifespanType = lt?.ToString();
            if (hookEvent.ExtraData.TryGetValue("milestoneId", out var mi)) milestoneId = mi?.ToString();
            if (hookEvent.ExtraData.TryGetValue("retryOfAgentId", out var ro)) retryOfAgentId = ro?.ToString();
            if (hookEvent.ExtraData.TryGetValue("retryCount", out var rc) && rc is int rcVal) retryCount = rcVal;
            if (hookEvent.ExtraData.TryGetValue("denialCount", out var dc) && dc is int dcVal) denialCount = dcVal;
            if (hookEvent.ExtraData.TryGetValue("completionPercentage", out var cp) && cp is int cpVal) completionPercentage = cpVal;
        }

        List<string>? blockedByAgentIds = null;
        if (hookEvent.ExtraData != null && hookEvent.ExtraData.TryGetValue("blockedByAgentIds", out var bba))
        {
            if (bba is List<string> bbaList) blockedByAgentIds = bbaList;
            else if (bba is IEnumerable<object> bbaEnum) blockedByAgentIds = bbaEnum.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList();
        }

        var evt = ActivityEvent.CreateAgentMetadataUpdate(
            hookEvent.SessionId, agentId,
            role, executionMode, lifespanType,
            retryCount, denialCount, milestoneId,
            completionPercentage, retryOfAgentId, blockedByAgentIds);

        state.ApplyEvent(evt);
        state.LastActivityTime = DateTime.UtcNow;

        events.Add(evt);

        return events;
    }

    private List<ActivityEvent> ProcessAgentDeleted(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        ReviveIfTerminal(state, lifecycleChanges);
        var agentId = hookEvent.AgentId ?? "";
        if (!state.Agents.ContainsKey(agentId))
            return events;

        var evt = ActivityEvent.CreateAgentDeleted(hookEvent.SessionId, agentId);
        state.ApplyEvent(evt);
        state.LastActivityTime = DateTime.UtcNow;
        events.Add(evt);

        return events;
    }

    private List<ActivityEvent> ProcessNotification(HookEvent hookEvent, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges)
    {
        var events = new List<ActivityEvent>();

        if (!_states.TryGetValue(hookEvent.SessionId, out var state))
            return events;

        ReviveIfTerminal(state, lifecycleChanges);
        state.LastActivityTime = DateTime.UtcNow;

        // Permission prompts transition the agent to WaitingPermission state
        if (hookEvent.NotificationType is "permission_prompt" or "permission")
        {
            var previousLifecycle = state.Lifecycle;
            // Redundant: derivation owns display state. Kept to fire LifecycleChanged for subscribers.
            state.Lifecycle = SessionLifecycle.WaitingPermission;

            if (state.MainAgent != null)
            {
                state.MainAgent.State = AgentState.WaitingPermission;
                // Invariant: permission prompts are always session-level, never per-subagent.
                state.MainAgent.StampPermissionPrompt(hookEvent.Timestamp);
            }

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
                lifecycleChanges.Add((hookEvent.SessionId, state.Lifecycle));
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

    private SessionActivityState GetOrCreateStateLocked(string sessionId, List<(string SessionId, SessionLifecycle NewState)> lifecycleChanges,
        string? cwd = null, string? transcriptPath = null,
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
            ReviveIfTerminal(existing, lifecycleChanges);
            return existing;
        }

        var state = SessionActivityState.Create(sessionId, cwd, transcriptPath, source, containerName);
        _states[sessionId] = state;
        return state;
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
