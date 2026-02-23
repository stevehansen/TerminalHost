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

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private readonly List<SseConnection> _sseConnections = new();
    private readonly object _sseLock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    // Delegate provided by MainViewModel to read tab state on UI thread
    private Func<List<ApiRepoInfo>>? _getRepos;
    private Func<int, ApiRepoDetailInfo?>? _getRepoDetail;

    public bool IsRunning { get; private set; }
    public string? BaseUrl { get; private set; }

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
        ITaskService? taskService = null)
    {
        _configService = configService;
        _dispatcherService = dispatcherService;
        _eventAggregator = eventAggregator;
        _gitStatusService = gitStatusService;
        _timelineService = timelineService;
        _taskService = taskService;
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

    public async Task StartAsync()
    {
        if (IsRunning) return;

        var config = _configService.Load();
        var apiSettings = config.Settings.Api;

        // Validate: require API key for non-loopback
        if (apiSettings.BindAddress != "127.0.0.1" && apiSettings.BindAddress != "localhost"
            && string.IsNullOrEmpty(apiSettings.ApiKey))
        {
            throw new InvalidOperationException("API key is required when binding to a non-loopback address.");
        }

        var prefix = $"http://{apiSettings.BindAddress}:{apiSettings.Port}/";

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            _cts = new CancellationTokenSource();
            BaseUrl = $"http://{(apiSettings.BindAddress == "0.0.0.0" ? "127.0.0.1" : apiSettings.BindAddress)}:{apiSettings.Port}";
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
            var config = _configService.Load();
            var apiSettings = config.Settings.Api;

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

            if (method != "GET")
            {
                await WriteJsonError(response, 405, "METHOD_NOT_ALLOWED", "Only GET requests are supported.");
                return;
            }

            // Route matching
            if (path == "/api/status")
                await HandleStatusAsync(response, config);
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
                await HandleTasksAsync(response);
            else if (path == "/api/timeline")
                await HandleTimelineAsync(response, request);
            else if (path == "/api/config")
                await HandleConfigAsync(response, config);
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
            _dispatcherService.Invoke(() =>
            {
                var repos = _getRepos();
                repoCount = repos.Count;
                activeIndex = repos.FindIndex(r => r.IsActive);
            });
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
            _dispatcherService.Invoke(() => { repos = _getRepos(); });
        }

        await WriteJson(response, new { repos });
    }

    private async Task HandleRepoDetailAsync(HttpListenerResponse response, int index)
    {
        ApiRepoDetailInfo? detail = null;

        if (_getRepoDetail != null)
        {
            _dispatcherService.Invoke(() => { detail = _getRepoDetail(index); });
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
            _dispatcherService.Invoke(() =>
            {
                var repos = _getRepos();
                if (index >= 0 && index < repos.Count)
                    workingDir = repos[index].WorkingDirectory;
            });
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

    private async Task HandleTasksAsync(HttpListenerResponse response)
    {
        var tasks = new List<ApiTaskInfo>();

        if (_taskService != null)
        {
            var allTasks = _taskService.GetAllTasks();
            tasks = allTasks.Select(t => new ApiTaskInfo
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status.ToString(),
                CreatedAt = t.CreatedAt
            }).ToList();
        }

        await WriteJson(response, new { tasks });
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
                Sessions = _timelineService.GetSessionsForIntent(intent.Id)
                    .Select(s => new ApiTimelineSessionInfo
                    {
                        Id = s.Id,
                        Status = s.Status.ToString(),
                        StartedAt = s.StartTime,
                        EndedAt = s.EndTime,
                        CommitHash = s.CommitHash,
                        CommitMessage = s.CommitMessage
                    }).ToList()
            }).ToList();
        }

        await WriteJson(response, new { intents });
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
                    await WriteSseEvent(writer, evt);
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

        var allowed = settings.CorsOrigins.Any(pattern =>
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
            response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Authorization, Content-Type, Last-Event-ID");
            response.Headers.Add("Access-Control-Max-Age", "86400");
        }
    }

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
        StopAsync().GetAwaiter().GetResult();
    }

    private sealed record SseConnection(CancellationTokenSource Cts, Channel<ApiEvent> Channel);
}
