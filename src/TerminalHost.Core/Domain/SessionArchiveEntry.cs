using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Lightweight summary of a completed session, persisted to disk.
/// Used to preserve devcontainer sessions that cannot be rediscovered
/// from the host-side session index after the in-memory retention window.
/// </summary>
public class SessionArchiveEntry
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("source")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionSource Source { get; set; }

    [JsonPropertyName("containerName")]
    public string? ContainerName { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    [JsonPropertyName("lifecycle")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionLifecycle Lifecycle { get; set; }

    [JsonPropertyName("totalToolCalls")]
    public int TotalToolCalls { get; set; }

    [JsonPropertyName("totalAgents")]
    public int TotalAgents { get; set; }

    [JsonPropertyName("filesRead")]
    public int FilesRead { get; set; }

    [JsonPropertyName("filesWritten")]
    public int FilesWritten { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("initialPrompt")]
    public string? InitialPrompt { get; set; }

    [JsonPropertyName("archivedAt")]
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates an archive entry from a SessionActivityState.
    /// </summary>
    public static SessionArchiveEntry FromState(SessionActivityState state) => new()
    {
        SessionId = state.SessionId,
        WorkingDirectory = state.WorkingDirectory,
        Source = state.Source,
        ContainerName = state.ContainerName,
        StartTime = state.StartTime,
        EndTime = state.EndTime,
        Lifecycle = state.Lifecycle,
        TotalToolCalls = state.TotalToolCalls,
        TotalAgents = state.TotalAgents,
        FilesRead = state.FilesRead,
        FilesWritten = state.FilesWritten,
        Summary = state.Summary,
        InitialPrompt = state.InitialPrompt,
        ArchivedAt = DateTime.UtcNow,
    };
}
