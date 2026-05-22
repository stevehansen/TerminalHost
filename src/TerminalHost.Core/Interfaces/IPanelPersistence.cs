using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Persists per-scope panel layout snapshots. Hides the smeared <c>DirectorySettings</c>
/// shape from the router — production adapters project to/from those fields.
/// </summary>
public interface IPanelPersistence
{
    /// <summary>
    /// Loads the most recently saved snapshot for the given scope. Returns an empty snapshot
    /// if no state has been saved yet.
    /// </summary>
    PanelLayoutSnapshot Load(PanelScope scope);

    /// <summary>
    /// Persists the snapshot as the current state for the given scope.
    /// </summary>
    void Save(PanelScope scope, PanelLayoutSnapshot snapshot);
}
