namespace TerminalHost.Core.Domain;

/// <summary>
/// Identifies the persistence/instance scope of a panel routing operation.
/// <see cref="AppShell"/> means the panel is global to the application; <see cref="ForTab"/>
/// scopes the panel to a single tab so per-tab panels do not interfere with each other.
/// </summary>
public readonly record struct PanelScope(string? TabId)
{
    /// <summary>The global, application-level scope.</summary>
    public static readonly PanelScope AppShell = new((string?)null);

    /// <summary>Creates a scope bound to a specific tab identifier.</summary>
    public static PanelScope ForTab(string tabId) => new(tabId);
}
