using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Port for the Eidet long-term memory service.
/// Hides HTTP transport, connection state machine, retry policy, and JSON
/// serialization; exposes typed memory operations and an observable status.
/// Production adapter wraps the HTTP API; test adapter is in-memory.
/// </summary>
public interface IEidetService
{
    /// <summary>Current connection status snapshot.</summary>
    MemoryStatus Status { get; }

    /// <summary>Fires whenever <see cref="Status"/> transitions.</summary>
    event EventHandler<MemoryStatus>? StatusChanged;

    /// <summary>Whether the service is currently connected to Eidet.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Try to connect to Eidet.
    /// Pass currently-open project paths to trigger auto-intake for restored tabs.
    /// </summary>
    Task<bool> TryConnectAsync(IReadOnlyList<string>? openProjectPaths = null, CancellationToken ct = default);

    /// <summary>Disconnect and release transport resources.</summary>
    void Disconnect();

    /// <summary>Reconcile connection state with the current settings (Enabled, URL).</summary>
    Task OnSettingsChangedAsync(CancellationToken ct = default);

    /// <summary>Notify the service that a project tab was opened. Triggers first-time intake.</summary>
    Task OnProjectOpenedAsync(string projectPath, CancellationToken ct = default);

    /// <summary>Manually trigger intake for a project (command palette / UI button).</summary>
    /// <returns>Human-readable status message suitable for display.</returns>
    Task<string> RunIntakeAsync(string projectPath, CancellationToken ct = default);

    /// <summary>Stats for a repo (counts by type, total, summary text).</summary>
    Task<EidetStatsResponse?> GetStatsAsync(string repoId, CancellationToken ct = default);

    /// <summary>Hybrid search across memories.</summary>
    Task<EidetSearchResponse?> SearchAsync(string repoId, string query, string? type = null, int limit = 100, CancellationToken ct = default);

    /// <summary>Browse all memories in a repo, optionally filtered by type.</summary>
    Task<EidetMemoriesResponse?> BrowseAsync(string repoId, string? type = null, CancellationToken ct = default);

    /// <summary>List memory layers (local + shared + base) for a repo.</summary>
    Task<EidetLayersResponse?> GetLayersAsync(string repoId, CancellationToken ct = default);

    /// <summary>Soft-delete a memory by id.</summary>
    Task<bool> ForgetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Raw GET proxy for ApiServer's thin /api/memory/* endpoints.
    /// Returns (statusCode, body, contentType). Never throws on transport errors —
    /// surfaces them as 502 bodies.
    /// </summary>
    Task<(int StatusCode, string Body, string ContentType)> ProxyGetAsync(string pathAndQuery, CancellationToken ct = default);

    /// <summary>
    /// Probe an arbitrary Eidet URL without altering connection state.
    /// Used by the Settings UI "Test Connection" button.
    /// </summary>
    Task<EidetStatusResponse?> TestConnectionAsync(string url, CancellationToken ct = default);
}
