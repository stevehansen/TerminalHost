using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Platform-shim port representing a single (zone, scope) surface where panels can be mounted.
/// Implementations adapt platform primitives (Window, Popup, ContentControl, dock host) to a
/// uniform mount/unmount/focus/dismiss protocol.
/// </summary>
public interface IPanelSurface
{
    /// <summary>The zone this surface represents.</summary>
    PanelZone Zone { get; }

    /// <summary>The scope this surface is bound to.</summary>
    PanelScope Scope { get; }

    /// <summary>
    /// Mounts the given view model on this surface using the provided options.
    /// </summary>
    void Mount(IPanelableViewModel vm, PanelMountOptions options);

    /// <summary>
    /// Unmounts the panel with the given id, if present.
    /// </summary>
    void Unmount(string panelId);

    /// <summary>
    /// Brings the panel with the given id to the foreground / gives it input focus.
    /// </summary>
    void Focus(string panelId);

    /// <summary>
    /// Returns true if a panel with the given id is currently mounted on this surface.
    /// </summary>
    bool IsMounted(string panelId);

    /// <summary>
    /// Raised when a mounted panel should be dismissed (Escape, click-outside, OS close, etc.).
    /// The router subscribes to this and translates the request into a <c>Close</c>.
    /// </summary>
    event EventHandler<PanelDismissEventArgs>? DismissRequested;

    /// <summary>
    /// Raised when the surface's active panel changes for reasons outside the router's
    /// control — typically a user click on a tab in a multi-mount surface (right dock).
    /// Event arg is the new active panel id, or null when nothing is active. The router
    /// subscribes to keep its active-tracking in sync so subsequent toggle/focus decisions
    /// use the user's current selection. Single-mount surfaces (Popup, Window, Center)
    /// don't need to raise this — the router updates active on Mount/Focus.
    /// </summary>
    event EventHandler<string?>? ActiveChanged;
}
