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
/// Hosted as a center panel inside TerminalPairView via WebView2.
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
    /// Available sessions for the picker.
    /// </summary>
    public ObservableCollection<SparkSessionItem> AvailableSessions { get; } = new();

    /// <summary>
    /// Whether the API server is running and accessible.
    /// </summary>
    public bool IsApiServerRunning => _apiServer?.IsRunning ?? false;

    #endregion

    #region Events

    /// <summary>
    /// Raised when the ViewModel needs to send a message to the WebView2 canvas.
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
        {
            ApiBaseUrl = _apiServer.BaseUrl ?? "http://localhost:19280";
        }

        if (_activityService != null)
        {
            _activityService.ActivityEventProcessed += OnActivityEvent;
        }
    }

    /// <summary>
    /// Open the canvas for a specific session.
    /// </summary>
    public void OpenSession(string sessionId)
    {
        CurrentSessionId = sessionId;

        if (!IsCanvasReady) return;

        // Clear previous session data before loading new one
        PostToCanvas(new { action = "clear" });

        // Push state directly via postMessage (works regardless of API server)
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
        // SSE from WebView2's HTTPS virtual host to http://localhost can fail as mixed content.
    }

    /// <summary>
    /// Called by the view when WebView2 canvas reports ready.
    /// </summary>
    public void OnCanvasReady()
    {
        IsCanvasReady = true;

        // Push saved theme to canvas
        var savedTheme = _configService?.Load().Settings.Timeline.SparkTheme ?? "holographic";
        PostToCanvas(new { action = "setTheme", theme = savedTheme });

        RefreshSessions();

        // Auto-connect to first active session if none specified
        if (CurrentSessionId != null)
        {
            OpenSession(CurrentSessionId);
        }
        else
        {
            AutoConnectToActiveSession();
        }
    }

    /// <summary>
    /// Called by the view when a message is received from the WebView2 canvas.
    /// </summary>
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
        RequestOpenJsonlFile?.Invoke(this, EventArgs.Empty);
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

        if (!IsCanvasReady)
            return;

        // Push to canvas with full tool call history + events for feed/transcript
        PostToCanvas(new { action = "clear" });
        PostToCanvas(new
        {
            action = "loadReplay",
            state = SerializeStateForReplay(state),
            events = result.Events.Select(SerializeEvent).ToList()
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
        AvailableSessions.Clear();

        // Get live sessions from TimelineService
        var liveSessions = _timelineService?.GetLiveSessions();
        if (liveSessions != null)
        {
            foreach (var session in liveSessions.Where(s => s.IsActive))
            {
                AvailableSessions.Add(new SparkSessionItem
                {
                    SessionId = session.ClaudeSessionId,
                    DisplayName = session.DisplayName,
                    ProjectPath = session.WorkingDirectory,
                    IsLive = true,
                    StartTime = session.StartTime
                });
            }
        }

        // Get activity states (may include sessions not in live list)
        var activityStates = _activityService?.GetActiveStates();
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

        // Push session list to canvas for the JS picker
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

    private void AutoConnectToActiveSession()
    {
        // Pick the first active live session
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
        {
            OpenSession(sessionId);
        }
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
        // Auto-connect to first session if none selected yet (single-session mode)
        if (!_isMultiMode && CurrentSessionId == null && evt.Type == ActivityEventType.SessionStart)
        {
            OpenSession(evt.SessionId);
            return;
        }

        // In multi-mode, forward events from ALL sessions
        // In single-mode, only forward events for the current session
        if (!_isMultiMode && (CurrentSessionId == null || evt.SessionId != CurrentSessionId)) return;

        // Always forward events via postMessage — SSE may not work from WebView2's
        // HTTPS virtual host context (mixed-content blocks to http://localhost).
        if (IsCanvasReady)
        {
            PostToCanvas(new
            {
                action = "event",
                @event = SerializeEvent(evt)
            });
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

    public void Dispose()
    {
        if (_activityService != null)
        {
            _activityService.ActivityEventProcessed -= OnActivityEvent;
        }
    }
}

/// <summary>
/// Lightweight item for the session picker dropdown.
/// </summary>
public class SparkSessionItem
{
    public string SessionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public bool IsLive { get; set; }
    public DateTime StartTime { get; set; }
}
