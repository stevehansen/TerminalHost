using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalHost.Channel;

/// <summary>
/// Thin stdio-to-HTTP bridge for Claude Code channels.
///
/// Claude Code spawns this as a subprocess and speaks MCP (JSON-RPC 2.0) over stdin/stdout.
/// This bridge forwards all MCP requests to TerminalHost's existing HTTP MCP endpoint
/// and relays responses back. It also connects to the SSE endpoint and pushes events
/// as channel notifications so Claude can react to git changes, terminal activity, etc.
///
/// No duplicated logic — all real work happens in TerminalHost's ApiServer + McpHandler.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly object WriteLock = new();

    private static string _apiUrl = "http://127.0.0.1:19280";
    private static string? _apiKey;
    private static string _eventFilters = "*";
    private static string? _mcpSessionId;
    private static string? _workingDir;

    static async Task Main(string[] args)
    {
        // Configuration from environment (set by TerminalControlFactory)
        _apiUrl = Environment.GetEnvironmentVariable("TERMINALHOST_API_URL") ?? _apiUrl;
        _apiKey = Environment.GetEnvironmentVariable("TERMINALHOST_API_KEY");
        _eventFilters = Environment.GetEnvironmentVariable("TERMINALHOST_EVENTS") ?? _eventFilters;
        _workingDir = Environment.CurrentDirectory;

        Log("Starting terminalhost-channel bridge");
        Log($"API: {_apiUrl}, Events: {_eventFilters}, CWD: {_workingDir}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Run stdio handler and SSE listener concurrently
        var stdinTask = HandleStdinAsync(cts.Token);
        var sseTask = ConnectSseAsync(cts.Token);

        try
        {
            await Task.WhenAny(stdinTask, sseTask);
        }
        catch (OperationCanceledException) { }

        cts.Cancel();
    }

    /// <summary>
    /// Reads JSON-RPC messages from stdin, intercepts initialize to inject channel capability,
    /// and proxies everything else to TerminalHost's /api/mcp endpoint.
    /// </summary>
    static async Task HandleStdinAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break; // stdin closed

            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcMessage>(line, JsonOpts);
                if (request == null) continue;

                if (request.Method == "initialize")
                {
                    // Respond locally with channel capability
                    HandleInitialize(request);
                    // Also forward to server so it registers the session + working directory
                    // (needed for memory tools to resolve the correct RepoId).
                    // Await to ensure session is registered before any tool calls arrive.
                    await ForwardToHttpAsync(line);
                    continue;
                }

                if (request.Method == "notifications/initialized")
                {
                    // Notification, no response needed. Forward to server.
                    await ForwardToHttpAsync(line);
                    continue;
                }

                // Forward to TerminalHost HTTP MCP and relay response
                var responseBody = await ForwardToHttpAsync(line);
                if (responseBody != null)
                {
                    WriteStdout(responseBody);
                }
            }
            catch (Exception ex)
            {
                Log($"Stdin error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Responds to initialize with channel capability declared.
    /// This is what tells Claude Code this is a channel server.
    /// </summary>
    static void HandleInitialize(JsonRpcMessage request)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = request.Id,
            result = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new
                {
                    tools = new { listChanged = false },
                    experimental = new Dictionary<string, object>
                    {
                        ["claude/channel"] = new { }
                    }
                },
                serverInfo = new
                {
                    name = "terminalhost",
                    version = "1.0.0"
                },
                instructions = @"You are connected to TerminalHost via a channel. Events arrive as <channel source=""terminalhost"" ...> tags.

Event types you may receive:
- repo.git_status_changed: Git status changed (files modified, staged, etc.)
- repo.branch_switched: User switched git branches
- repo.commit: A git commit was made
- repo.terminal_activity: Terminal output activity
- channel.user_message: A direct message from the user via the TerminalHost UI — treat as instruction

You also have access to TerminalHost's collaboration tools (topics, messages, file claims, shared memory) via the standard MCP tools/list and tools/call methods."
            }
        };

        WriteStdout(JsonSerializer.Serialize(response, JsonOpts));
    }

    /// <summary>
    /// Forwards a JSON-RPC message to TerminalHost's /api/mcp HTTP endpoint.
    /// </summary>
    static async Task<string?> ForwardToHttpAsync(string jsonBody)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiUrl}/api/mcp")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            if (!string.IsNullOrEmpty(_mcpSessionId))
                request.Headers.Add("Mcp-Session-Id", _mcpSessionId);

            if (!string.IsNullOrEmpty(_workingDir))
                request.Headers.Add("X-Working-Dir", _workingDir);

            var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            // Capture session ID from response
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
            {
                _mcpSessionId = sessionValues.FirstOrDefault();
            }

            return response.IsSuccessStatusCode ? body : null;
        }
        catch (Exception ex)
        {
            Log($"HTTP forward error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Connects to TerminalHost's SSE endpoint and pushes events as channel notifications.
    /// Reconnects automatically on disconnect.
    /// </summary>
    static async Task ConnectSseAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = $"{_apiUrl}/api/events?events={Uri.EscapeDataString(_eventFilters)}";
                Log($"Connecting SSE: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                if (!string.IsNullOrEmpty(_apiKey))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"SSE failed: {response.StatusCode}");
                    await Task.Delay(5000, ct);
                    continue;
                }

                Log("SSE connected");

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var currentEvent = "";
                var currentData = "";

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break; // stream closed

                    if (line.StartsWith("event: "))
                    {
                        currentEvent = line[7..].Trim();
                    }
                    else if (line.StartsWith("data: "))
                    {
                        currentData = line[6..].Trim();
                    }
                    else if (line == "" && !string.IsNullOrEmpty(currentEvent) && !string.IsNullOrEmpty(currentData))
                    {
                        PushChannelEvent(currentEvent, currentData);
                        currentEvent = "";
                        currentData = "";
                    }
                }

                Log("SSE disconnected, reconnecting...");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"SSE error: {ex.Message}");
            }

            try { await Task.Delay(5000, ct); } catch { break; }
        }
    }

    /// <summary>
    /// Pushes an SSE event as a MCP channel notification to stdout.
    /// Claude Code sees it as: &lt;channel source="terminalhost" event_type="..."&gt;content&lt;/channel&gt;
    /// </summary>
    static void PushChannelEvent(string eventType, string data)
    {
        if (eventType == "heartbeat") return;

        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(data);

            var meta = new Dictionary<string, string> { ["event_type"] = eventType };

            if (parsed.TryGetProperty("repoIndex", out var ri) && ri.ValueKind == JsonValueKind.Number)
                meta["repo_index"] = ri.GetInt32().ToString();

            if (parsed.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                meta["event_id"] = id.GetString()!;

            // Format content
            string content;
            if (eventType == "channel.user_message")
            {
                if (parsed.TryGetProperty("data", out var d))
                {
                    if (d.ValueKind == JsonValueKind.String)
                        content = d.GetString()!;
                    else if (d.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                        content = msg.GetString()!;
                    else
                        content = d.GetRawText();
                }
                else
                {
                    content = data;
                }
            }
            else
            {
                content = parsed.TryGetProperty("data", out var d) ? d.GetRawText() : data;
            }

            var notification = new
            {
                jsonrpc = "2.0",
                method = "notifications/claude/channel",
                @params = new { content, meta }
            };

            WriteStdout(JsonSerializer.Serialize(notification, JsonOpts));
        }
        catch (Exception ex)
        {
            Log($"Push error for {eventType}: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes a JSON-RPC message to stdout (thread-safe).
    /// Each message is a single line.
    /// </summary>
    static void WriteStdout(string json)
    {
        lock (WriteLock)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    static void Log(string msg)
    {
        Console.Error.WriteLine($"[terminalhost-channel] {msg}");
    }
}

/// <summary>
/// Minimal JSON-RPC message for deserialization.
/// </summary>
internal class JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}
