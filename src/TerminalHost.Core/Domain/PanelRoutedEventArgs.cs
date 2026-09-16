namespace TerminalHost.Core.Domain;

/// <summary>
/// Event payload raised by <c>IPanelRouter.Routed</c> when a panel is shown, moved, or closed.
/// </summary>
/// <remarks>
/// A null <see cref="OldZone"/> indicates a freshly opened panel; a null <see cref="NewZone"/> indicates a closed panel.
/// Both non-null indicates a zone transition.
/// </remarks>
public sealed class PanelRoutedEventArgs(string panelId, PanelZone? oldZone, PanelZone? newZone, PanelScope scope) : EventArgs
{
    /// <summary>The id of the affected panel.</summary>
    public string PanelId { get; } = panelId;

    /// <summary>The zone the panel was in before this event; null when newly opened.</summary>
    public PanelZone? OldZone { get; } = oldZone;

    /// <summary>The zone the panel is in after this event; null when closed.</summary>
    public PanelZone? NewZone { get; } = newZone;

    /// <summary>The scope the panel operation took place under.</summary>
    public PanelScope Scope { get; } = scope;
}
