namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Opt-in sibling interface that a panel VM can implement to veto its own close.
/// Surfaces invoke <see cref="CanClose"/> from their close gesture (OS X button, programmatic
/// close); the router's <c>BuildMountOptions</c> mirrors the presence of this interface into
/// <see cref="TerminalHost.Core.Domain.PanelMountOptions.ConfirmOnClose"/> for surfaces that want a cheap probe.
/// </summary>
public interface IPanelCloseGuard
{
    /// <summary>
    /// Returns true if the panel may close. The implementation typically prompts the user
    /// when there's unsaved state and may show modal UI. Must run synchronously on the UI thread.
    /// Surfaces skip this probe when closing programmatically (router-initiated Unmount, dock-back,
    /// app shutdown) — see <c>PanelWindow.BeginProgrammaticClose</c>.
    /// </summary>
    bool CanClose();
}
