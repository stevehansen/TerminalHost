using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
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

        // If API server is running, also connect SSE for live updates
        if (IsApiServerRunning)
        {
            PostToCanvas(new
            {
                action = "connectSSE",
                apiBase = ApiBaseUrl,
                sessionId
            });
        }
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

            if (action == "selectSession")
            {
                var sessionId = doc.RootElement.GetProperty("sessionId").GetString();
                if (sessionId != null)
                    OpenSession(sessionId);
            }
            else if (action == "themeChanged")
            {
                var theme = doc.RootElement.GetProperty("theme").GetString();
                if (theme != null && _configService != null)
                {
                    var config = _configService.Load();
                    config.Settings.Timeline.SparkTheme = theme;
                    _configService.Save(config);
                }
            }
        }
        catch { }
    }

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
    }

    [RelayCommand]
    private void SelectSession(string? sessionId)
    {
        if (sessionId != null)
        {
            OpenSession(sessionId);
        }
    }

    #endregion

    #region Event Handling

    private void OnActivityEvent(object? sender, ActivityEvent evt)
    {
        if (CurrentSessionId == null || evt.SessionId != CurrentSessionId) return;
        if (IsApiServerRunning) return; // SSE handles it

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
