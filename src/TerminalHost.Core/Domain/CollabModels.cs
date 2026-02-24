using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// A session participating in MCP collaboration (e.g., "backend", "frontend").
/// </summary>
public class CollabSession
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; set; }

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
}

/// <summary>
/// An exclusive file claim by a session to prevent edit conflicts.
/// </summary>
public class CollabClaim
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("session")]
    public string Session { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("claimedAt")]
    public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A key-value entry in shared memory.
/// </summary>
public class CollabSharedEntry
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("setBy")]
    public string SetBy { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
