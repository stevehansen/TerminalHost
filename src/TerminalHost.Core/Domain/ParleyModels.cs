using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

// DTOs for the Parley hub HTTP API (GET /api/topics, /api/sessions, /api/messages).
// Parley is a standalone app (github.com/stevehansen/parley); these mirror its camelCase JSON.

/// <summary>An agent session known to the Parley hub (usually one per Claude Code session).</summary>
public class ParleySession
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; set; }

    [JsonPropertyName("projectName")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("lastSeen")]
    public DateTime LastSeen { get; set; }

    /// <summary>Open push streams held by the session's shim.</summary>
    [JsonPropertyName("listeners")]
    public int Listeners { get; set; }

    /// <summary>Whether the session is connected (messages reach it without polling).</summary>
    [JsonIgnore]
    public bool IsConnected => Listeners > 0;
}

/// <summary>A subscriber of a topic, joined with its session info.</summary>
public class ParleySubscriber
{
    public string Name { get; set; } = "";
    public string? ProjectName { get; set; }
    public string? WorkingDir { get; set; }
    public bool IsConnected { get; set; }
}

/// <summary>A named topic on the Parley hub.</summary>
public class ParleyTopic
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("subscribers")]
    public List<string> Subscribers { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }

    /// <summary>
    /// <see cref="Subscribers"/> joined with the hub's sessions (connected state, project).
    /// Filled by <see cref="Interfaces.IParleyService.GetTopicsAsync"/>; not part of the hub JSON.
    /// </summary>
    [JsonIgnore]
    public List<ParleySubscriber> SubscriberDetails { get; set; } = new();
}

/// <summary>A message sent to a Parley topic.</summary>
public class ParleyMessage
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
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UI-only flag: true when the message arrived after the previous UI refresh
    /// (drives the bubble-up entrance animation). Never set by the hub.
    /// </summary>
    [JsonIgnore]
    public bool IsNew { get; set; }
}
