namespace TerminalHost.Core.Domain;

/// <summary>
/// Event payload raised by an <c>IPanelSurface</c> when a panel should be dismissed.
/// </summary>
public sealed class PanelDismissEventArgs(string panelId, PanelDismissTrigger trigger) : EventArgs
{
    /// <summary>The id of the panel to dismiss.</summary>
    public string PanelId { get; } = panelId;

    /// <summary>The reason the surface raised this dismissal.</summary>
    public PanelDismissTrigger Trigger { get; } = trigger;
}
