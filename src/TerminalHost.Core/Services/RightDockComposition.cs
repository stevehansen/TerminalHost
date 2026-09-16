using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Pure merge rule for the hoisted right dock. The dock shows the active workspace's per-tab
/// panels (Explorer, etc.) followed by app-global panels (Sessions). This type owns the
/// "sticky-by-kind" active-selection rule so it is testable without any WPF surface.
/// </summary>
public static class RightDockComposition
{
    /// <summary>
    /// Merges the two source panel lists into one ordered list (per-workspace first, global as a
    /// stable tail) and decides which panel id should be active after the merge.
    /// </summary>
    /// <param name="perWorkspace">The active workspace's per-tab right-dock panels, in their own order.</param>
    /// <param name="global">App-global right-dock panels (e.g. Sessions), in their own order.</param>
    /// <param name="currentActiveId">The panel id currently focused in the dock, if any.</param>
    /// <param name="incomingWorkspaceLastActiveId">
    /// The per-workspace panel id last focused for the incoming workspace, used when the previously
    /// focused dock tab was a per-workspace panel (which no longer exists after the switch).
    /// </param>
    /// <returns>
    /// The merged ordered panel list and the panel id that should be active. The active id is:
    /// <list type="number">
    /// <item>kept as-is when the currently focused panel is still present (sticky-by-kind: a focused
    /// global panel survives a workspace switch);</item>
    /// <item>otherwise the incoming workspace's last-active per-workspace panel, if present;</item>
    /// <item>otherwise the first merged panel (favoring per-workspace, falling to global);</item>
    /// <item>otherwise null when nothing is present.</item>
    /// </list>
    /// </returns>
    public static (IReadOnlyList<IPanelableViewModel> Merged, string? ActiveId) Compose(
        IReadOnlyList<IPanelableViewModel> perWorkspace,
        IReadOnlyList<IPanelableViewModel> global,
        string? currentActiveId,
        string? incomingWorkspaceLastActiveId)
    {
        var merged = new List<IPanelableViewModel>(perWorkspace.Count + global.Count);
        merged.AddRange(perWorkspace);
        merged.AddRange(global);

        string? activeId = null;
        if (currentActiveId is not null && merged.Any(p => p.PanelId == currentActiveId))
            activeId = currentActiveId;
        else if (incomingWorkspaceLastActiveId is not null
                 && merged.Any(p => p.PanelId == incomingWorkspaceLastActiveId))
            activeId = incomingWorkspaceLastActiveId;
        else if (merged.Count > 0)
            activeId = merged[0].PanelId;

        return (merged, activeId);
    }
}
