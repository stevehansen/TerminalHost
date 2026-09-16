namespace TerminalHost.Core.Domain;

/// <summary>
/// Identifies a presentation zone where a panel can be mounted.
/// Closed set — adding a new zone requires one enum value plus one <c>IPanelSurface</c> implementation.
/// </summary>
public enum PanelZone
{
    /// <summary>Left-side dock surface (tab strip along the left edge).</summary>
    LeftDock,

    /// <summary>Right-side dock surface (tab strip along the right edge).</summary>
    RightDock,

    /// <summary>Center overlay slot inside a tab's content area.</summary>
    Center,

    /// <summary>Transient popup anchored to the main window or an element.</summary>
    Popup,

    /// <summary>Detached top-level OS window.</summary>
    Window
}
