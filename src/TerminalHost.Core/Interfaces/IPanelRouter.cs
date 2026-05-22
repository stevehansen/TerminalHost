using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Caller-facing facade for opening, moving, and closing panels across all surfaces.
/// Owns single-instance dedupe, toggle semantics, atomic zone transitions, persistence
/// emission, and ESC / dismiss fan-in. Hides the concrete platform surfaces from call sites.
/// </summary>
public interface IPanelRouter
{
    /// <summary>
    /// Resolves a panel view model by type from the configured factory and shows it with default options.
    /// Throws <see cref="InvalidOperationException"/> if no factory was configured or the factory cannot
    /// produce a view model for <typeparamref name="TPanel"/>.
    /// </summary>
    void Show<TPanel>() where TPanel : IPanelableViewModel;

    /// <summary>
    /// Shows the given view model with default options. Zone and scope resolve via
    /// <see cref="IPanelPlacement"/> when implemented, otherwise default to <see cref="PanelZone.Popup"/>
    /// / <see cref="PanelScope.AppShell"/>.
    /// </summary>
    void Show(IPanelableViewModel vm);

    /// <summary>
    /// Shows the given view model. If the same panel id is already open in the same scope,
    /// the call toggles (closes the panel) unless <see cref="PanelShowOptions.ForceShow"/> is set.
    /// Once a panel id is first registered in a scope with a particular
    /// <see cref="PanelShowOptions.AllowMultiInstance"/> value, subsequent <c>Show</c> calls in that
    /// scope must use the same value — mixing modes throws <see cref="InvalidOperationException"/>.
    /// </summary>
    void Show(IPanelableViewModel vm, PanelShowOptions options);

    /// <summary>
    /// Moves an already-open panel to a new zone within its current scope, preserving the
    /// view model instance and updating <c>DisplayState</c> / <c>PreferredSide</c> as appropriate.
    /// When the panel id has multiple open instances (via <see cref="PanelShowOptions.AllowMultiInstance"/>),
    /// this operates on an arbitrary instance — instance-targeted moves are not supported in Phase 0.
    /// If both the new mount and the rollback mount fail, the panel is force-closed and an
    /// <see cref="AggregateException"/> containing both failures is thrown.
    /// </summary>
    void Move(string panelId, PanelZone newZone);

    /// <summary>
    /// Moves an already-open panel to a new zone and, when <paramref name="options"/> is non-null,
    /// refreshes the surface mount options (e.g. <see cref="PanelShowOptions.AlwaysOnTop"/>) from
    /// the new options. When <paramref name="options"/> is null, the original mount options are preserved.
    /// Same multi-instance and double-failure semantics as the single-argument overload.
    /// </summary>
    void Move(string panelId, PanelZone newZone, PanelShowOptions? options);

    /// <summary>
    /// Closes the panel with the given id, if open. No-op when the panel is not open.
    /// When the panel id has multiple open instances (via <see cref="PanelShowOptions.AllowMultiInstance"/>),
    /// this closes an arbitrary instance — instance-targeted closes are not supported in Phase 0.
    /// </summary>
    void Close(string panelId);

    /// <summary>
    /// Closes every panel currently routed to (<paramref name="zone"/>, <paramref name="scope"/>).
    /// Other zones and other scopes are left alone. Intended for ESC fan-in.
    /// </summary>
    void CloseZone(PanelZone zone, PanelScope scope);

    /// <summary>
    /// Returns true if a panel with the given id is currently routed (in any zone, any scope).
    /// </summary>
    bool IsOpen(string panelId);

    /// <summary>
    /// Returns the view model for the panel with the given id if open, otherwise null.
    /// When <see cref="PanelShowOptions.AllowMultiInstance"/> is in use, returns one of the open instances.
    /// </summary>
    IPanelableViewModel? Get(string panelId);

    /// <summary>
    /// Replays the persisted snapshot for the given scope by resolving each open entry's
    /// view model and calling <c>Show</c> with <see cref="PanelShowOptions.ForceShow"/> set.
    /// Entries whose view models cannot be resolved are skipped.
    /// </summary>
    void Restore(PanelScope scope, Func<string, IPanelableViewModel?> resolveVm);

    /// <summary>
    /// Raised whenever a panel is shown, moved, or closed. A null <c>OldZone</c> indicates a newly
    /// opened panel; a null <c>NewZone</c> indicates a closed panel.
    /// </summary>
    event EventHandler<PanelRoutedEventArgs>? Routed;
}
