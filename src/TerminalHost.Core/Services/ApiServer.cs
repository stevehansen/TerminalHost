using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// HttpListener-based HTTP server providing REST API endpoints and SSE streaming.
/// All ViewModel state reads are marshaled to the UI thread via IDispatcherService.
/// </summary>
public class ApiServer : IApiServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IConfigurationService _configService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IEventAggregatorService _eventAggregator;
    private readonly IGitStatusService? _gitStatusService;
    private readonly ITimelineService? _timelineService;
    private readonly ITaskService? _taskService;
    private readonly IClaudeTaskFileService? _claudeTaskFileService;
    private readonly IClaudeTaskDetectionService? _claudeTaskDetectionService;
    private readonly McpHandler? _mcpHandler;
    private readonly IClipboardService? _clipboardService;
    private readonly ISessionActivityService? _sessionActivityService;
    private readonly ISessionArchiveService? _sessionArchiveService;
    private readonly ICollabService? _collabService;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private readonly List<SseConnection> _sseConnections = new();
    private readonly object _sseLock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    /// <summary>Maximum time to wait for the UI thread when handling API requests.</summary>
    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(10);

    // Delegates provided by MainViewModel to read UI state on UI thread
    private Func<List<ApiRepoInfo>>? _getRepos;
    private Func<int, ApiRepoDetailInfo?>? _getRepoDetail;
    private Func<List<ApiWorkspaceInfo>>? _getWorkspaces;

    public bool IsRunning { get; private set; }
    public event EventHandler<HookEvent>? HookEventReceived;
    public string? BaseUrl { get; private set; }

    // Hook debug log — ring buffer of last 200 entries
    private readonly List<HookDebugEntry> _hookDebugLog = new();
    private readonly object _hookDebugLock = new();
    private const int MaxHookDebugEntries = 200;
    public IReadOnlyList<HookDebugEntry> HookDebugLog
    {
        get { lock (_hookDebugLock) return _hookDebugLog.ToList(); }
    }

    // Cached config to avoid loading 145KB JSON on every HTTP request.
    // Refreshed when user changes settings.
    private ApiSettings _cachedApiSettings = new();
    private AppConfiguration _cachedConfig = new();

    /// <summary>
    /// Refreshes cached settings. Call after the user changes settings.
    /// </summary>
    public void RefreshCachedSettings()
    {
        _cachedConfig = _configService.Load();
        _cachedApiSettings = _cachedConfig.Settings.Api;
    }

    public int ActiveSseConnections
    {
        get { lock (_sseLock) return _sseConnections.Count; }
    }

    public ApiServer(
        IConfigurationService configService,
        IDispatcherService dispatcherService,
        IEventAggregatorService eventAggregator,
        IGitStatusService? gitStatusService = null,
        ITimelineService? timelineService = null,
        ITaskService? taskService = null,
        IClaudeTaskFileService? claudeTaskFileService = null,
        IClaudeTaskDetectionService? claudeTaskDetectionService = null,
        McpHandler? mcpHandler = null,
        IClipboardService? clipboardService = null,
        ISessionActivityService? sessionActivityService = null,
        ISessionArchiveService? sessionArchiveService = null,
        ICollabService? collabService = null)
    {
        _configService = configService;
        _dispatcherService = dispatcherService;
        _eventAggregator = eventAggregator;
        _gitStatusService = gitStatusService;
        _timelineService = timelineService;
        _taskService = taskService;
        _claudeTaskFileService = claudeTaskFileService;
        _claudeTaskDetectionService = claudeTaskDetectionService;
        _mcpHandler = mcpHandler;
        _clipboardService = clipboardService;
        _sessionActivityService = sessionActivityService;
        _sessionArchiveService = sessionArchiveService;
        _collabService = collabService;
    }

    /// <summary>
    /// Sets the delegate used to read repo/tab state from the UI thread.
    /// Must be called before StartAsync.
    /// </summary>
    public void SetRepoStateProvider(Func<List<ApiRepoInfo>> getRepos, Func<int, ApiRepoDetailInfo?> getRepoDetail)
    {
        _getRepos = getRepos;
        _getRepoDetail = getRepoDetail;
    }

    /// <summary>
    /// Sets the delegate used to read workspace state from the UI thread.
    /// Must be called before StartAsync.
    /// </summary>
    public void SetWorkspaceStateProvider(Func<List<ApiWorkspaceInfo>> getWorkspaces)
    {
        _getWorkspaces = getWorkspaces;
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        var config = _configService.Load();
        var apiSettings = config.Settings.Api;
        _cachedApiSettings = apiSettings;

        // Validate: require API key for non-loopback
        if (apiSettings.BindAddress != "127.0.0.1" && apiSettings.BindAddress != "localhost"
            && string.IsNullOrEmpty(apiSettings.ApiKey))
        {
            throw new InvalidOperationException("API key is required when binding to a non-loopback address.");
        }

        var port = apiSettings.Port;

        try
        {
            _listener = new HttpListener();

            // On macOS, the managed HttpListener matches the Host header against the prefix host.
            // If we bind to "127.0.0.1" but the client connects via "localhost", the Host header
            // won't match and HttpListener returns its own 404 HTML. Add both loopback variants.
            if (apiSettings.BindAddress is "127.0.0.1" or "localhost")
            {
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Prefixes.Add($"http://localhost:{port}/");
            }
            else
            {
                _listener.Prefixes.Add($"http://{apiSettings.BindAddress}:{port}/");
            }

            _listener.Start();

            _cts = new CancellationTokenSource();
            BaseUrl = $"http://{(apiSettings.BindAddress == "0.0.0.0" ? "127.0.0.1" : apiSettings.BindAddress)}:{port}";
            IsRunning = true;

            _listenerTask = Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (HttpListenerException)
        {
            _listener?.Close();
            _listener = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts?.Cancel();

        // Close all SSE connections
        lock (_sseLock)
        {
            foreach (var conn in _sseConnections)
                conn.Cts.Cancel();
            _sseConnections.Clear();
        }

        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;

        if (_listenerTask != null)
        {
            try { await _listenerTask; } catch { }
        }

        BaseUrl = null;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Ignore individual request errors
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var apiSettings = _cachedApiSettings;

            // CORS headers
            SetCorsHeaders(response, request, apiSettings);

            // Handle preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            // Auth check
            if (!IsAuthorized(request, apiSettings))
            {
                await WriteJsonError(response, 401, "UNAUTHORIZED", "Missing or invalid API key.");
                return;
            }

            // Route dispatch
            var path = request.Url?.AbsolutePath ?? "/";
            var method = request.HttpMethod;

            // MCP endpoint — POST for JSON-RPC, GET returns 405 per MCP Streamable HTTP spec
            if (path == "/api/mcp")
            {
                if (method == "POST")
                {
                    await HandleMcpAsync(request, response, ct);
                    return;
                }
                // GET /api/mcp is allowed by spec to return 405 if server doesn't offer SSE on this endpoint
                response.StatusCode = 405;
                response.Headers.Add("Allow", "POST");
                response.Close();
                return;
            }

            // Channel endpoints — POST for pushing messages/replies into the event system
            if (path == "/api/channel/message" && method == "POST")
            {
                await HandleChannelMessageAsync(request, response);
                return;
            }
            if (path == "/api/channel/reply" && method == "POST")
            {
                await HandleChannelReplyAsync(request, response);
                return;
            }

            // Clipboard bridge — enables containers to read/write host clipboard
            if (path == "/api/clipboard/text" && method == "GET")
            {
                await HandleClipboardGetTextAsync(response);
                return;
            }
            if (path == "/api/clipboard/text" && method == "POST")
            {
                await HandleClipboardSetTextAsync(request, response);
                return;
            }
            if (path == "/api/clipboard/image" && method == "GET")
            {
                await HandleClipboardGetImageAsync(response);
                return;
            }
            if (path == "/api/clipboard/image" && method == "POST")
            {
                await HandleClipboardSetImageAsync(request, response);
                return;
            }
            if (path.StartsWith("/api/hooks/") && method == "POST")
            {
                await HandleHookAsync(response, request, path["/api/hooks/".Length..]);
                return;
            }

            if (method != "GET")
            {
                await WriteJsonError(response, 405, "METHOD_NOT_ALLOWED", "Only GET requests are supported.");
                return;
            }

            // Route matching
            if (path == "/api/status")
                await HandleStatusAsync(response, _cachedConfig);
            else if (path == "/api/repos")
                await HandleReposAsync(response);
            else if (path.StartsWith("/api/repos/") && TryParseRepoIndex(path, "/api/repos/", out var idx, out var subPath))
            {
                if (string.IsNullOrEmpty(subPath))
                    await HandleRepoDetailAsync(response, idx);
                else if (subPath == "/git")
                    await HandleRepoGitAsync(response, idx);
                else if (subPath == "/links")
                    await HandleRepoLinksAsync(response, idx);
                else
                    await WriteJsonError(response, 404, "NOT_FOUND", $"Unknown endpoint: {path}");
            }
            else if (path == "/api/tasks")
                await HandleTasksAsync(response, request);
            else if (path.StartsWith("/api/tasks/"))
                await HandleTaskByIdAsync(response, path["/api/tasks/".Length..]);
            else if (path == "/api/workspaces")
                await HandleWorkspacesAsync(response, request);
            else if (path == "/api/timeline")
                await HandleTimelineAsync(response, request);
            else if (path == "/api/config")
                await HandleConfigAsync(response, _cachedConfig);
            else if (path.StartsWith("/api/sessions/") && path.EndsWith("/state"))
                await HandleSessionStateAsync(response, path);
            else if (path == "/api/sessions")
                await HandleActiveSessionsAsync(response);
            else if (path == "/api/devcontainer/setup")
                await HandleDevcontainerSetupAsync(response);
            else if (path == "/api/collab/topics")
                await HandleCollabTopicsAsync(response);
            else if (path == "/api/collab/sessions")
                await HandleCollabSessionsAsync(response);
            else if (path == "/api/events")
            {
                if (apiSettings.EnableSse)
                    await HandleSseAsync(context, request, ct);
                else
                    await WriteJsonError(response, 403, "SSE_DISABLED", "SSE streaming is not enabled.");
            }
            else
                await WriteJsonError(response, 404, "NOT_FOUND", $"Unknown endpoint: {path}");
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonError(response, 500, "INTERNAL_ERROR", ex.Message);
            }
            catch { }
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private bool TryParseRepoIndex(string fullPath, string prefix, out int index, out string subPath)
    {
        index = -1;
        subPath = "";

        var remaining = fullPath[prefix.Length..];
        var slashPos = remaining.IndexOf('/');
        var indexStr = slashPos >= 0 ? remaining[..slashPos] : remaining;
        subPath = slashPos >= 0 ? remaining[slashPos..] : "";

        return int.TryParse(indexStr, out index);
    }

    #region Endpoint Handlers

    private async Task HandleStatusAsync(HttpListenerResponse response, AppConfiguration config)
    {
        var repoCount = 0;
        var activeIndex = -1;

        if (_getRepos != null)
        {
            if (!_dispatcherService.TryInvoke(() =>
            {
                var repos = _getRepos();
                repoCount = repos.Count;
                activeIndex = repos.FindIndex(r => r.IsActive);
            }, UiTimeout))
            {
                await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
                return;
            }
        }

        var uptime = DateTime.UtcNow - _startTime;
        var status = new Dictionary<string, object?>
        {
            ["version"] = "1.0.0",
            ["uptime"] = uptime.ToString(@"hh\:mm\:ss"),
            ["uptimeSeconds"] = (int)uptime.TotalSeconds,
            ["tabCount"] = repoCount,
            ["activeTabIndex"] = activeIndex,
            ["touchMode"] = config.Settings.TouchMode,
            ["platform"] = "Windows",
            ["apiVersion"] = "1"
        };

        await WriteJson(response, status);
    }

    private async Task HandleReposAsync(HttpListenerResponse response)
    {
        List<ApiRepoInfo> repos = new();

        if (_getRepos != null)
        {
            if (!_dispatcherService.TryInvoke(() => { repos = _getRepos(); }, UiTimeout))
            {
                await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
                return;
            }
        }

        await WriteJson(response, new { repos });
    }

    private async Task HandleRepoDetailAsync(HttpListenerResponse response, int index)
    {
        ApiRepoDetailInfo? detail = null;

        if (_getRepoDetail != null)
        {
            if (!_dispatcherService.TryInvoke(() => { detail = _getRepoDetail(index); }, UiTimeout))
            {
                await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
                return;
            }
        }

        if (detail == null)
        {
            await WriteJsonError(response, 404, "NOT_FOUND", $"Repo index {index} not found.");
            return;
        }

        await WriteJson(response, detail);
    }

    private async Task HandleRepoGitAsync(HttpListenerResponse response, int index)
    {
        // Get working directory from repo state
        string? workingDir = null;
        if (_getRepos != null)
        {
            if (!_dispatcherService.TryInvoke(() =>
            {
                var repos = _getRepos();
                if (index >= 0 && index < repos.Count)
                    workingDir = repos[index].WorkingDirectory;
            }, UiTimeout))
            {
                await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
                return;
            }
        }

        if (workingDir == null)
        {
            await WriteJsonError(response, 404, "NOT_FOUND", $"Repo index {index} not found.");
            return;
        }

        try
        {
            var gitStatus = await _gitStatusService!.GetGitStatusAsync(workingDir);
            var files = await _gitStatusService.GetModifiedFilesAsync(workingDir);
            var commits = await _gitStatusService.GetCommitHistoryAsync(workingDir, count: 10);

            var result = new ApiGitDetailInfo
            {
                Branch = gitStatus.BranchName,
                IsDirty = gitStatus.IsDirty,
                Ahead = gitStatus.AheadCount,
                Behind = gitStatus.BehindCount,
                StashCount = gitStatus.StashCount,
                ChangedFiles = files.Count,
                StagedFiles = files.Count(f => f.IsStaged),
                UntrackedFiles = files.Count(f => f.Status == GitFileStatusType.Untracked),
                Files = files.Select(f => new ApiGitFileInfo
                {
                    Path = f.FilePath,
                    Status = f.Status.ToString(),
                    IsStaged = f.IsStaged,
                    OldPath = f.OriginalPath
                }).ToList(),
                RecentCommits = commits.Select(c => new ApiGitCommitInfo
                {
                    Hash = c.ShortHash,
                    Message = c.Subject,
                    Author = c.AuthorName,
                    Date = c.CommitDate.UtcDateTime
                }).ToList()
            };

            await WriteJson(response, result);
        }
        catch (Exception ex)
        {
            await WriteJsonError(response, 500, "INTERNAL_ERROR", $"Git status failed: {ex.Message}");
        }
    }

    private async Task HandleRepoLinksAsync(HttpListenerResponse response, int index)
    {
        // Links are not easily accessible from Core - return empty for now
        // MainViewModel can provide them via the state delegate in the future
        await WriteJson(response, new { links = Array.Empty<object>() });
    }

    private async Task HandleTasksAsync(HttpListenerResponse response, HttpListenerRequest request)
    {
        var allTasks = GetMergedTasks();
        if (allTasks == null)
        {
            await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
            return;
        }

        // Apply query parameter filters
        var statusFilter = request.QueryString["status"];
        if (!string.IsNullOrEmpty(statusFilter))
            allTasks = allTasks.Where(t => t.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var sourceFilter = request.QueryString["source"];
        if (!string.IsNullOrEmpty(sourceFilter))
        {
            if (sourceFilter.Equals("claude", StringComparison.OrdinalIgnoreCase))
                allTasks = allTasks.Where(t => t.Claude != null).ToList();
            else if (sourceFilter.Equals("manual", StringComparison.OrdinalIgnoreCase))
                allTasks = allTasks.Where(t => t.Claude == null).ToList();
        }

        var repoFilter = request.QueryString["repo"];
        if (!string.IsNullOrEmpty(repoFilter) && int.TryParse(repoFilter, out var repoIdx))
            allTasks = allTasks.Where(t => t.RepoIndex == repoIdx).ToList();

        await WriteJson(response, new { tasks = allTasks });
    }

    private async Task HandleTaskByIdAsync(HttpListenerResponse response, string taskId)
    {
        var allTasks = GetMergedTasks();
        if (allTasks == null)
        {
            await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
            return;
        }
        var task = allTasks.FirstOrDefault(t => t.Id == taskId);

        if (task == null)
        {
            await WriteJsonError(response, 404, "NOT_FOUND", $"Task '{taskId}' not found.");
            return;
        }

        await WriteJson(response, task);
    }

    private async Task HandleWorkspacesAsync(HttpListenerResponse response, HttpListenerRequest request)
    {
        var workspaces = new List<ApiWorkspaceInfo>();

        if (_getWorkspaces != null)
        {
            if (!_dispatcherService.TryInvoke(() => { workspaces = _getWorkspaces(); }, UiTimeout))
            {
                await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
                return;
            }
        }
        else
        {
            // Fallback: read workspaces directly from config
            var config = _configService.Load();
            List<ApiRepoInfo> openRepos = new();
            if (_getRepos != null)
            {
                if (!_dispatcherService.TryInvoke(() => { openRepos = _getRepos(); }, UiTimeout))
                {
                    await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
                    return;
                }
            }

            workspaces = config.Workspaces.Select(w => MapWorkspace(w, openRepos)).ToList();
        }

        var sectionFilter = request.QueryString["section"];
        if (!string.IsNullOrEmpty(sectionFilter))
            workspaces = workspaces.Where(w => w.Section.Equals(sectionFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        await WriteJson(response, new { workspaces });
    }

    private async Task HandleTimelineAsync(HttpListenerResponse response, HttpListenerRequest request)
    {
        var intents = new List<ApiTimelineIntentInfo>();

        if (_timelineService != null)
        {
            var allIntents = _timelineService.GetAllIntents();
            var limitStr = request.QueryString["limit"];
            var limit = int.TryParse(limitStr, out var l) ? l : 50;

            intents = allIntents.Take(limit).Select(intent => new ApiTimelineIntentInfo
            {
                Id = intent.Id,
                Name = intent.Name,
                Status = intent.Status.ToString(),
                BranchName = intent.BranchName,
                RepoPath = intent.MainRepoPath,
                CreatedAt = intent.CreatedAt,
                Sessions = _timelineService.GetLiveSessions()
                    .Where(s => s.IntentId == intent.Id)
                    .Select(s => new ApiTimelineSessionInfo
                    {
                        Id = s.ClaudeSessionId,
                        Status = s.IsActive ? "Running" : "Completed",
                        StartedAt = s.StartTime,
                        EndedAt = s.EndTime,
                    }).ToList()
            }).ToList();
        }

        await WriteJson(response, new { intents });
    }

    private async Task HandleSessionStateAsync(HttpListenerResponse response, string path)
    {
        // Extract session ID from /api/sessions/{id}/state
        var parts = path.Split('/');
        // parts: ["", "api", "sessions", "{id}", "state"]
        if (parts.Length < 5)
        {
            await WriteJsonError(response, 400, "BAD_REQUEST", "Invalid session state path.");
            return;
        }

        var sessionId = parts[3];

        if (_sessionActivityService == null)
        {
            await WriteJsonError(response, 503, "SERVICE_UNAVAILABLE", "Session activity service not available.");
            return;
        }

        var state = _sessionActivityService.GetState(sessionId);
        if (state == null)
        {
            await WriteJsonError(response, 404, "NOT_FOUND", $"Session {sessionId} not found.");
            return;
        }

        var result = new
        {
            sessionId = state.SessionId,
            workingDirectory = state.WorkingDirectory,
            transcriptPath = state.TranscriptPath,
            startTime = state.StartTime,
            endTime = state.EndTime,
            lastActivityTime = state.LastActivityTime,
            lifecycle = state.Lifecycle.ToString(),
            initialPrompt = state.InitialPrompt,
            summary = state.Summary,
            gitBranch = state.GitBranch,
            source = state.Source.ToString(),
            containerName = state.ContainerName,
            totalToolCalls = state.TotalToolCalls,
            totalAgents = state.TotalAgents,
            filesRead = state.FilesRead,
            filesWritten = state.FilesWritten,
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
                        startTime = kv.Value.StartTime,
                        endTime = kv.Value.EndTime
                    }),
            fileActivities = state.FileActivities.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    filePath = kv.Value.FilePath,
                    readCount = kv.Value.ReadCount,
                    writeCount = kv.Value.WriteCount,
                    searchHitCount = kv.Value.SearchHitCount
                })
        };

        await WriteJson(response, result);
    }

    private async Task HandleActiveSessionsAsync(HttpListenerResponse response)
    {
        var sessions = new List<object>();
        var seenIds = new HashSet<string>();

        // Primary source: live sessions from TimelineService (most reliable for active sessions)
        var liveSessions = _timelineService?.GetLiveSessions();
        if (liveSessions != null)
        {
            foreach (var s in liveSessions)
            {
                if (!seenIds.Add(s.ClaudeSessionId)) continue;
                sessions.Add(new
                {
                    sessionId = s.ClaudeSessionId,
                    workingDirectory = s.WorkingDirectory,
                    lifecycle = s.IsActive ? "Active" : "Completed",
                    startTime = s.StartTime,
                    lastActivityTime = s.LastActivityTime,
                    totalAgents = 0,
                    totalToolCalls = 0,
                    summary = (string?)null,
                    gitBranch = (string?)null,
                    initialPrompt = (string?)null,
                    source = s.Source.ToString(),
                    containerName = s.ContainerName
                });
            }
        }

        // Enrich with activity service data (has richer stats)
        var states = _sessionActivityService?.GetActiveStates();
        if (states != null)
        {
            foreach (var s in states)
            {
                if (!seenIds.Add(s.SessionId)) continue;
                sessions.Add(new
                {
                    sessionId = s.SessionId,
                    workingDirectory = s.WorkingDirectory,
                    lifecycle = s.Lifecycle.ToString(),
                    startTime = s.StartTime,
                    lastActivityTime = s.LastActivityTime,
                    totalAgents = s.TotalAgents,
                    totalToolCalls = s.TotalToolCalls,
                    summary = s.Summary,
                    gitBranch = s.GitBranch,
                    initialPrompt = s.InitialPrompt,
                    source = s.Source.ToString(),
                    containerName = s.ContainerName
                });
            }
        }

        // Include recently archived devcontainer sessions (can't be rediscovered from host file system)
        var archived = _sessionArchiveService?.GetArchivedSessions(TimeSpan.FromHours(24));
        if (archived != null)
        {
            foreach (var a in archived)
            {
                if (!seenIds.Add(a.SessionId)) continue;
                sessions.Add(new
                {
                    sessionId = a.SessionId,
                    workingDirectory = a.WorkingDirectory,
                    lifecycle = a.Lifecycle.ToString(),
                    startTime = a.StartTime,
                    lastActivityTime = a.EndTime,
                    totalAgents = a.TotalAgents,
                    totalToolCalls = a.TotalToolCalls,
                    summary = a.Summary,
                    gitBranch = (string?)null,
                    initialPrompt = a.InitialPrompt,
                    source = a.Source.ToString(),
                    containerName = a.ContainerName
                });
            }
        }

        await WriteJson(response, new { sessions });
    }

    private async Task HandleCollabTopicsAsync(HttpListenerResponse response)
    {
        if (_collabService == null)
        {
            await WriteJsonError(response, 404, "NOT_AVAILABLE", "Collab service not available");
            return;
        }

        var collabSessions = _collabService.GetSessions();
        var topics = _collabService.GetTopics().Select(t => new
        {
            name = t.Name,
            description = t.Description,
            subscribers = t.Subscribers.ToList(),
            // Enriched subscriber info with identity fields
            subscriberDetails = t.Subscribers.Select(subName =>
            {
                var cs = collabSessions.FirstOrDefault(s => s.Name == subName);
                return new
                {
                    name = subName,
                    claudeSessionId = cs?.ClaudeSessionId,
                    projectName = cs?.ProjectName,
                    workingDir = cs?.WorkingDir
                };
            }).ToList(),
            createdBy = t.CreatedBy,
            messageCount = t.MessageCount,
            createdAt = t.CreatedAt
        });
        await WriteJson(response, new { topics });
    }

    private async Task HandleCollabSessionsAsync(HttpListenerResponse response)
    {
        if (_collabService == null)
        {
            await WriteJsonError(response, 404, "NOT_AVAILABLE", "Collab service not available");
            return;
        }

        var sessions = _collabService.GetSessions().Select(s => new
        {
            name = s.Name,
            workingDir = s.WorkingDir,
            claudeSessionId = s.ClaudeSessionId,
            projectName = s.ProjectName,
            lastSeen = s.LastSeen
        });
        await WriteJson(response, new { sessions });
    }

    private async Task HandleConfigAsync(HttpListenerResponse response, AppConfiguration config)
    {
        // Return a redacted view of configuration
        var result = new Dictionary<string, object?>
        {
            ["settings"] = new
            {
                customCommandName = config.Settings.CustomCommandName,
                shellCommandName = config.Settings.ShellCommandName,
                touchMode = config.Settings.TouchMode,
                confirmOnClose = config.Settings.ConfirmOnClose
            },
            ["quickCommands"] = config.QuickCommands.Select(qc => new
            {
                id = qc.Id,
                label = qc.Label,
                icon = qc.Icon,
                shortcut = qc.Shortcut
            }),
            ["aiAssistants"] = config.AiAssistants.Select(ai => new
            {
                id = ai.Id,
                name = ai.Name,
                icon = ai.Icon
            })
        };

        await WriteJson(response, result);
    }

    #endregion

    #region Task & Workspace Helpers

    /// <summary>
    /// Merges tasks from all three sources (ITaskService, IClaudeTaskFileService, IClaudeTaskDetectionService)
    /// with deduplication by task ID, and maps to API DTOs with repo index resolution.
    /// </summary>
    private List<ApiTaskInfo>? GetMergedTasks()
    {
        var seen = new HashSet<string>();
        var result = new List<FocusTask>();

        // 1. Manual tasks from ITaskService
        if (_taskService != null)
        {
            foreach (var task in _taskService.GetAllTasks())
            {
                var key = task.ClaudeTaskId ?? task.Id;
                if (seen.Add(key))
                    result.Add(task);
            }
        }

        // 2. Claude tasks from file service (~/.claude/tasks/)
        if (_claudeTaskFileService != null)
        {
            foreach (var task in _claudeTaskFileService.GetAllTasks())
            {
                var key = task.ClaudeTaskId ?? task.Id;
                if (seen.Add(key))
                    result.Add(task);
            }
        }

        // 3. Claude tasks from terminal detection
        if (_claudeTaskDetectionService != null)
        {
            foreach (var task in _claudeTaskDetectionService.GetAllClaudeTasks())
            {
                var key = task.ClaudeTaskId ?? task.Id;
                if (seen.Add(key))
                    result.Add(task);
            }
        }

        // Resolve repo indices from open tabs
        List<ApiRepoInfo> openRepos = new();
        if (_getRepos != null)
        {
            if (!_dispatcherService.TryInvoke(() => { openRepos = _getRepos(); }, UiTimeout))
                return null; // UI thread timed out — caller should return 503
        }

        return result.Select(t => MapTask(t, openRepos)).ToList();
    }

    private static ApiTaskInfo MapTask(FocusTask task, List<ApiRepoInfo> openRepos)
    {
        // Resolve repoIndex by matching task's project paths to open tab working directories
        int? repoIndex = null;
        if (task.ProjectPaths.Count > 0)
        {
            var normalizedPaths = task.ProjectPaths.Select(NormalizePathForComparison).ToHashSet();
            var match = openRepos.FirstOrDefault(r => normalizedPaths.Contains(NormalizePathForComparison(r.WorkingDirectory)));
            if (match != null)
                repoIndex = match.Index;
        }

        var dto = new ApiTaskInfo
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status.ToString(),
            Priority = task.Priority,
            Tags = task.Tags.ToList(),
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt,
            ElapsedTime = task.ElapsedTime.HasValue ? task.ElapsedTimeDisplay : null,
            RepoIndex = repoIndex,
            ProjectPaths = task.ProjectPaths.ToList(),
            ParentTaskId = task.ParentTaskId,
            Blocks = task.Blocks.ToList(),
            BlockedBy = task.BlockedBy.ToList(),
            IsBlocked = task.IsBlocked,
            LinkedBranch = task.LinkedBranch,
            LinkedPrNumber = task.LinkedPrNumber,
            LinkedPrUrl = task.LinkedPrUrl,
        };

        if (task.IsClaudeTask)
        {
            dto.Claude = new ApiClaudeTaskInfo
            {
                SessionId = task.ClaudeSessionId,
                ClaudeTaskId = task.ClaudeTaskId,
                ActiveForm = task.ActiveForm,
            };
        }

        return dto;
    }

    private static ApiWorkspaceInfo MapWorkspace(Workspace workspace, List<ApiRepoInfo> openRepos)
    {
        var normalizedPath = NormalizePathForComparison(workspace.Path);
        var matchingRepo = openRepos.FirstOrDefault(r => NormalizePathForComparison(r.WorkingDirectory) == normalizedPath);

        return new ApiWorkspaceInfo
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Path = workspace.Path,
            PathId = NormalizePathId(workspace.Path),
            Section = workspace.Section,
            IsPinned = workspace.IsPinned,
            Order = workspace.Order,
            CustomIcon = workspace.CustomIcon,
            IsOpen = matchingRepo != null,
            RepoIndex = matchingRepo?.Index,
            ActivityIndicator = matchingRepo?.ActivityIndicator,
            Terminals = matchingRepo?.Terminals,
        };
    }

    /// <summary>
    /// Normalizes a filesystem path for case-insensitive comparison.
    /// </summary>
    private static string NormalizePathForComparison(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>
    /// Creates a stable, URL-safe identifier from a filesystem path.
    /// Lowercased, backslashes replaced with forward slashes, trimmed, then
    /// path separators replaced with dashes and drive colons removed.
    /// Example: "P:\TerminalHost" → "p-terminalhost"
    /// </summary>
    public static string NormalizePathId(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var normalized = path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        // Remove drive letter colon (e.g., "p:" → "p")
        normalized = normalized.Replace(":", "");
        // Replace slashes with dashes
        normalized = normalized.Replace('/', '-');
        // Collapse multiple dashes
        while (normalized.Contains("--"))
            normalized = normalized.Replace("--", "-");
        // Trim leading/trailing dashes
        return normalized.Trim('-');
    }

    #endregion

    #region MCP Handler

    private async Task HandleMcpAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        if (_mcpHandler == null)
        {
            await WriteJsonError(response, 501, "NOT_IMPLEMENTED", "MCP handler is not configured.");
            return;
        }

        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync();
        }

        var sessionHint = request.Headers["X-Session"]; // null if not provided (global config)
        var mcpSessionId = request.Headers["Mcp-Session-Id"]; // null on first request
        var result = await _mcpHandler.HandleRequestAsync(body, sessionHint, mcpSessionId, ct);

        // Set Mcp-Session-Id header if assigned
        if (!string.IsNullOrEmpty(result.McpSessionId))
        {
            response.Headers.Add("Mcp-Session-Id", result.McpSessionId);
        }

        if (result.ResponseBody == null)
        {
            // Notification — no response body
            response.StatusCode = 202;
            response.Close();
            return;
        }

        response.ContentType = "application/json";
        response.StatusCode = 200;
        var bytes = Encoding.UTF8.GetBytes(result.ResponseBody);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    #endregion

    #region Channel Endpoints

    /// <summary>
    /// Handles POST /api/channel/message — pushes a user message into the event system
    /// so the channel server can forward it to Claude Code as a channel notification.
    /// </summary>
    private async Task HandleChannelMessageAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var payload = JsonSerializer.Deserialize<ChannelMessagePayload>(body, JsonOptions);

            if (payload == null || string.IsNullOrWhiteSpace(payload.Message))
            {
                await WriteJsonError(response, 400, "BAD_REQUEST", "Missing 'message' field.");
                return;
            }

            // Publish as a channel.user_message event so the SSE/channel server picks it up
            _eventAggregator.Publish(new ApiEvent
            {
                Type = "channel.user_message",
                RepoIndex = payload.RepoIndex,
                Data = new { message = payload.Message, sender = "user" }
            });

            await WriteJson(response, new { ok = true, message = "Message published" });
        }
        catch (JsonException)
        {
            await WriteJsonError(response, 400, "BAD_REQUEST", "Invalid JSON body.");
        }
    }

    /// <summary>
    /// Handles POST /api/channel/reply — receives replies from the channel server
    /// (Claude's responses) and publishes them as events for UI display.
    /// </summary>
    private async Task HandleChannelReplyAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var payload = JsonSerializer.Deserialize<ChannelReplyPayload>(body, JsonOptions);

            if (payload == null || string.IsNullOrWhiteSpace(payload.Text))
            {
                await WriteJsonError(response, 400, "BAD_REQUEST", "Missing 'text' field.");
                return;
            }

            // Publish as a channel.reply event for the UI to display (e.g., as a toast)
            _eventAggregator.Publish(new ApiEvent
            {
                Type = "channel.reply",
                Data = new { text = payload.Text, type = payload.Type ?? "info" }
            });

            await WriteJson(response, new { ok = true });
        }
        catch (JsonException)
        {
            await WriteJsonError(response, 400, "BAD_REQUEST", "Invalid JSON body.");
        }
    }

    private class ChannelMessagePayload
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("repoIndex")]
        public int? RepoIndex { get; set; }
    }

    private class ChannelReplyPayload
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    #endregion

    #region Devcontainer Setup

    private async Task HandleDevcontainerSetupAsync(HttpListenerResponse response)
    {
        var port = _cachedApiSettings.Port;
        var script = DevcontainerSetupScript.Replace("{PORT}", port.ToString());
        response.StatusCode = 200;
        response.ContentType = "text/plain";
        var bytes = System.Text.Encoding.UTF8.GetBytes(script);
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    // Hook name mapping: Claude Code event name → host.exe --hook argument → API route.
    // Must match ApiServer.HandleHookAsync switch and TimelineService.InstallHooks().
    //   SessionStart      → session-start
    //   Stop              → session-stop
    //   SessionEnd        → session-end
    //   PreToolUse        → tool-start
    //   PostToolUse       → tool-end
    //   PostToolUseFailure→ tool-error
    //   SubagentStart     → subagent-start
    //   SubagentStop      → subagent-stop
    //   Notification      → notification
    private const string DevcontainerSetupScript = """
#!/bin/bash
# TerminalHost devcontainer setup — installs hook proxy and configures Claude Code.
# Usage: curl -sf http://host.docker.internal:{PORT}/api/devcontainer/setup | bash
set -e

API_URL="http://host.docker.internal:{PORT}"
CONTAINER_NAME="${TERMINALHOST_DEVCONTAINER_NAME:-$(hostname)}"

echo "=== TerminalHost Devcontainer Setup ==="
echo "API endpoint: $API_URL"
echo "Container:    $CONTAINER_NAME"

# 1. Install host.exe proxy
cat > /usr/local/bin/host.exe << 'PROXY_EOF'
#!/bin/bash
API_URL="${TERMINALHOST_API:-http://host.docker.internal:{PORT}}"
CONTAINER_NAME="${TERMINALHOST_DEVCONTAINER_NAME:-$(hostname)}"
if [ "$1" = "--hook" ] && [ -n "$2" ]; then
    PAYLOAD=$(cat)
    curl -s -X POST \
        -H "Content-Type: application/json" \
        -H "X-TerminalHost-Source: devcontainer" \
        -H "X-TerminalHost-Container: ${CONTAINER_NAME}" \
        -d "$PAYLOAD" \
        "$API_URL/api/hooks/$2" > /dev/null 2>&1
    exit 0
fi
echo "TerminalHost devcontainer proxy | API: $API_URL | Container: $CONTAINER_NAME"
PROXY_EOF
chmod +x /usr/local/bin/host.exe
echo "[OK] Installed /usr/local/bin/host.exe"

# 2. Set environment variable
echo "export TERMINALHOST_API=\"$API_URL\"" >> ~/.bashrc
echo "export TERMINALHOST_DEVCONTAINER_NAME=\"$CONTAINER_NAME\"" >> ~/.bashrc
export TERMINALHOST_API="$API_URL"
echo "[OK] Set TERMINALHOST_API in ~/.bashrc"

# 3. Register Claude Code hooks in ~/.claude/settings.json
# Uses the same nested format as TerminalHost's InstallHooks():
#   "EventName": [{"hooks": [{"type": "command", "command": "...", "timeout": N, "async": true}]}]
CLAUDE_DIR="$HOME/.claude"
SETTINGS_FILE="$CLAUDE_DIR/settings.json"
mkdir -p "$CLAUDE_DIR"

# Build the hooks JSON — merge into existing settings if present
# Shared merge logic — identical for python3 and node, just different runtimes.
# The HOOKS_JSON variable holds the hook definitions to merge.
HOOKS_JSON='{
  "SessionStart":       [{"hooks": [{"type":"command","command":"host.exe --hook session-start","timeout":10,"async":true}]}],
  "Stop":               [{"hooks": [{"type":"command","command":"host.exe --hook session-stop","timeout":10,"async":true}]}],
  "SessionEnd":         [{"hooks": [{"type":"command","command":"host.exe --hook session-end","timeout":10,"async":true}]}],
  "PreToolUse":         [{"hooks": [{"type":"command","command":"host.exe --hook tool-start","timeout":5,"async":true}]}],
  "PostToolUse":        [{"hooks": [{"type":"command","command":"host.exe --hook tool-end","timeout":5,"async":true}]}],
  "PostToolUseFailure": [{"hooks": [{"type":"command","command":"host.exe --hook tool-error","timeout":5,"async":true}]}],
  "SubagentStart":      [{"hooks": [{"type":"command","command":"host.exe --hook subagent-start","timeout":5,"async":true}]}],
  "SubagentStop":       [{"hooks": [{"type":"command","command":"host.exe --hook subagent-stop","timeout":5,"async":true}]}],
  "Notification":       [{"hooks": [{"type":"command","command":"host.exe --hook notification","timeout":5,"async":true}]}]
}'

merge_hooks() {
    # Merges HOOKS_JSON into existing settings.json, preserving non-hook settings.
    # Only overwrites a hook event if it doesn't already contain a host.exe hook.
    # $1 = runtime ("python3" or "node")
    if [ "$1" = "python3" ]; then
        python3 - "$SETTINGS_FILE" "$HOOKS_JSON" << 'PYEOF'
import json, sys
settings_file, hooks_json = sys.argv[1], sys.argv[2]
settings = {}
try:
    with open(settings_file, "r") as f:
        settings = json.load(f)
except (FileNotFoundError, json.JSONDecodeError):
    pass
hooks = settings.setdefault("hooks", {})
new_hooks = json.loads(hooks_json)
for event_name, entry in new_hooks.items():
    existing = hooks.get(event_name, [])
    has_host_hook = any(
        any(h.get("command", "").startswith("host.exe --hook") for h in item.get("hooks", []))
        for item in existing if isinstance(item, dict)
    )
    if not has_host_hook:
        hooks[event_name] = entry
with open(settings_file, "w") as f:
    json.dump(settings, f, indent=2)
PYEOF
    elif [ "$1" = "node" ]; then
        node -e "
const fs = require('fs');
const [,, settingsFile, hooksJson] = process.argv;
let settings = {};
try { settings = JSON.parse(fs.readFileSync(settingsFile, 'utf8')); } catch {}
const hooks = settings.hooks = settings.hooks || {};
const newHooks = JSON.parse(hooksJson);
for (const [eventName, entry] of Object.entries(newHooks)) {
    const existing = hooks[eventName] || [];
    const hasHostHook = existing.some(item =>
        (item.hooks || []).some(h => (h.command || '').startsWith('host.exe --hook'))
    );
    if (!hasHostHook) hooks[eventName] = entry;
}
fs.writeFileSync(settingsFile, JSON.stringify(settings, null, 2));
" "$SETTINGS_FILE" "$HOOKS_JSON"
    fi
}

if command -v python3 &> /dev/null; then
    merge_hooks python3
    echo "[OK] Configured Claude Code hooks in $SETTINGS_FILE (merged via python3)"
elif command -v node &> /dev/null; then
    merge_hooks node
    echo "[OK] Configured Claude Code hooks in $SETTINGS_FILE (merged via node)"
else
    # No python or node — write hooks directly (overwrites any existing settings)
    echo "{\"hooks\": $HOOKS_JSON}" | if command -v jq &> /dev/null; then jq .; else cat; fi > "$SETTINGS_FILE"
    echo "[OK] Configured Claude Code hooks in $SETTINGS_FILE (fresh — no python3/node for merge)"
fi

echo "=== Setup complete! Restart Claude Code to activate hooks. ==="
""";

    #endregion

    #region Hook Endpoint (Container Proxy)

    private async Task HandleHookAsync(HttpListenerResponse response, HttpListenerRequest request, string hookType)
    {
        var source = request.RemoteEndPoint?.ToString() ?? "unknown";
        string? body = null;

        try
        {
            using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                AddHookDebugEntry(hookType, source, null, null, null, false, "EMPTY_BODY", 400, body);
                await WriteJsonError(response, 400, "EMPTY_BODY", "No hook payload provided.");
                return;
            }

            var hookData = HookEventData.Parse(body);
            if (hookData == null)
            {
                AddHookDebugEntry(hookType, source, null, null, null, false, "PARSE_ERROR", 400, body);
                await WriteJsonError(response, 400, "PARSE_ERROR", "Failed to parse hook payload.");
                return;
            }

            // Allow HTTP headers to set source (devcontainer proxy uses this instead of JSON mutation)
            var sourceHeader = request.Headers["X-TerminalHost-Source"];
            if (!string.IsNullOrEmpty(sourceHeader) && string.IsNullOrEmpty(hookData.Source))
                hookData.Source = sourceHeader;

            var containerHeader = request.Headers["X-TerminalHost-Container"];
            if (!string.IsNullOrEmpty(containerHeader) && string.IsNullOrEmpty(hookData.DevcontainerName))
                hookData.DevcontainerName = containerHeader;

            HookEvent? hookEvent = hookType switch
            {
                "session-start" => HookEvent.CreateSessionStart(hookData),
                "session-stop" => HookEvent.CreateSessionStop(hookData),
                "session-end" => HookEvent.CreateSessionEnd(hookData),
                "tool-start" => HookEvent.CreateToolStart(hookData),
                "tool-end" => HookEvent.CreateToolEnd(hookData),
                "tool-error" => HookEvent.CreateToolError(hookData),
                "subagent-start" => HookEvent.CreateSubagentStart(hookData),
                "subagent-stop" => HookEvent.CreateSubagentStop(hookData),
                "notification" => HookEvent.CreateNotification(hookData),
                "file-changed" => hookData.IsFileModificationTool() ? HookEvent.CreateFileChanged(hookData) : null,
                _ => null
            };

            if (hookEvent == null)
            {
                AddHookDebugEntry(hookType, source, hookData.SessionId, hookData.Cwd, hookData.ToolName, false, $"UNKNOWN_HOOK: {hookType}", 400, body);
                await WriteJsonError(response, 400, "UNKNOWN_HOOK", $"Unknown hook type: {hookType}");
                return;
            }

            // Count subscribers before firing
            var subscriberCount = HookEventReceived?.GetInvocationList().Length ?? 0;

            // Fire event for App.xaml.cs to route through the standard pipeline
            HookEventReceived?.Invoke(this, hookEvent);

            AddHookDebugEntry(hookType, source, hookEvent.SessionId, hookEvent.Cwd, hookEvent.ToolName, true, null, 200, body, subscriberCount);

            response.StatusCode = 200;
            response.ContentType = "application/json";
            var okBytes = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");
            await response.OutputStream.WriteAsync(okBytes);
            response.Close();
        }
        catch (Exception ex)
        {
            AddHookDebugEntry(hookType, source, null, null, null, false, ex.Message, 500, body);
            await WriteJsonError(response, 500, "HOOK_ERROR", ex.Message);
        }
    }

    /// <summary>
    /// Adds a debug entry for hooks received via named pipe (non-API path).
    /// </summary>
    public void AddPipeHookDebugEntry(HookEvent hookEvent)
    {
        AddHookDebugEntry(
            hookEvent.EventType.ToString(),
            "named-pipe",
            hookEvent.SessionId,
            hookEvent.Cwd,
            hookEvent.ToolName,
            true,
            null,
            0,
            null,
            1);
    }

    private void AddHookDebugEntry(string hookType, string source, string? sessionId, string? cwd, string? toolName, bool success, string? error, int statusCode, string? rawBody, int subscriberCount = 0)
    {
        var entry = new HookDebugEntry
        {
            HookType = hookType,
            Source = source,
            SessionId = sessionId,
            Cwd = cwd,
            ToolName = toolName,
            Success = success,
            Error = error,
            StatusCode = statusCode,
            RawBody = rawBody?.Length > 2000 ? rawBody[..2000] + "…" : rawBody,
            SubscriberCount = subscriberCount
        };

        lock (_hookDebugLock)
        {
            _hookDebugLog.Add(entry);
            while (_hookDebugLog.Count > MaxHookDebugEntries)
                _hookDebugLog.RemoveAt(0);
        }
    }

    #endregion

    #region SSE Streaming

    private async Task HandleSseAsync(HttpListenerContext httpContext, HttpListenerRequest request, CancellationToken serverCt)
    {
        const int maxConnections = 10;

        lock (_sseLock)
        {
            if (_sseConnections.Count >= maxConnections)
            {
                httpContext.Response.StatusCode = 429;
                httpContext.Response.Close();
                return;
            }
        }

        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        var eventFilter = request.QueryString["events"];
        var lastEventId = request.Headers["Last-Event-ID"];

        var connCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        var channel = Channel.CreateBounded<ApiEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var conn = new SseConnection(connCts, channel);
        lock (_sseLock) _sseConnections.Add(conn);

        // Subscribe to events
        var subscription = _eventAggregator.Subscribe(evt =>
        {
            channel.Writer.TryWrite(evt);
        }, eventFilter);

        try
        {
            var writer = new StreamWriter(response.OutputStream, Encoding.UTF8, leaveOpen: true);

            // Send welcome comment
            await writer.WriteLineAsync(": connected to TerminalHost event stream");
            await writer.WriteLineAsync();
            await writer.FlushAsync();

            // Replay missed events if Last-Event-ID provided
            if (!string.IsNullOrEmpty(lastEventId))
            {
                var recentEvents = _eventAggregator.RecentEvents;
                var found = false;
                foreach (var evt in recentEvents)
                {
                    if (found)
                    {
                        await WriteSseEvent(writer, evt);
                    }
                    if (evt.Id == lastEventId) found = true;
                }
            }

            // Stream events with heartbeat
            var heartbeatInterval = TimeSpan.FromSeconds(30);
            using var heartbeatTimer = new PeriodicTimer(heartbeatInterval);

            while (!connCts.Token.IsCancellationRequested)
            {
                var readTask = channel.Reader.ReadAsync(connCts.Token).AsTask();
                var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(connCts.Token).AsTask();

                var completedTask = await Task.WhenAny(readTask, heartbeatTask);

                if (completedTask == readTask)
                {
                    var evt = await readTask;
                    try
                    {
                        await WriteSseEvent(writer, evt);
                    }
                    catch (IOException) { throw; } // Client gone — rethrow to exit loop
                    catch (Exception ex)
                    {
                        // Serialization or write error on a single event — skip it, don't kill the connection
                        System.Diagnostics.Debug.WriteLine($"SSE event write failed: {ex.Message}");
                    }
                }
                else
                {
                    // Heartbeat
                    await writer.WriteLineAsync(": heartbeat");
                    await writer.WriteLineAsync();
                    await writer.FlushAsync();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { } // Client disconnected
        catch { }
        finally
        {
            subscription.Dispose();
            lock (_sseLock) _sseConnections.Remove(conn);
        }
    }

    private static async Task WriteSseEvent(StreamWriter writer, ApiEvent evt)
    {
        await writer.WriteLineAsync($"event: {evt.Type}");
        await writer.WriteLineAsync($"id: {evt.Id}");
        await writer.WriteLineAsync($"data: {JsonSerializer.Serialize(evt, JsonOptions)}");
        await writer.WriteLineAsync();
        await writer.FlushAsync();
    }

    #endregion

    #region Helpers

    private static bool IsAuthorized(HttpListenerRequest request, ApiSettings settings)
    {
        // Loopback doesn't require auth
        if (settings.BindAddress == "127.0.0.1" || settings.BindAddress == "localhost")
            return true;

        if (string.IsNullOrEmpty(settings.ApiKey))
            return true;

        // Check Authorization header
        var authHeader = request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var key = authHeader["Bearer ".Length..];
            if (key == settings.ApiKey) return true;
        }

        // Check query parameter
        var queryKey = request.QueryString["key"];
        if (!string.IsNullOrEmpty(queryKey) && queryKey == settings.ApiKey)
            return true;

        return false;
    }

    private static void SetCorsHeaders(HttpListenerResponse response, HttpListenerRequest request, ApiSettings settings)
    {
        var origin = request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin)) return;

        // Always allow our own WebView2 virtual hosts (Spark Canvas, Markdown viewer)
        var isInternalOrigin = origin.StartsWith("https://spark.local", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://localmd.files", StringComparison.OrdinalIgnoreCase);

        var allowed = isInternalOrigin || settings.CorsOrigins.Any(pattern =>
        {
            if (pattern == "*") return true;
            if (pattern.Contains('*'))
            {
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(origin, regex);
            }
            return origin == pattern;
        });

        if (allowed)
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Authorization, Content-Type, Last-Event-ID, X-Session, Mcp-Session-Id");
            response.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id");
            response.Headers.Add("Access-Control-Max-Age", "86400");
        }
    }

    #region Clipboard Handlers

    private async Task HandleClipboardGetTextAsync(HttpListenerResponse response)
    {
        if (_clipboardService == null)
        {
            await WriteJsonError(response, 503, "UNAVAILABLE", "Clipboard service not available.");
            return;
        }

        string? text = null;
        if (!_dispatcherService.TryInvoke(() => { text = _clipboardService.GetTextAsync().GetAwaiter().GetResult(); }, UiTimeout))
        {
            await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
            return;
        }
        await WriteJson(response, new { text });
    }

    private async Task HandleClipboardSetTextAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (_clipboardService == null)
        {
            await WriteJsonError(response, 503, "UNAVAILABLE", "Clipboard service not available.");
            return;
        }

        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<ClipboardTextPayload>(body, JsonOptions);
        if (payload?.Text == null)
        {
            await WriteJsonError(response, 400, "BAD_REQUEST", "Missing 'text' field.");
            return;
        }

        if (!_dispatcherService.TryInvoke(() => { _clipboardService.SetTextAsync(payload.Text).GetAwaiter().GetResult(); }, UiTimeout))
        {
            await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
            return;
        }
        await WriteJson(response, new { ok = true });
    }

    private async Task HandleClipboardGetImageAsync(HttpListenerResponse response)
    {
        if (_clipboardService == null)
        {
            await WriteJsonError(response, 503, "UNAVAILABLE", "Clipboard service not available.");
            return;
        }

        byte[]? png = null;
        if (!_dispatcherService.TryInvoke(() => { png = _clipboardService.GetImagePngAsync().GetAwaiter().GetResult(); }, UiTimeout))
        {
            await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
            return;
        }

        if (png == null)
        {
            response.StatusCode = 204;
            return;
        }

        // Return raw PNG binary — shim scripts pipe this directly
        response.ContentType = "image/png";
        response.StatusCode = 200;
        response.ContentLength64 = png.Length;
        await response.OutputStream.WriteAsync(png);
    }

    private async Task HandleClipboardSetImageAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (_clipboardService == null)
        {
            await WriteJsonError(response, 503, "UNAVAILABLE", "Clipboard service not available.");
            return;
        }

        using var ms = new MemoryStream();
        await request.InputStream.CopyToAsync(ms);
        var png = ms.ToArray();
        if (png.Length == 0)
        {
            await WriteJsonError(response, 400, "BAD_REQUEST", "Empty request body.");
            return;
        }

        if (!_dispatcherService.TryInvoke(() => { _clipboardService.SetImagePngAsync(png).GetAwaiter().GetResult(); }, UiTimeout))
        {
            await WriteJsonError(response, 503, "UI_BUSY", "UI thread did not respond in time.");
            return;
        }
        await WriteJson(response, new { ok = true });
    }

    private record ClipboardTextPayload(string? Text);

    #endregion

    private static async Task WriteJson(HttpListenerResponse response, object data)
    {
        response.ContentType = "application/json";
        response.StatusCode = 200;
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    private static async Task WriteJsonError(HttpListenerResponse response, int statusCode, string code, string message)
    {
        response.ContentType = "application/json";
        response.StatusCode = statusCode;
        var error = new ApiErrorResponse
        {
            Error = new ApiErrorDetail { Code = code, Message = message }
        };
        var json = JsonSerializer.Serialize(error, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    #endregion

    public void Dispose()
    {
        // Don't use StopAsync().GetAwaiter().GetResult() here — that deadlocks when
        // called on the UI thread because in-flight request handlers may be blocked
        // inside _dispatcherService.Invoke() waiting for the same UI thread.
        // Instead, cancel and close synchronously; the listen loop and any in-flight
        // requests will abort via OperationCanceledException / ObjectDisposedException.
        IsRunning = false;
        _cts?.Cancel();

        lock (_sseLock)
        {
            foreach (var conn in _sseConnections)
                conn.Cts.Cancel();
            _sseConnections.Clear();
        }

        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        BaseUrl = null;
    }

    private sealed record SseConnection(CancellationTokenSource Cts, Channel<ApiEvent> Channel);
}
