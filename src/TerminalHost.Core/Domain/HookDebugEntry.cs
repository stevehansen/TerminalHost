namespace TerminalHost.Core.Domain;

/// <summary>
/// A single entry in the hook debug log, capturing what arrived at the API
/// and whether it was successfully processed.
/// </summary>
public class HookDebugEntry
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>The hook type from the URL (e.g., "session-start", "tool-end").</summary>
    public string HookType { get; init; } = "";

    /// <summary>Source IP of the request.</summary>
    public string Source { get; init; } = "";

    /// <summary>Claude session ID extracted from the payload (if any).</summary>
    public string? SessionId { get; init; }

    /// <summary>Working directory from the payload (if any).</summary>
    public string? Cwd { get; init; }

    /// <summary>Tool name for tool hooks.</summary>
    public string? ToolName { get; init; }

    /// <summary>Whether the event was successfully parsed and dispatched.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if processing failed.</summary>
    public string? Error { get; init; }

    /// <summary>HTTP status code returned.</summary>
    public int StatusCode { get; init; }

    /// <summary>Truncated raw body (first 2000 chars) for inspection.</summary>
    public string? RawBody { get; init; }

    /// <summary>Number of HookEventReceived subscribers at dispatch time.</summary>
    public int SubscriberCount { get; init; }

    /// <summary>Display summary for the debug list.</summary>
    public string Summary => Success
        ? $"{HookType} — {SessionId?[..Math.Min(8, SessionId?.Length ?? 0)]}… {ToolName ?? ""}"
        : $"{HookType} — FAILED: {Error}";
}
