using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Spark Canvas — real-time force-directed visualization of AI agent execution.
/// Hosted as a center panel inside TerminalPairView via NativeWebView.
/// Manages session selection, activity event forwarding, and initial state loading.
/// </summary>
public partial class SparkCanvasViewModel : BasePanelViewModel, IDisposable
{
    private readonly ISessionActivityService? _activityService;
    private readonly ITimelineService? _timelineService;
    private readonly IApiServer? _apiServer;
    private readonly IConfigurationService? _configService;

    #region IPanelableViewModel Implementation

    public override string PanelId => "sparkCanvas";
    public override string PanelTitle => "Spark";
    public override string PanelIcon => "\u2728"; // Sparkles emoji
    public override PanelSizePreset SizePreset => PanelSizePreset.Full;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "\uD83D\uDCC2", // Open folder
            Tooltip = "Load JSONL transcript file",
            Command = OpenJsonlFileCommand
        },
        new PanelHeaderCommand
        {
            Icon = "\u21BB", // Refresh
            Tooltip = "Refresh session list",
            Command = RefreshSessionsCommand
        },
        new PanelHeaderCommand
        {
            Icon = "\u2716", // X
            Tooltip = "Close Spark Canvas",
            Command = CloseCommand
        }
    ];

    public override string? StatusText => CurrentSessionId != null
        ? $"Session: {CurrentSessionId[..Math.Min(8, CurrentSessionId.Length)]}..."
        : "No session selected";

    #endregion

    #region Properties

    [ObservableProperty]
    private string? _currentSessionId;

    [ObservableProperty]
    private string _apiBaseUrl = "";

    [ObservableProperty]
    private bool _isCanvasReady;

    [ObservableProperty]
    private string _connectionStatus = "Connecting";

    /// <summary>
    /// Whether the canvas is in multi-session observatory mode.
    /// When true, events from ALL sessions are forwarded (not just CurrentSessionId).
    /// </summary>
    private bool _isMultiMode;

    /// <summary>
    /// When true, the view should open the JSONL file picker once it attaches.
    /// Handles the case where the command fires before the view subscribes to the event.
    /// </summary>
    public bool HasPendingJsonlOpen { get; private set; }

    /// <summary>
    /// Pending replay data to push when the canvas becomes ready.
    /// </summary>
    private (object state, List<object> events)? _pendingReplay;

    public ObservableCollection<SparkSessionItem> AvailableSessions { get; } = new();

    public bool IsApiServerRunning => _apiServer?.IsRunning ?? false;

    #endregion

    #region Events

    /// <summary>
    /// Raised when the ViewModel needs to send a message to the WebView canvas.
    /// </summary>
    public event EventHandler<string>? SendMessageToCanvas;

    /// <summary>
    /// Raised when the user wants to open a JSONL file. The View handles the file dialog.
    /// </summary>
    public event EventHandler? RequestOpenJsonlFile;

    #endregion

    public SparkCanvasViewModel(
        ISessionActivityService? activityService = null,
        IApiServer? apiServer = null,
        ITimelineService? timelineService = null,
        IConfigurationService? configService = null)
    {
        _activityService = activityService;
        _apiServer = apiServer;
        _timelineService = timelineService;
        _configService = configService;

        if (_apiServer != null)
            ApiBaseUrl = _apiServer.BaseUrl ?? "http://localhost:19280";

        if (_activityService != null)
            _activityService.ActivityEventProcessed += OnActivityEvent;
    }

    public void OpenSession(string sessionId)
    {
        try
        {
            CurrentSessionId = sessionId;

            if (!IsCanvasReady) return;

            // Clear previous session data before loading new one
            PostToCanvas(new { action = "clear" });

            var state = _activityService?.GetState(sessionId);
            if (state != null)
            {
                PostToCanvas(new
                {
                    action = "loadState",
                    state = SerializeState(state)
                });
            }

            // Events are pushed via postMessage from OnActivityEvent — no SSE needed.
        }
        catch (Exception ex)
        {
            LogSparkError("OpenSession", ex);
        }
    }

    public void OnCanvasReady()
    {
        IsCanvasReady = true;

        var savedTheme = _configService?.Load().Settings.Timeline.SparkTheme ?? "holographic";
        PostToCanvas(new { action = "setTheme", theme = savedTheme });

        RefreshSessions();

        // Flush pending replay data (loaded before canvas was ready)
        if (_pendingReplay is { } replay)
        {
            _pendingReplay = null;
            PushReplayToCanvas(replay.state, replay.events);
            return;
        }
        if (CurrentSessionId != null)
            OpenSession(CurrentSessionId);
        else
            AutoConnectToActiveSession();
    }

    public void OnCanvasMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var action = doc.RootElement.GetProperty("action").GetString();

            switch (action)
            {
                case "selectSession":
                {
                    var sessionId = doc.RootElement.GetProperty("sessionId").GetString();
                    if (sessionId != null)
                    {
                        _isMultiMode = false;
                        OpenSession(sessionId);
                    }
                    break;
                }
                case "requestMultiMode":
                    LoadMultiMode();
                    break;
                case "exitMultiMode":
                    _isMultiMode = false;
                    break;
                case "themeChanged":
                {
                    var theme = doc.RootElement.GetProperty("theme").GetString();
                    if (theme != null && _configService != null)
                    {
                        var config = _configService.Load();
                        config.Settings.Timeline.SparkTheme = theme;
                        _configService.Save(config);
                    }
                    break;
                }
            }
        }
        catch { }
    }

    #region JSONL File Loading

    [RelayCommand]
    private void OpenJsonlFile()
    {
        if (RequestOpenJsonlFile != null)
        {
            HasPendingJsonlOpen = false;
            RequestOpenJsonlFile.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // View hasn't subscribed yet — queue for when it attaches
            HasPendingJsonlOpen = true;
        }
    }

    /// <summary>
    /// Loads a JSONL transcript file and pushes the parsed session to the canvas.
    /// </summary>
    public async Task LoadJsonlFileAsync(string filePath)
    {
        var parser = new TranscriptParserService();
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        var result = await parser.ParseTranscriptRichAsync(filePath, sessionId);

        if (!result.ParsedSuccessfully || result.Events.Count == 0)
            return;

        // Build a SessionActivityState from the parsed events
        var state = SessionActivityState.Create(sessionId);
        state.WorkingDirectory = Path.GetDirectoryName(filePath);
        state.Lifecycle = SessionLifecycle.Completed;

        foreach (var evt in result.Events)
        {
            state.ApplyEvent(evt);
        }

        if (result.Summary != null)
            state.Summary = result.Summary;
        if (result.Model != null && state.MainAgent != null)
            state.MainAgent.Model = result.Model;

        // Mark session as completed
        state.EndTime = state.LastActivityTime ?? DateTime.UtcNow;
        if (state.MainAgent != null)
        {
            state.MainAgent.State = AgentState.Complete;
            state.MainAgent.CompleteTime = state.EndTime;
        }

        CurrentSessionId = sessionId;

        var replayState = SerializeStateForReplay(state);
        var replayEvents = result.Events.Select(SerializeEvent).ToList();

        if (!IsCanvasReady)
        {
            _pendingReplay = (replayState, replayEvents);
            return;
        }

        PushReplayToCanvas(replayState, replayEvents);
    }

    private void PushReplayToCanvas(object replayState, List<object> replayEvents)
    {
        PostToCanvas(new { action = "clear" });
        PostToCanvas(new
        {
            action = "loadReplay",
            state = replayState,
            events = replayEvents
        });
    }

    /// <summary>
    /// Serializes state with ALL tool calls (not just running ones) for replay mode.
    /// </summary>
    private static object SerializeStateForReplay(SessionActivityState state)
    {
        return new
        {
            sessionId = state.SessionId,
            workingDirectory = state.WorkingDirectory,
            startTime = state.StartTime,
            endTime = state.EndTime,
            lifecycle = state.Lifecycle.ToString(),
            agents = state.Agents.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    id = kv.Value.Id,
                    name = kv.Value.Name,
                    isMain = kv.Value.IsMain,
                    parentId = kv.Value.ParentId,
                    state = kv.Value.State.ToString(),
                    model = kv.Value.Model,
                    task = kv.Value.Task,
                    spawnTime = kv.Value.SpawnTime,
                    completeTime = kv.Value.CompleteTime,
                    toolCallCount = kv.Value.ToolCallCount,
                    tokensUsed = kv.Value.Context?.Total ?? 0,
                    tokensMax = ModelContextSizes.GetMaxTokens(kv.Value.Model),
                    currentToolUseId = kv.Value.CurrentToolUseId,
                    context = kv.Value.Context != null ? new
                    {
                        systemPrompt = kv.Value.Context.SystemPrompt,
                        userMessages = kv.Value.Context.UserMessages,
                        toolResults = kv.Value.Context.ToolResults,
                        reasoning = kv.Value.Context.Reasoning,
                        subagentResults = kv.Value.Context.SubagentResults
                    } : (object?)null
                }),
            toolCalls = state.ToolCalls.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    toolUseId = kv.Value.ToolUseId,
                    agentId = kv.Value.AgentId,
                    toolName = kv.Value.ToolName,
                    inputSummary = kv.Value.InputSummary,
                    resultSummary = kv.Value.ResultSummary,
                    state = kv.Value.State.ToString(),
                    startTime = kv.Value.StartTime,
                    endTime = kv.Value.EndTime,
                    tokenCost = kv.Value.TokenCost,
                    errorMessage = kv.Value.ErrorMessage
                }),
            fileActivities = state.FileActivities.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    readCount = kv.Value.ReadCount,
                    writeCount = kv.Value.WriteCount
                })
        };
    }

    #endregion

    #region Session Picker

    [RelayCommand]
    private void RefreshSessions()
    {
        try
        {
            AvailableSessions.Clear();

            var liveSessions = _timelineService?.GetLiveSessions();
            if (liveSessions != null)
            {
                foreach (var session in liveSessions)
                {
                    AvailableSessions.Add(new SparkSessionItem
                    {
                        SessionId = session.ClaudeSessionId,
                        DisplayName = session.DisplayName,
                        ProjectPath = session.WorkingDirectory,
                        IsLive = session.IsActive,
                        StartTime = session.StartTime
                    });
                }
            }

            var activityStates = _activityService?.GetAllStates();
            if (activityStates != null)
            {
                foreach (var state in activityStates)
                {
                    if (AvailableSessions.Any(s => s.SessionId == state.SessionId))
                        continue;

                    var dirName = state.WorkingDirectory.Split('/', '\\').LastOrDefault(s => s.Length > 0) ?? "Session";
                    AvailableSessions.Add(new SparkSessionItem
                    {
                        SessionId = state.SessionId,
                        DisplayName = dirName,
                        ProjectPath = state.WorkingDirectory,
                        IsLive = state.Lifecycle == SessionLifecycle.Active,
                        StartTime = state.StartTime
                    });
                }
            }

            if (IsCanvasReady)
            {
                PostToCanvas(new
                {
                    action = "sessionList",
                    sessions = AvailableSessions.Select(s => new
                    {
                        sessionId = s.SessionId,
                        displayName = s.DisplayName,
                        projectPath = s.ProjectPath,
                        isLive = s.IsLive,
                        startTime = s.StartTime
                    })
                });
            }
        }
        catch (Exception ex)
        {
            LogSparkError("RefreshSessions", ex);
        }
    }

    private void AutoConnectToActiveSession()
    {
        var first = AvailableSessions.FirstOrDefault(s => s.IsLive)
            ?? AvailableSessions.FirstOrDefault();

        if (first != null)
        {
            OpenSession(first.SessionId);
        }
        else
        {
            // No sessions yet — tell canvas to show waiting state (will go live when a session starts)
            PostToCanvas(new { action = "setSession", sessionId = (string?)null, sessionName = "Waiting for session..." });
        }
    }

    [RelayCommand]
    private void SelectSession(string? sessionId)
    {
        if (sessionId != null)
            OpenSession(sessionId);
    }

    /// <summary>
    /// Enter multi-session observatory mode — push all session states to canvas via postMessage.
    /// </summary>
    private void LoadMultiMode()
    {
        _isMultiMode = true;
        CurrentSessionId = null;

        var allStates = _activityService?.GetAllStates() ?? [];
        var liveSessions = _timelineService?.GetLiveSessions();

        var serializedStates = new List<object>();
        foreach (var state in allStates)
        {
            serializedStates.Add(SerializeState(state));
        }

        // Include sessions from timeline that aren't in activity service yet
        if (liveSessions != null)
        {
            var existingIds = new HashSet<string>(allStates.Select(s => s.SessionId));
            foreach (var live in liveSessions.Where(s => s.IsActive && !existingIds.Contains(s.ClaudeSessionId)))
            {
                serializedStates.Add(new
                {
                    sessionId = live.ClaudeSessionId,
                    workingDirectory = live.WorkingDirectory,
                    startTime = live.StartTime,
                    lifecycle = "Active",
                    agents = new Dictionary<string, object>
                    {
                        [live.ClaudeSessionId] = new
                        {
                            id = live.ClaudeSessionId,
                            name = "main",
                            isMain = true,
                            state = "Active",
                            spawnTime = live.StartTime,
                            toolCallCount = 0
                        }
                    },
                    toolCalls = new Dictionary<string, object>()
                });
            }
        }

        PostToCanvas(new
        {
            action = "loadMultiState",
            sessions = serializedStates
        });
    }

    #endregion

    #region Event Handling

    private void OnActivityEvent(object? sender, ActivityEvent evt)
    {
        try
        {
            // Auto-connect to first session if none selected yet (single-session mode)
            if (!_isMultiMode && CurrentSessionId == null && evt.Type == ActivityEventType.SessionStart)
            {
                OpenSession(evt.SessionId);
                return;
            }

            // In multi-mode, forward events from ALL sessions
            // In single-mode, only forward events for the current session
            if (!_isMultiMode && (CurrentSessionId == null || evt.SessionId != CurrentSessionId)) return;

            // Always forward events via postMessage
            if (IsCanvasReady)
            {
                PostToCanvas(new
                {
                    action = "event",
                    @event = SerializeEvent(evt)
                });
            }
        }
        catch (Exception ex)
        {
            LogSparkError("OnActivityEvent", ex);
        }
    }

    #endregion

    #region Serialization Helpers

    private void PostToCanvas(object message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            SendMessageToCanvas?.Invoke(this, json);
        }
        catch { }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static object SerializeState(SessionActivityState state)
    {
        return new
        {
            sessionId = state.SessionId,
            workingDirectory = state.WorkingDirectory,
            startTime = state.StartTime,
            endTime = state.EndTime,
            lifecycle = state.Lifecycle.ToString(),
            agents = state.Agents.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    id = kv.Value.Id,
                    name = kv.Value.Name,
                    isMain = kv.Value.IsMain,
                    parentId = kv.Value.ParentId,
                    state = kv.Value.State.ToString(),
                    model = kv.Value.Model,
                    task = kv.Value.Task,
                    spawnTime = kv.Value.SpawnTime,
                    completeTime = kv.Value.CompleteTime,
                    toolCallCount = kv.Value.ToolCallCount,
                    tokensUsed = kv.Value.Context?.Total ?? 0,
                    tokensMax = ModelContextSizes.GetMaxTokens(kv.Value.Model),
                    currentToolUseId = kv.Value.CurrentToolUseId,
                    context = kv.Value.Context != null ? new
                    {
                        systemPrompt = kv.Value.Context.SystemPrompt,
                        userMessages = kv.Value.Context.UserMessages,
                        toolResults = kv.Value.Context.ToolResults,
                        reasoning = kv.Value.Context.Reasoning,
                        subagentResults = kv.Value.Context.SubagentResults
                    } : (object?)null
                }),
            toolCalls = state.ToolCalls
                .Where(kv => kv.Value.State == ToolCallState.Running)
                .ToDictionary(
                    kv => kv.Key,
                    kv => new
                    {
                        toolUseId = kv.Value.ToolUseId,
                        agentId = kv.Value.AgentId,
                        toolName = kv.Value.ToolName,
                        inputSummary = kv.Value.InputSummary,
                        state = kv.Value.State.ToString(),
                        startTime = kv.Value.StartTime
                    }),
            fileActivities = state.FileActivities.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    readCount = kv.Value.ReadCount,
                    writeCount = kv.Value.WriteCount
                })
        };
    }

    private static object SerializeEvent(ActivityEvent evt)
    {
        return new
        {
            type = evt.Type.ToString(),
            sessionId = evt.SessionId,
            agentId = evt.AgentId,
            timestamp = evt.Timestamp,
            data = evt.Data
        };
    }

    #endregion

    private static void LogSparkError(string context, Exception ex)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spark_debug.log");
            var msg = $"[{DateTime.Now:HH:mm:ss.fff}] {context}: {ex.Message}\n{ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(path, msg);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_activityService != null)
            _activityService.ActivityEventProcessed -= OnActivityEvent;
    }
}

public class SparkSessionItem
{
    public string SessionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public bool IsLive { get; set; }
    public DateTime StartTime { get; set; }
}
