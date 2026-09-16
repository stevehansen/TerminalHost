using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Opt-in sibling interface for panelable view models that wish to declare their
/// preferred routing target. Not inherited by <see cref="IPanelableViewModel"/> — implement
/// only when a panel has a meaningful default zone or scope.
/// </summary>
public interface IPanelPlacement
{
    /// <summary>The zone the panel should be shown in when no explicit override is supplied.</summary>
    PanelZone PreferredZone { get; }

    /// <summary>
    /// The scope the panel should be shown in when no explicit override is supplied.
    /// Optional — the default implementation returns <see cref="PanelScope.AppShell"/>.
    /// Override only when the panel must be scoped to a specific tab or other non-shell scope.
    /// </summary>
    PanelScope PreferredScope => PanelScope.AppShell;
}
