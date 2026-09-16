using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Services.Spark;

/// <summary>
/// Production <see cref="ISessionCatalog"/> over <see cref="ISessionLifecycleCoordinator"/>
/// and <see cref="TranscriptParserService"/>. Owns the canvas-shaped projection
/// from <see cref="SessionActivityState"/>.
/// </summary>
public sealed class TimelineSessionCatalog : ISessionCatalog
{
    private const string LogSource = "TimelineSessionCatalog";

    private readonly ISessionLifecycleCoordinator? _coord;
    private readonly TranscriptParserService _parser;
    private readonly IDebugLogService? _log;

    public TimelineSessionCatalog(
        ISessionLifecycleCoordinator? sessionCoordinator,
        TranscriptParserService? parser = null,
        IDebugLogService? log = null)
    {
        _coord = sessionCoordinator;
        _parser = parser ?? new TranscriptParserService();
        _log = log;
    }

    public IReadOnlyList<SessionListItem> List()
    {
        var items = new List<SessionListItem>();
        var views = _coord?.GetAllSessions();
        if (views == null) return items;

        foreach (var v in views)
        {
            var st = v.ActivityState;
            var live = v.LiveSession;
            // Prefer LiveSession's DisplayName when present (it carries hook-derived
            // metadata like the directory leaf with worktree decoration); fall back
            // to the activity state's working directory.
            string displayName;
            if (live != null && !string.IsNullOrEmpty(live.DisplayName))
                displayName = live.DisplayName;
            else
                displayName = (st.WorkingDirectory ?? "").Split('/', '\\').LastOrDefault(s => s.Length > 0) ?? "Session";

            items.Add(new SessionListItem
            {
                SessionId = v.SessionId,
                DisplayName = displayName,
                ProjectPath = st.WorkingDirectory ?? live?.WorkingDirectory ?? "",
                // Timeline catalog uses a tighter semantic than SessionView.IsLive:
                // only sessions currently Working or WaitingPermission count as "live"
                // here. Done (between turns) is tracked but not live in the timeline view.
                IsLive = st.DeriveParentDisplay(DateTime.UtcNow)
                    is AgentDisplayState.Working or AgentDisplayState.WaitingPermission,
                StartTime = v.StartTime
            });
        }
        return items;
    }

    public SnapshotEnvelope? GetSnapshot(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        var view = _coord?.GetSession(sessionId);
        if (view == null) return null;

        // The coordinator synthesizes an empty activity state for live-only sessions;
        // distinguish "real" activity state from synthesized one by checking whether
        // the activity service actually tracks it (Agents.Count == 0 + Live present
        // is the placeholder shape).
        var state = view.ActivityState;
        if (state.Agents.Count == 0 && view.LiveSession != null)
            return ProjectPlaceholder(view.LiveSession);

        return ProjectLive(state);
    }

    public async Task<ReplayLoadResult?> LoadReplayAsync(string jsonlPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(jsonlPath)) return null;

        var sessionId = Path.GetFileNameWithoutExtension(jsonlPath);
        // S5: TranscriptParserService.ParseTranscriptRichAsync does not currently accept a
        // CancellationToken — the parse runs to completion. Plumbing a CT through the parser
        // is out of scope here; the orchestrator's _cts is still respected for the surrounding
        // await chain via OperationCanceledException on later awaits.
        var result = await _parser.ParseTranscriptRichAsync(jsonlPath, sessionId);
        if (!result.ParsedSuccessfully || result.Events.Count == 0)
            return null;
        ct.ThrowIfCancellationRequested();

        var state = SessionActivityState.Create(sessionId);
        state.WorkingDirectory = Path.GetDirectoryName(jsonlPath);
        // Stored for archive snapshot semantics; not read by derivation.
        state.Lifecycle = SessionLifecycle.Completed;
        foreach (var evt in result.Events)
            state.ApplyEvent(evt);

