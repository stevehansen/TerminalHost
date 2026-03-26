namespace TerminalHost.Core.Domain;

/// <summary>
/// Type of activity event in a session.
/// </summary>
public enum ActivityEventType
{
    // Session lifecycle
    SessionStart,
    SessionEnd,
    SessionTimeout,

    // Agent lifecycle
    AgentSpawn,
    AgentComplete,
    AgentStateChange,
    ModelDetected,

    // Tool activity
    ToolCallStart,
    ToolCallEnd,

    // Messages
    UserMessage,
    AssistantMessage,
    ThinkingBlock,

    // File activity
    FileAccessed
}

/// <summary>
/// Source of an activity event.
/// </summary>
public enum EventSource
{
    /// <summary>From Claude Code hook (real-time push).</summary>
    Hook,

    /// <summary>From JSONL transcript file (batch or file-watcher).</summary>
    Transcript,

    /// <summary>From sessions-index.json metadata.</summary>
    SessionIndex,

    /// <summary>Derived/inferred event (e.g., timeout detection).</summary>
    Inferred
}

/// <summary>
/// Unified event representing something that happened during a session.
/// All data sources (hooks, JSONL, session index) are normalized into this format.
/// </summary>
public class ActivityEvent
{
    /// <summary>
    /// Absolute timestamp of the event.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Type of event.
    /// </summary>
    public ActivityEventType Type { get; set; }

    /// <summary>
    /// Parent session ID.
    /// </summary>
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Agent that produced/received this event (main or subagent).
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Event-specific data payload.
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = [];

    /// <summary>
    /// Where this event came from.
    /// </summary>
    public EventSource Source { get; set; }

    /// <summary>
    /// Gets a typed value from Data.
    /// </summary>
    public T? GetData<T>(string key) where T : class
    {
        return Data.TryGetValue(key, out var value) ? value as T : null;
    }

    /// <summary>
    /// Gets a string value from Data.
    /// </summary>
    public string? GetString(string key)
    {
        return Data.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Gets an int value from Data.
    /// </summary>
    public int GetInt(string key)
    {
        if (Data.TryGetValue(key, out var value))
        {
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    // Factory methods for common event types

    public static ActivityEvent CreateSessionStart(string sessionId, string? cwd, string? transcriptPath, EventSource source = EventSource.Hook)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.SessionStart,
            SessionId = sessionId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["cwd"] = cwd,
                ["transcriptPath"] = transcriptPath
            }
        };
    }

    public static ActivityEvent CreateSessionEnd(string sessionId, string reason, EventSource source = EventSource.Hook)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.SessionEnd,
            SessionId = sessionId,
            Source = source,
            Data = new Dictionary<string, object?> { ["reason"] = reason }
        };
    }

    public static ActivityEvent CreateToolCallStart(string sessionId, string? agentId, string toolUseId, string toolName, string? inputSummary, EventSource source = EventSource.Hook)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.ToolCallStart,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["toolUseId"] = toolUseId,
                ["toolName"] = toolName,
                ["inputSummary"] = inputSummary
            }
        };
    }

    public static ActivityEvent CreateToolCallEnd(string sessionId, string? agentId, string toolUseId, string toolName, string? resultSummary, int tokenCost, string? error, EventSource source = EventSource.Hook)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.ToolCallEnd,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["toolUseId"] = toolUseId,
                ["toolName"] = toolName,
                ["resultSummary"] = resultSummary,
                ["tokenCost"] = tokenCost,
                ["error"] = error
            }
        };
    }

    public static ActivityEvent CreateAgentSpawn(string sessionId, string agentId, string? parentId, string name, bool isMain, string? task, string? model, EventSource source = EventSource.Hook)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.AgentSpawn,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["agentId"] = agentId,
                ["name"] = name,
                ["parentId"] = parentId,
                ["isMain"] = isMain,
                ["task"] = task,
                ["model"] = model
            }
        };
    }

    public static ActivityEvent CreateAgentComplete(string sessionId, string agentId, EventSource source = EventSource.Hook)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.AgentComplete,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?> { ["agentId"] = agentId }
        };
    }

    public static ActivityEvent CreateFileAccessed(string sessionId, string? agentId, string filePath, string accessType, string? toolUseId, EventSource source = EventSource.Hook)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.FileAccessed,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["filePath"] = filePath,
                ["accessType"] = accessType,
                ["toolUseId"] = toolUseId
            }
        };
    }

    public static ActivityEvent CreateUserMessage(string sessionId, string? content, int estimatedTokens, EventSource source = EventSource.Transcript)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.UserMessage,
            SessionId = sessionId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["content"] = content != null && content.Length > 200 ? content[..200] + "..." : content,
                ["estimatedTokens"] = estimatedTokens
            }
        };
    }

    public static ActivityEvent CreateAssistantMessage(string sessionId, string? agentId, string? content, int estimatedTokens, EventSource source = EventSource.Transcript)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.AssistantMessage,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["content"] = content != null && content.Length > 200 ? content[..200] + "..." : content,
                ["estimatedTokens"] = estimatedTokens
            }
        };
    }

    public static ActivityEvent CreateThinkingBlock(string sessionId, string? agentId, string? content, int estimatedTokens, EventSource source = EventSource.Transcript)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.ThinkingBlock,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["content"] = content != null && content.Length > 200 ? content[..200] + "..." : content,
                ["estimatedTokens"] = estimatedTokens
            }
        };
    }

    public static ActivityEvent CreateModelDetected(string sessionId, string agentId, string model, EventSource source = EventSource.Transcript)
    {
        return new ActivityEvent
        {
            Type = ActivityEventType.ModelDetected,
            SessionId = sessionId,
            AgentId = agentId,
            Source = source,
            Data = new Dictionary<string, object?>
            {
                ["agentId"] = agentId,
                ["model"] = model
            }
        };
    }
}
