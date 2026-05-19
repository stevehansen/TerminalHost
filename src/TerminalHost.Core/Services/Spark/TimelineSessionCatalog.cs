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
/// Production <see cref="ISessionCatalog"/> over <see cref="ITimelineService"/>,
/// <see cref="ISessionActivityService"/>, and <see cref="TranscriptParserService"/>.
/// Owns the canvas-shaped projection from <see cref="SessionActivityState"/>.
/// </summary>
public sealed class TimelineSessionCatalog : ISessionCatalog
{
    private const string LogSource = "TimelineSessionCatalog";

    private readonly ITimelineService? _timeline;
    private readonly ISessionActivityService? _activity;
    private readonly TranscriptParserService _parser;
    private readonly IDebugLogService? _log;

    public TimelineSessionCatalog(
        ITimelineService? timeline,
        ISessionActivityService? activity,
        TranscriptParserService? parser = null,
        IDebugLogService? log = null)
    {
        _timeline = timeline;
        _activity = activity;
        _parser = parser ?? new TranscriptParserService();
        _log = log;
    }

    public IReadOnlyList<SessionListItem> List()
    {
        var items = new List<SessionListItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var liveSessions = _timeline?.GetLiveSessions();
        if (liveSessions != null)
        {
            foreach (var s in liveSessions)
            {
                if (string.IsNullOrEmpty(s.ClaudeSessionId)) continue;
                if (!seen.Add(s.ClaudeSessionId)) continue;
                items.Add(new SessionListItem
                {
                    SessionId = s.ClaudeSessionId,
                    DisplayName = s.DisplayName,
                    ProjectPath = s.WorkingDirectory ?? "",
                    IsLive = s.IsActive,
                    StartTime = s.StartTime
                });
            }
        }

        var activityStates = _activity?.GetAllStates();
        if (activityStates != null)
        {
            foreach (var st in activityStates)
            {
                if (!seen.Add(st.SessionId)) continue;
                var dirName = (st.WorkingDirectory ?? "").Split('/', '\\').LastOrDefault(s => s.Length > 0) ?? "Session";
                items.Add(new SessionListItem
                {
                    SessionId = st.SessionId,
                    DisplayName = dirName,
                    ProjectPath = st.WorkingDirectory ?? "",
                    IsLive = st.Lifecycle == SessionLifecycle.Active,
                    StartTime = st.StartTime
                });
            }
        }

        return items;
    }

    public SessionSnapshot? GetSnapshot(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        var state = _activity?.GetState(sessionId);
        if (state != null)
            return Project(state, isReplay: false);

        // Fall back to a placeholder snapshot for sessions known to the timeline but not yet tracked.
        var live = _timeline?.GetLiveSessionByClaudeId(sessionId);
        if (live != null)
            return ProjectPlaceholder(live);

        return null;
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
        state.Lifecycle = SessionLifecycle.Completed;
        foreach (var evt in result.Events)
            state.ApplyEvent(evt);

        if (result.Summary != null) state.Summary = result.Summary;
        if (result.Model != null && state.MainAgent != null)
            state.MainAgent.Model = result.Model;

        state.EndTime = state.LastActivityTime ?? DateTime.UtcNow;
        if (state.MainAgent != null)
        {
            state.MainAgent.State = AgentState.Complete;
            state.MainAgent.CompleteTime = state.EndTime;
        }

        var snapshot = Project(state, isReplay: true);
        var events = result.Events.Select(SparkCanvasOrchestrator.ToEventPayload).ToList();
        return new ReplayLoadResult(snapshot, events);
    }

    public async Task EnrichAsync(string sessionId, CancellationToken ct)
    {
        if (_activity == null || string.IsNullOrEmpty(sessionId)) return;
        try
        {
            await _activity.EnrichFromTranscriptAsync(sessionId);
        }
        catch (Exception ex)
        {
            // best-effort, but make the failure observable.
            _log?.Warn(LogSource, $"EnrichFromTranscriptAsync ('{sessionId}') failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------- Projection --------

    private static SessionSnapshot Project(SessionActivityState state, bool isReplay)
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

        var toolCalls = new Dictionary<string, SnapshotToolCall>();
        foreach (var kv in state.ToolCalls)
        {
            // Live mode: only running tool calls (matches old SerializeState).
            // Replay mode: all tool calls (matches old SerializeStateForReplay).
            if (!isReplay && kv.Value.State != ToolCallState.Running) continue;
            var c = kv.Value;
            toolCalls[kv.Key] = new SnapshotToolCall
            {
                ToolUseId = c.ToolUseId,
                AgentId = c.AgentId,
                ToolName = c.ToolName,
                InputSummary = c.InputSummary,
                ResultSummary = isReplay ? c.ResultSummary : null,
                State = c.State.ToString(),
                StartTime = c.StartTime,
                EndTime = isReplay ? c.EndTime : null,
                TokenCost = isReplay ? c.TokenCost : null,
                ErrorMessage = isReplay ? c.ErrorMessage : null
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

        var messages = isReplay
            ? Array.Empty<SnapshotMessage>()
            : state.Messages
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

        return new SessionSnapshot
        {
            SessionId = state.SessionId,
            WorkingDirectory = state.WorkingDirectory,
            StartTime = state.StartTime,
            EndTime = state.EndTime,
            Lifecycle = state.Lifecycle.ToString(),
            IsReplay = isReplay,
            Agents = agents,
            ToolCalls = toolCalls,
            FileActivities = files,
            Messages = messages
        };
    }

    private static SessionSnapshot ProjectPlaceholder(LiveSession live)
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

        return new SessionSnapshot
        {
            SessionId = live.ClaudeSessionId,
            WorkingDirectory = live.WorkingDirectory,
            StartTime = live.StartTime,
            Lifecycle = "Active",
            IsReplay = false,
            Agents = agents
        };
    }
}