        if (result.Summary != null) state.Summary = result.Summary;
        if (result.Model != null && state.MainAgent != null)
            state.MainAgent.Model = result.Model;

        state.EndTime = state.LastActivityTime ?? DateTime.UtcNow;
        if (state.MainAgent != null)
        {
            // Replay path: synthesize a believable terminal state for consumers that still
            // read agent.State (Spark canvas via the snapshot wire). Derivation ignores this.
            state.MainAgent.State = AgentState.Complete;
            state.MainAgent.CompleteTime = state.EndTime;
        }

        var snapshot = ProjectReplay(state);
        var events = result.Events.Select(ProjectEventPayload).ToList();
        return new ReplayLoadResult(snapshot, events);
    }

    public async Task EnrichAsync(string sessionId, CancellationToken ct)
    {
        if (_coord == null || string.IsNullOrEmpty(sessionId)) return;
        try
        {
            await _coord.Advanced.EnrichFromTranscriptAsync(sessionId, ct);
        }
        catch (Exception ex)
        {
            // best-effort, but make the failure observable.
            _log?.Warn(LogSource, $"EnrichFromTranscriptAsync ('{sessionId}') failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------- Projection --------

    private static (Dictionary<string, SnapshotAgent> agents, Dictionary<string, SnapshotFileActivity> files)
        ProjectShared(SessionActivityState state)
    {
        var agents = new Dictionary<string, SnapshotAgent>(state.Agents.Count);
        foreach (var kv in state.Agents)
        {
            var a = kv.Value;
            agents[kv.Key] = new SnapshotAgent
            {
                Id = a.Id,
                Name = a.Name,
                IsMain = a.IsMain,
                ParentId = a.ParentId,
                State = a.State.ToString(),
                Model = a.Model,
                Task = a.Task,
                SpawnTime = a.SpawnTime,
                CompleteTime = a.CompleteTime,
                ToolCallCount = a.ToolCallCount,
                TokensUsed = a.LatestContextTokens,
                LatestContextTokens = a.LatestContextTokens,
                TotalOutputTokens = a.TotalOutputTokens,
                TokensMax = ModelContextSizes.GetMaxTokens(a.Model),
                CurrentToolUseId = a.CurrentToolUseId,
                Context = a.Context == null ? null : new SnapshotAgentContext
                {
                    SystemPrompt = a.Context.SystemPrompt,
                    UserMessages = a.Context.UserMessages,
                    ToolResults = a.Context.ToolResults,
                    Reasoning = a.Context.Reasoning,
                    SubagentResults = a.Context.SubagentResults
                }
            };
        }

        var files = new Dictionary<string, SnapshotFileActivity>();
        foreach (var kv in state.FileActivities)
        {
            files[kv.Key] = new SnapshotFileActivity
            {
                ReadCount = kv.Value.ReadCount,
                WriteCount = kv.Value.WriteCount
            };
        }

        return (agents, files);
    }

    private static LiveSessionSnapshot ProjectLive(SessionActivityState state)
    {
        var (agents, files) = ProjectShared(state);

        // Live mode: only running tool calls (matches old SerializeState).
        var toolCalls = new Dictionary<string, SnapshotToolCall>();
        foreach (var kv in state.ToolCalls)
        {
            if (kv.Value.State != ToolCallState.Running) continue;
            var c = kv.Value;
            toolCalls[kv.Key] = new SnapshotToolCall
            {
                ToolUseId = c.ToolUseId,
                AgentId = c.AgentId,
                ToolName = c.ToolName,
                InputSummary = c.InputSummary,
                State = c.State.ToString(),
                StartTime = c.StartTime
            };
        }

        var messages = state.Messages
            .TakeLast(50)
            .Select(m => new SnapshotMessage
            {
                Type = m.Type switch
                {
                    MessageType.UserMessage => "UserMessage",
                    MessageType.AssistantText => "AssistantMessage",
                    MessageType.Thinking => "ThinkingBlock",
                    _ => ""
                },
                AgentId = m.AgentId,
                Content = m.Content,
                Timestamp = m.Timestamp
            })
            .Where(m => m.Type.Length > 0)
            .ToArray();

        return new LiveSessionSnapshot
        {
            SessionId = state.SessionId,
            WorkingDirectory = state.WorkingDirectory,
            StartTime = state.StartTime,
            EndTime = state.EndTime,
            Lifecycle = state.Lifecycle.ToString(),
            DisplayState = state.DeriveParentDisplay(DateTime.UtcNow).ToString(),
            Agents = agents,
            ToolCalls = toolCalls,
            FileActivities = files,
            Messages = messages
        };
    }

    private static ReplaySessionSnapshot ProjectReplay(SessionActivityState state)
    {
        var (agents, files) = ProjectShared(state);

        // Replay mode: all tool calls (matches old SerializeStateForReplay).
        var toolCalls = new Dictionary<string, SnapshotToolCall>();
        foreach (var kv in state.ToolCalls)
        {
            var c = kv.Value;
            toolCalls[kv.Key] = new SnapshotToolCall
            {
                ToolUseId = c.ToolUseId,
                AgentId = c.AgentId,
                ToolName = c.ToolName,
                InputSummary = c.InputSummary,
                ResultSummary = c.ResultSummary,
                State = c.State.ToString(),
                StartTime = c.StartTime,
                EndTime = c.EndTime,
                TokenCost = c.TokenCost,
                ErrorMessage = c.ErrorMessage
            };
        }

        return new ReplaySessionSnapshot
        {
            SessionId = state.SessionId,
            WorkingDirectory = state.WorkingDirectory,
            StartTime = state.StartTime,
            EndTime = state.EndTime ?? state.LastActivityTime ?? DateTime.UtcNow,
            Lifecycle = state.Lifecycle.ToString(),
            // Replay is historical — derivation inputs are absent. Surface a stable "Done"
            // so the wire shape mirrors the archived-session path in ApiServer.
            DisplayState = "Done",
            Agents = agents,
            ToolCalls = toolCalls,
            FileActivities = files
        };
    }

    private static PlaceholderSessionSnapshot ProjectPlaceholder(LiveSession live)
    {
        var agents = new Dictionary<string, SnapshotAgent>
        {
            [live.ClaudeSessionId] = new SnapshotAgent
            {
                Id = live.ClaudeSessionId,
                Name = "main",
                IsMain = true,
                State = "Active",
                SpawnTime = live.StartTime,
                ToolCallCount = 0
            }
        };

        return new PlaceholderSessionSnapshot
        {
            SessionId = live.ClaudeSessionId,
            WorkingDirectory = live.WorkingDirectory,
            StartTime = live.StartTime,
            Lifecycle = "Active",
            DisplayState = "Working",
            Agents = agents
        };
    }

    // Local copy of the per-event projection. SparkPayloadComposer owns the canonical
    // version for live events; the replay-load path reproduces it here so the catalog
    // does not take a runtime dependency on the composer service.
    private static EventPayload ProjectEventPayload(ActivityEvent evt)
    {
        return new EventPayload
        {
            Type = evt.Type.ToString(),
            SessionId = evt.SessionId,
            AgentId = evt.AgentId,
            Timestamp = evt.Timestamp,
            Data = DeepCloneDictionary(evt.Data)
        };
    }

    private static Dictionary<string, object?> DeepCloneDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(source.Count);
        foreach (var kv in source)
            clone[kv.Key] = DeepCloneValue(kv.Value);
        return clone;
    }

    private static object? DeepCloneValue(object? value) => value switch
    {
        null => null,
        string or bool or int or long or double or decimal or float or short or byte
            or DateTime or DateTimeOffset or Guid or TimeSpan or Uri
            => value,
        IReadOnlyDictionary<string, object?> dict => DeepCloneDictionary(dict),
        IEnumerable<object?> list => list.Select(DeepCloneValue).ToList(),
        _ => value
    };
}
