namespace TerminalHost.Domain;

/// <summary>
/// Defines the application-level layout mode for navigation.
/// </summary>
public enum AppLayoutMode
{
    /// <summary>
    /// Traditional horizontal tab strip at the top of the window.
    /// </summary>
    Tabs,

    /// <summary>
    /// Vertical sidebar on the left side of the window.
    /// Shows workspaces, worktrees, and open projects.
    /// </summary>
    Sidebar
}
