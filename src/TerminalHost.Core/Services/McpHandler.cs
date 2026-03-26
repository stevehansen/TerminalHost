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
                    "create_topic" => ExecuteCreateTopic(args, session),
                    "subscribe" => ExecuteSubscribe(args, session),
                    "unsubscribe" => ExecuteUnsubscribe(args, session),
                    "list_topics" => ExecuteListTopics(),
                    "send_message" => ExecuteSendMessage(args, session),
                    "claim_file" => ExecuteClaimFile(args, session),
                    "release_file" => ExecuteReleaseFile(args, session),
                    "list_claims" => ExecuteListClaims(),
                    "set_shared" => ExecuteSetShared(args, session),
                    "get_shared" => ExecuteGetShared(args),
                    "list_shared" => ExecuteListShared(),
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

    private McpCallToolResult ExecuteCreateTopic(JsonElement args, string session)
    {
        var name = GetString(args, "name");
        if (string.IsNullOrEmpty(name))
            return ErrorResult("Missing required parameter: name");

        var description = GetString(args, "description");
        var (ok, error) = _collab.CreateTopic(session, name, description);
        return ok
            ? TextResult($"Topic '{name}' created. You are subscribed.")
            : ErrorResult(error!);
    }

    private McpCallToolResult ExecuteSubscribe(JsonElement args, string session)
    {
        var topic = GetString(args, "topic");
        if (string.IsNullOrEmpty(topic))
            return ErrorResult("Missing required parameter: topic");

        var (ok, error) = _collab.Subscribe(session, topic);
        return ok
            ? TextResult($"Subscribed to topic '{topic}'.")
            : ErrorResult(error!);
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

        var (ok, error) = _collab.SendMessage(session, topic, content);
        return ok
            ? TextResult($"Message sent to topic '{topic}'.")
            : ErrorResult(error!);
    }

    private async Task<McpCallToolResult> ExecuteReadMessagesAsync(JsonElement args, string session, CancellationToken ct)
    {
        var topic = GetString(args, "topic");
        if (string.IsNullOrEmpty(topic))
            return ErrorResult("Missing required parameter: topic");

        var sinceId = GetInt(args, "since_id");
        var timeoutMs = Math.Clamp(GetInt(args, "timeout"), 0, 300_000);

        var (messages, cursor, error) = await _collab.ReadMessagesAsync(session, topic, sinceId, timeoutMs, ct);
        if (error != null)
            return ErrorResult(error);

        if (messages.Count == 0)
            return TextResult($"No new messages on topic '{topic}'. (cursor: {cursor})");

        var lines = messages.Select(m =>
            $"[{m.CreatedAt:HH:mm:ss}] {m.Sender}: {m.Content}");
        var text = $"{messages.Count} message(s) on '{topic}':\n\n" +
                   string.Join("\n", lines) +
                   $"\n\n(cursor: {cursor})";
        return TextResult(text);
    }

    private McpCallToolResult ExecuteClaimFile(JsonElement args, string session)
    {
        var filePath = GetString(args, "file_path");
        if (string.IsNullOrEmpty(filePath))
            return ErrorResult("Missing required parameter: file_path");

        var description = GetString(args, "description");
        var (ok, error) = _collab.ClaimFile(session, filePath, description);
        return ok
            ? TextResult($"Claimed file '{filePath}'.")
            : ErrorResult(error!);
    }

    private McpCallToolResult ExecuteReleaseFile(JsonElement args, string session)
    {
        var filePath = GetString(args, "file_path");
        if (string.IsNullOrEmpty(filePath))
            return ErrorResult("Missing required parameter: file_path");

        var (ok, error) = _collab.ReleaseFile(session, filePath);
        return ok
            ? TextResult($"Released claim on file '{filePath}'.")
            : ErrorResult(error!);
    }

    private McpCallToolResult ExecuteListClaims()
    {
        var claims = _collab.GetClaims();
        if (claims.Count == 0)
            return TextResult("No active file claims.");

        var lines = claims.Select(c =>
        {
            var desc = !string.IsNullOrEmpty(c.Description) ? $" ({c.Description})" : "";
            return $"- {c.FilePath} → {c.Session}{desc}";
        });
        return TextResult(string.Join("\n", lines));
    }

    private McpCallToolResult ExecuteSetShared(JsonElement args, string session)
    {
        var key = GetString(args, "key");
        if (string.IsNullOrEmpty(key))
            return ErrorResult("Missing required parameter: key");

        var value = GetString(args, "value");
        if (value == null)
            return ErrorResult("Missing required parameter: value");

        _collab.SetShared(key, value, session);
        return TextResult($"Shared key '{key}' set.");
    }

    private McpCallToolResult ExecuteGetShared(JsonElement args)
    {
        var key = GetString(args, "key");
        if (string.IsNullOrEmpty(key))
            return ErrorResult("Missing required parameter: key");

        var entry = _collab.GetShared(key);
        if (entry == null)
            return ErrorResult($"Key '{key}' not found.");

        return TextResult($"Key: {entry.Key}\nSet by: {entry.SetBy} at {entry.UpdatedAt:HH:mm:ss}\n\n{entry.Value}");
    }

    private McpCallToolResult ExecuteListShared()
    {
        var entries = _collab.ListShared();
        if (entries.Count == 0)
            return TextResult("No shared entries.");

        var lines = entries.Select(e =>
            $"- {e.Key} (set by {e.SetBy} at {e.UpdatedAt:HH:mm:ss})");
        return TextResult(string.Join("\n", lines));
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
            Name = "create_topic",
            Description = "Create a collaboration topic and auto-subscribe. Other sessions can then subscribe to exchange messages.",
            InputSchema = Schema(new[] {
                Prop("name", "string", "Topic name (e.g., 'user-api')"),
                Prop("description", "string", "Optional topic description") },
                new[] { "name" })
        },
        new()
        {
            Name = "subscribe",
            Description = "Subscribe to an existing topic to send and receive messages.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic name to subscribe to") },
                new[] { "topic" })
        },
        new()
        {
            Name = "unsubscribe",
            Description = "Unsubscribe from a topic.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic name to unsubscribe from") },
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
            Description = "Send a message to a topic. Must be subscribed to the topic.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic to send to"),
                Prop("content", "string", "Message content") },
                new[] { "topic", "content" })
        },
        new()
        {
            Name = "read_messages",
            Description = "Read messages from a topic. Returns messages since the given cursor. Must be subscribed.",
            InputSchema = Schema(new[] {
                Prop("topic", "string", "Topic to read from"),
                Prop("since_id", "integer", "Read messages after this ID (cursor). Omit or 0 for all."),
                Prop("timeout", "integer", "Max ms to wait for new messages. 0 = return immediately (default).") },
                new[] { "topic" })
        },
        new()
        {
            Name = "claim_file",
            Description = "Claim exclusive work on a file to prevent edit conflicts with other sessions.",
            InputSchema = Schema(new[] {
                Prop("file_path", "string", "File path to claim"),
                Prop("description", "string", "Optional description of planned changes") },
                new[] { "file_path" })
        },
        new()
        {
            Name = "release_file",
            Description = "Release your claim on a file so other sessions can work on it.",
            InputSchema = Schema(new[] {
                Prop("file_path", "string", "File path to release") },
                new[] { "file_path" })
        },
        new()
        {
            Name = "list_claims",
            Description = "List all active file claims across sessions.",
            InputSchema = Schema(Array.Empty<(string, JsonObject)>(), null)
        },
        new()
        {
            Name = "set_shared",
            Description = "Set a key-value pair in shared memory. Use for API contracts, type definitions, or shared state.",
            InputSchema = Schema(new[] {
                Prop("key", "string", "Key name"),
                Prop("value", "string", "Value to store") },
                new[] { "key", "value" })
        },
        new()
        {
            Name = "get_shared",
            Description = "Get a value from shared memory by key.",
            InputSchema = Schema(new[] {
                Prop("key", "string", "Key to retrieve") },
                new[] { "key" })
        },
        new McpToolDefinition
        {
            Name = "list_shared",
            Description = "List all keys in shared memory with metadata.",
            InputSchema = Schema(Array.Empty<(string, JsonObject)>(), null)
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
