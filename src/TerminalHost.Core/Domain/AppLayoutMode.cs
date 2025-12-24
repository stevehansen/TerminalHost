namespace TerminalHost.Core.Domain;

/// <summary>
/// Defines the application-wide layout mode for displaying projects.
/// </summary>
public enum AppLayoutMode
{
    /// <summary>
    /// Traditional tab bar at the top of the window.
    /// </summary>
    Tabs,

    /// <summary>
    /// Workspace sidebar on the left with project tree and worktree support.
    /// </summary>
    WorkspaceSidebar
}
