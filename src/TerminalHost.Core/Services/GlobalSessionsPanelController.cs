using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Owns the placement of the single, app-global Sessions panel. The Sessions view model is a
/// cross-workspace singleton; its only home is <c>(RightDock, AppShell)</c>. Routing it there
/// (instead of into each tab's <c>(RightDock, Tab:x)</c> surface) guarantees it is mounted in
/// exactly one place and never relocated by the router's single-instance-per-VM rule when the
/// user switches tabs.
/// </summary>
/// <remarks>
/// This is the only thing in the system that knows the Sessions panel is global. It hides that
/// showing/hiding is a router <c>Show</c>/<c>Close</c>, that the home is AppShell-scoped, and that
/// the single-instance rule exists. Refresh fan-out is not its concern — the Sessions view model
/// already subscribes to <c>ISessionLifecycleCoordinator</c>.
/// </remarks>
public sealed class GlobalSessionsPanelController
{
    private readonly IPanelRouter _router;
    private readonly IPanelableViewModel _panel;

    public GlobalSessionsPanelController(IPanelRouter router, IPanelableViewModel panel)
    {
        _router = router;
        _panel = panel;
    }

    /// <summary>True when the Sessions panel is currently mounted on its AppShell home.</summary>
    public bool IsVisible => _router.IsOpen(_panel.PanelId);

    /// <summary>
    /// Idempotent. Routes the single Sessions view model to <c>(RightDock, AppShell)</c> when
    /// <paramref name="visible"/> is true, or closes it when false. Double calls in the same
    /// direction are no-ops.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (visible)
        {
            if (IsVisible) return;
            _router.Show(
                _panel,
                new PanelShowOptions(Zone: PanelZone.RightDock, Scope: PanelScope.AppShell, ForceShow: true));
        }
        else
        {
            if (!IsVisible) return;
            _router.Close(_panel.PanelId);
        }
    }
}
