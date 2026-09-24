using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Read-mostly client of the Parley hub (inter-session pub/sub for AI agents).
/// Hides HTTP transport, JSON, the hub URL, the SSE change feed and its reconnect policy.
/// Never throws for an unreachable or disabled hub: queries return empty lists and
/// <see cref="IsAvailable"/> reports false.
/// </summary>
public interface IParleyService
{
    /// <summary>Whether the hub is enabled in settings and was reachable on the last attempt.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Fires (on a background thread) when hub state may have changed: a message was sent,
    /// topics/sessions changed, or the hub connected/disconnected. Refresh by re-querying.
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// Starts, restarts or stops watching the hub's change feed to match current settings
    /// (Enabled, HubUrl). Idempotent; call at startup and after settings are saved.
    /// </summary>
    void ApplySettings();

    /// <summary>Topics with <see cref="ParleyTopic.SubscriberDetails"/> joined from the hub's sessions.</summary>
    Task<IReadOnlyList<ParleyTopic>> GetTopicsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ParleySession>> GetSessionsAsync(CancellationToken ct = default);

    /// <summary>The most recent <paramref name="count"/> messages across all topics, oldest first.</summary>
    Task<IReadOnlyList<ParleyMessage>> GetRecentMessagesAsync(int count = 30, CancellationToken ct = default);

    /// <summary>
    /// Probes a hub (default: the configured URL) regardless of the Enabled setting.
    /// Returns the hub version, or null when no Parley hub answers.
    /// </summary>
    Task<string?> GetHubVersionAsync(string? hubUrl = null, CancellationToken ct = default);
}
