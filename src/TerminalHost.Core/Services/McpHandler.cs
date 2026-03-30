using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Handles MCP Streamable HTTP protocol (JSON-RPC) for collaboration tools.
/// Routes initialize, tools/list, and tools/call methods.
/// </summary>
public class McpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ICollabService _collab;
    private readonly ITimelineService? _timelineService;
    private readonly object _sessionLock = new();
    private int _nextAutoSessionId;
    // Maps Mcp-Session-Id → session name
    private readonly Dictionary<string, string> _sessionMap = new();

    public McpHandler(ICollabService collab, ITimelineService? timelineService = null)
    {
        _collab = collab;
        _timelineService = timelineService;
    }

    /// <summary>
    /// Result of handling a JSON-RPC request. Includes optional session ID for response header.
    /// </summary>
    public record McpResult(string? ResponseBody, string? McpSessionId);

    /// <summary>
    /// Handles a JSON-RPC request body.
    /// sessionHint: from X-Session header (may be null for global config).
    /// mcpSessionId: from Mcp-Session-Id header (may be null on first request).
    /// Returns response body + session ID to set on response.
    /// </summary>
    public async Task<McpResult> HandleRequestAsync(string jsonBody, string? sessionHint, string? mcpSessionId, CancellationToken ct = default)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(jsonBody, JsonOptions);
        }
        catch
        {
            var err = JsonRpcResponse.ErrorResponse(null, -32700, "Parse error");
            return new McpResult(JsonSerializer.Serialize(err, JsonOptions), null);
        }

        if (request == null)
        {
            var err = JsonRpcResponse.ErrorResponse(null, -32600, "Invalid request");
            return new McpResult(JsonSerializer.Serialize(err, JsonOptions), null);
        }

        // Resolve session name
        string sessionName;
        string? returnSessionId = null;

        if (request.Method == "initialize")
        {
            // Assign a new session — prefer X-Session header, then try roots from params
            var nameHint = sessionHint ?? TryExtractRootsName(request.Params);
            var rootsDir = TryExtractRootsDir(request.Params);
            var newId = AssignSession(nameHint, rootsDir);
            sessionName = _sessionMap[newId];
            returnSessionId = newId;
        }
        else if (!string.IsNullOrEmpty(mcpSessionId) && _sessionMap.ContainsKey(mcpSessionId))
        {
            sessionName = _sessionMap[mcpSessionId];
            returnSessionId = mcpSessionId;
        }
        else if (!string.IsNullOrEmpty(sessionHint))
        {
            sessionName = sessionHint;
        }
        else
        {
            sessionName = "unknown";
        }

        // Register collab session with working directory (for discovery)
        var collabDir = request.Method == "initialize" ? TryExtractRootsDir(request.Params) : null;
        _collab.EnsureSession(sessionName, collabDir);

        JsonRpcResponse? response;
        if (request.Method == "tools/call")
            response = await HandleToolsCallAsync(request, sessionName, ct);
        else
        {
            response = request.Method switch
            {
                "initialize" => HandleInitialize(request, sessionName),
                "notifications/initialized" => null, // notification, no response
                "tools/list" => HandleToolsList(request, sessionName),
                _ => JsonRpcResponse.ErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
            };
        }

        var body = response != null ? JsonSerializer.Serialize(response, JsonOptions) : null;
        return new McpResult(body, returnSessionId);
    }

    private string AssignSession(string? hint, string? rootsDir = null)
    {
        lock (_sessionLock)
        {
            var id = Guid.NewGuid().ToString("N")[..12];
            string name;
            if (!string.IsNullOrEmpty(hint))
            {
                name = hint;
            }
            else
            {
                // Try to derive name from live sessions (hooks know cwd)
                name = TryMatchLiveSession(rootsDir) ?? $"session-{++_nextAutoSessionId}";
            }
            _sessionMap[id] = name;
            return id;
        }
    }

    /// <summary>
    /// Tries to find a live session whose working directory matches the given path
    /// and returns the project folder name. Falls back to null.
    /// </summary>
    private string? TryMatchLiveSession(string? rootsDir)
    {
        if (_timelineService == null) return null;

        var liveSessions = _timelineService.GetLiveSessions();
        if (liveSessions.Count == 0) return null;

        if (string.IsNullOrEmpty(rootsDir)) return null;

        var normalized = rootsDir.Replace('\\', '/').TrimEnd('/');
        // Extract folder name for container path matching (e.g., /workspace/Api → Api)
        var folderName = normalized.Contains('/') ? normalized[(normalized.LastIndexOf('/') + 1)..] : normalized;

        foreach (var ls in liveSessions)
        {
            var lsDir = (ls.WorkingDirectory ?? "").Replace('\\', '/').TrimEnd('/');

            // Exact path match (host paths)
            if (string.Equals(lsDir, normalized, StringComparison.OrdinalIgnoreCase))
                return ls.DisplayName;

            // Container path match: /workspace/X matches a live session whose folder name is X
            if (normalized.StartsWith("/workspace/", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ls.DisplayName, folderName, StringComparison.OrdinalIgnoreCase))
                return ls.DisplayName;
        }

        // Don't guess — wrong matches are worse than "session-N"
        return null;
    }

    /// <summary>
    /// Returns true if the session name was auto-generated (not explicitly set).
    /// </summary>
    private bool IsAutoGeneratedName(string sessionName)
    {
        return sessionName.StartsWith("session-") || sessionName == "unknown";
    }

    /// <summary>
    /// Tries to extract a session name from the MCP initialize params' roots list.
    /// Roots are file:// URIs; we take the last path segment of the first root.
    /// </summary>
    private static string? TryExtractRootsName(JsonElement? initParams)
    {
        if (initParams == null || initParams.Value.ValueKind != JsonValueKind.Object)
            return null;

        // Try params.roots[] — array of { uri: "file:///path/to/project" }
        if (initParams.Value.TryGetProperty("roots", out var roots) && roots.ValueKind == JsonValueKind.Array)
        {
            foreach (var root in roots.EnumerateArray())
            {
                if (root.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String)
                {
                    var name = ExtractDirName(uri.GetString());
                    if (name != null) return name;
                }
                // Some clients send just a string
                if (root.ValueKind == JsonValueKind.String)
                {
                    var name = ExtractDirName(root.GetString());
                    if (name != null) return name;
                }
            }
        }

        // Try params.workspaceFolders[] (VS Code convention)
        if (initParams.Value.TryGetProperty("workspaceFolders", out var folders) && folders.ValueKind == JsonValueKind.Array)
        {
            foreach (var folder in folders.EnumerateArray())
            {
                if (folder.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String)
                {
                    var name = ExtractDirName(uri.GetString());
                    if (name != null) return name;
                }
                if (folder.TryGetProperty("name", out var fname) && fname.ValueKind == JsonValueKind.String)
                {
                    var n = fname.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the directory name from a file URI or path.
    /// "file:///P:/MyProject" → "MyProject", "/home/user/backend" → "backend"
    /// </summary>
    /// <summary>
    /// Tries to extract the full directory path from MCP initialize params' roots list.
    /// Used to match MCP sessions to live sessions via working directory.
    /// </summary>
    private static string? TryExtractRootsDir(JsonElement? initParams)
    {
        if (initParams == null || initParams.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (initParams.Value.TryGetProperty("roots", out var roots) && roots.ValueKind == JsonValueKind.Array)
        {
            foreach (var root in roots.EnumerateArray())
            {
                if (root.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String)
                {
                    var dir = ExtractDirPath(uri.GetString());
                    if (dir != null) return dir;
                }
                if (root.ValueKind == JsonValueKind.String)
                {
                    var dir = ExtractDirPath(root.GetString());
                    if (dir != null) return dir;
                }
            }
        }

        if (initParams.Value.TryGetProperty("workspaceFolders", out var folders) && folders.ValueKind == JsonValueKind.Array)
        {
            foreach (var folder in folders.EnumerateArray())
            {
                if (folder.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String)
                {
                    var dir = ExtractDirPath(uri.GetString());
                    if (dir != null) return dir;
                }
            }
        }

        return null;
    }

    private static string? ExtractDirPath(string? uriOrPath)
    {
        if (string.IsNullOrEmpty(uriOrPath)) return null;
        var path = uriOrPath;
        if (path.StartsWith("file:///")) path = path["file:///".Length..];
        else if (path.StartsWith("file://")) path = path["file://".Length..];
        path = Uri.UnescapeDataString(path).Replace('\\', '/').TrimEnd('/');
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static string? ExtractDirName(string? uriOrPath)
    {
        if (string.IsNullOrEmpty(uriOrPath)) return null;

        var path = uriOrPath;

        // Strip file:// prefix
        if (path.StartsWith("file:///"))
            path = path["file:///".Length..];
        else if (path.StartsWith("file://"))
            path = path["file://".Length..];

        // URI-decode
        path = Uri.UnescapeDataString(path);

        // Normalize separators and trim
        path = path.Replace('\\', '/').TrimEnd('/');

        // Get last segment
        var lastSlash = path.LastIndexOf('/');
        var name = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

        // Clean up (remove drive colon if it's just a drive letter)
        name = name.TrimEnd(':');

        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Renames a session (used by set_session_name tool).
    /// </summary>
    private void RenameSession(string currentName, string newName)
    {
        lock (_sessionLock)
        {
            foreach (var kv in _sessionMap)
            {
                if (kv.Value == currentName)
                {
                    _sessionMap[kv.Key] = newName;
                    break;
                }
            }
        }
        _collab.EnsureSession(newName);
    }

    private static JsonRpcResponse HandleInitialize(JsonRpcRequest request, string sessionName)
    {
        var result = new McpInitializeResult();
        return JsonRpcResponse.Success(request.Id, result);
    }

    private JsonRpcResponse HandleToolsList(JsonRpcRequest request, string session)
    {
        var needsIdentity = IsAutoGeneratedName(session);
        return JsonRpcResponse.Success(request.Id, new McpToolsListResult
        {
            Tools = GetToolDefinitions(needsIdentity)
        });
    }

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest request, string session, CancellationToken ct)
    {
        if (request.Params == null)
            return JsonRpcResponse.ErrorResponse(request.Id, -32602, "Missing params");

        string toolName;
        JsonElement arguments;
        try
        {
            toolName = request.Params.Value.GetProperty("name").GetString()!;
            arguments = request.Params.Value.TryGetProperty("arguments", out var args) ? args : default;
        }
        catch
        {
            return JsonRpcResponse.ErrorResponse(request.Id, -32602, "Invalid params: expected 'name' and optional 'arguments'");
        }

        var result = await ExecuteToolAsync(toolName, arguments, session, ct);
        return JsonRpcResponse.Success(request.Id, result);
    }

    #region Tool Execution

    private async Task<McpCallToolResult> ExecuteToolAsync(string name, JsonElement args, string session, CancellationToken ct)
    {
        try
        {
            McpCallToolResult result;
            if (name == "read_messages")
            {
                result = await ExecuteReadMessagesAsync(args, session, ct);
            }
            else
            {
                result = name switch
                {
                    "set_session_name" => ExecuteSetSessionName(args, session),
                    "subscribe" => ExecuteSubscribe(args, session),
                    "unsubscribe" => ExecuteUnsubscribe(args, session),
                    "list_topics" => ExecuteListTopics(),
                    "send_message" => ExecuteSendMessage(args, session),
                    _ => ErrorResult($"Unknown tool: {name}")
                };
            }

            // Append unread hints to successful results
            if (!result.IsError)
                AppendUnreadHints(result, session);

            return result;
        }
        catch (Exception ex)
        {
            return ErrorResult($"Internal error: {ex.Message}");
        }
    }

    private McpCallToolResult ExecuteSetSessionName(JsonElement args, string currentSession)
    {
        var name = GetString(args, "name");
        if (string.IsNullOrEmpty(name))
            return ErrorResult("Missing required parameter: name");

        var workingDir = GetString(args, "working_dir");
        var projectName = GetString(args, "project_name");

        RenameSession(currentSession, name);

        // Enrich the collab session with identity fields
        var sessions = _collab.GetSessions();
        var session = sessions.FirstOrDefault(s => s.Name == name);
        if (session != null)
        {
            if (!string.IsNullOrEmpty(workingDir)) session.WorkingDir = workingDir;
            if (!string.IsNullOrEmpty(projectName)) session.ProjectName = projectName;
            else if (!string.IsNullOrEmpty(workingDir))
            {
                // Derive project name from working directory
                var folder = workingDir.Replace('\\', '/').TrimEnd('/');
                var lastSlash = folder.LastIndexOf('/');
                session.ProjectName = lastSlash >= 0 ? folder[(lastSlash + 1)..] : folder;
            }
        }

        return TextResult($"Session renamed to '{name}'. Other sessions will see you as '{name}'.");
    }

    private McpCallToolResult ExecuteSubscribe(JsonElement args, string session)
    {
        var topic = GetString(args, "topic");
        if (string.IsNullOrEmpty(topic))
            return ErrorResult("Missing required parameter: topic");

        var description = GetString(args, "description");
        _collab.Subscribe(session, topic, description);
        return TextResult($"Subscribed to topic '{topic}'.");
    }

    private McpCallToolResult ExecuteUnsubscribe(JsonElement args, string session)
    {
        var topic = GetString(args, "topic");
        if (string.IsNullOrEmpty(topic))
            return ErrorResult("Missing required parameter: topic");

        var (ok, error) = _collab.Unsubscribe(session, topic);
        return ok
            ? TextResult($"Unsubscribed from topic '{topic}'.")
            : ErrorResult(error!);
    }

    private McpCallToolResult ExecuteListTopics()
    {
        var topics = _collab.GetTopics();
        if (topics.Count == 0)
            return TextResult("No topics exist yet.");

        var lines = topics.Select(t =>
        {
            var subs = string.Join(", ", t.Subscribers);
            var desc = !string.IsNullOrEmpty(t.Description) ? $" - {t.Description}" : "";
            return $"- {t.Name}{desc} ({t.MessageCount} msgs, subscribers: {subs})";
        });
        return TextResult(string.Join("\n", lines));
    }

    private McpCallToolResult ExecuteSendMessage(JsonElement args, string session)
    {
        var topic = GetString(args, "topic");
        if (string.IsNullOrEmpty(topic))
            return ErrorResult("Missing required parameter: topic");

        var content = GetString(args, "content");
        if (string.IsNullOrEmpty(content))
            return ErrorResult("Missing required parameter: content");

        _collab.SendMessage(session, topic, content);
        return TextResult($"Message sent to topic '{topic}'.");
    }

    private async Task<McpCallToolResult> ExecuteReadMessagesAsync(JsonElement args, string session, CancellationToken ct)
    {
        var topic = GetString(args, "topic");
        if (string.IsNullOrEmpty(topic))
            return ErrorResult("Missing required parameter: topic");

        var sinceId = GetInt(args, "since_id");
        var timeoutMs = Math.Clamp(GetInt(args, "timeout"), 0, 300_000);

        var (messages, cursor) = await _collab.ReadMessagesAsync(session, topic, sinceId, timeoutMs, ct);

        if (messages.Count == 0)
            return TextResult($"No new messages on topic '{topic}'. (cursor: {cursor})");

        var lines = messages.Select(m =>
            $"[{m.CreatedAt:HH:mm:ss}] {m.Sender}: {m.Content}");
        var text = $"{messages.Count} message(s) on '{topic}':\n\n" +
                   string.Join("\n", lines) +
                   $"\n\n(cursor: {cursor})";
        return TextResult(text);
    }


    #endregion

    #region Unread Hints

    private void AppendUnreadHints(McpCallToolResult result, string session)
    {
        var unread = _collab.GetUnreadCounts(session);
        if (unread.Count == 0) return;

        var hints = unread.Select(kv => $"[You have {kv.Value} unread message(s) on topic '{kv.Key}']");
        var hintText = "\n\n" + string.Join("\n", hints);

        if (result.Content.Count > 0 && result.Content[^1].Type == "text")
        {
            result.Content[^1].Text += hintText;
        }
    }

    #endregion

    #region Tool Definitions

    private static List<McpToolDefinition> GetToolDefinitions(bool includeSetSessionName)
    {
        var tools = new List<McpToolDefinition>();

        if (includeSetSessionName)
        {
            tools.Add(new McpToolDefinition
            {
                Name = "set_session_name",
                Description = "IMPORTANT: Call this first before using any other collab tools. Use your project folder name as the session name (e.g., if your cwd is /workspace/Api, use 'Api'). This identifies your session to other collaborators. Also pass working_dir for reliable matching.",
                InputSchema = Schema(new[] {
                    Prop("name", "string", "Session name — use the project/folder name from your working directory (e.g., 'Api', 'cronos', 'frontend')"),
                    Prop("working_dir", "string", "Working directory of this session (optional, helps match to project)"),
                    Prop("project_name", "string", "Project/folder name (optional, derived from working_dir if not set)") },
                    new[] { "name" })
            });
        }

        tools.AddRange(
        [
        new McpToolDefinition
        {
            Name = "subscribe",
            Description = "Join a topic (creates it if it doesn't exist). Idempotent — safe to call multiple times. Use to explicitly join before reading, or to set/update a topic's description.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic name (e.g., 'user-api')"),
                Prop("description", "string", "Optional topic description (sets or updates)") },
                new[] { "topic" })
        },
        new()
        {
            Name = "unsubscribe",
            Description = "Leave a topic. If you're the last subscriber, the topic and its messages are automatically deleted.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic name to leave") },
                new[] { "topic" })
        },
        new()
        {
            Name = "list_topics",
            Description = "List all collaboration topics with their subscribers and message counts.",
            InputSchema = Schema(Array.Empty<(string, JsonObject)>(), null)
        },
        new()
        {
            Name = "send_message",
            Description = "Send a message to a topic. Auto-creates the topic and subscribes you if needed — just send.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic to send to (auto-created if needed)"),
                Prop("content", "string", "Message content") },
                new[] { "topic", "content" })
        },
        new McpToolDefinition
        {
            Name = "read_messages",
            Description = "Read messages from a topic. Auto-subscribes you if needed. Returns messages after the given cursor.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic to read from (auto-created if needed)"),
                Prop("since_id", "integer", "Read messages after this ID (cursor). Omit or 0 for all."),
                Prop("timeout", "integer", "Max ms to wait for new messages. 0 = return immediately (default).") },
                new[] { "topic" })
        }
        ]);

        return tools;
    }

    #endregion

    #region Helpers

    private static McpCallToolResult TextResult(string text) => new()
    {
        Content = [new McpContent { Text = text }]
    };

    private static McpCallToolResult ErrorResult(string text) => new()
    {
        Content = [new McpContent { Text = text }],
        IsError = true
    };

    private static string? GetString(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return null;
        return args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int GetInt(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Undefined || args.ValueKind == JsonValueKind.Null)
            return 0;
        if (args.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt32();
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var v)) return v;
        }
        return 0;
    }

    private static JsonObject Schema(
        (string name, JsonObject prop)[] properties,
        string[]? required)
    {
        var schema = new JsonObject
        {
            ["type"] = "object"
        };

        var props = new JsonObject();
        foreach (var (name, prop) in properties)
            props[name] = prop;
        schema["properties"] = props;

        if (required != null && required.Length > 0)
        {
            var arr = new JsonArray();
            foreach (var r in required) arr.Add(r);
            schema["required"] = arr;
        }

        return schema;
    }

    private static (string name, JsonObject prop) Prop(string name, string type, string description)
    {
        return (name, new JsonObject
        {
            ["type"] = type,
            ["description"] = description
        });
    }

    #endregion
}
