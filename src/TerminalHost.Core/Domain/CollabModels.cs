using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// A session participating in MCP collaboration (e.g., "backend", "frontend").
/// Enriched with identity fields for reliable matching to Claude Code sessions.
/// </summary>
public class CollabSession
{
    /// <summary>Free-text name chosen by the agent (e.g., "backend", "api-service").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Working directory of the Claude Code session (from hooks or set_session_name).</summary>
    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; set; }

    /// <summary>Claude Code's session ID (from hooks correlation). Primary key for canvas matching.</summary>
    [JsonPropertyName("claudeSessionId")]
    public string? ClaudeSessionId { get; set; }

    /// <summary>Project folder name derived from working directory.</summary>
    [JsonPropertyName("projectName")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("lastSeen")]
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A named topic that sessions can subscribe to for messaging.
/// </summary>
public class CollabTopic
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("subscribers")]
    public HashSet<string> Subscribers { get; set; } = new();

    /// <summary>
    /// Tracks whether any session has subscribed since this topic was loaded.
    /// Prevents auto-delete on restart before sessions have a chance to reconnect.
    /// </summary>
    [JsonIgnore]
    public bool HasHadSubscriber { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }
}

/// <summary>
/// A message sent to a topic.
/// </summary>
public class CollabMessage
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = "";

    [JsonPropertyName("sender")]
    public string Sender { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Transient flag — true when message arrived after the last UI refresh.
    /// Used for bubble-up entrance animation.
    /// </summary>
    [JsonIgnore]
    public bool IsNew { get; set; }
}

/// <summary>
/// Persisted topic data (excludes transient subscribers).
/// </summary>
public class PersistedTopic
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Root object persisted to collab-state.json.
/// </summary>
public class CollabPersistedState
{
    [JsonPropertyName("nextMessageId")]
    public int NextMessageId { get; set; }

    [JsonPropertyName("topics")]
    public List<PersistedTopic> Topics { get; set; } = new();

    [JsonPropertyName("messages")]
    public List<CollabMessage> Messages { get; set; } = new();

    [JsonPropertyName("cursors")]
    public Dictionary<string, Dictionary<string, int>> Cursors { get; set; } = new();
}

