using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// Minimal <see cref="BasePanelViewModel"/> subclass used by router tests.
/// </summary>
public class StubPanelableViewModel(
    string panelId,
    string panelTitle = "Stub",
    string panelIcon = "*",
    PanelSizePreset sizePreset = PanelSizePreset.Medium) : BasePanelViewModel
{
    public override string PanelId { get; } = panelId;
    public override string PanelTitle { get; } = panelTitle;
    public override string PanelIcon { get; } = panelIcon;
    public override PanelSizePreset SizePreset { get; } = sizePreset;

    /// <summary>Raises the base class's <c>StateChangeRequested</c> event as if the user clicked dock/detach.</summary>
    public void TriggerStateChangeRequest(PanelDisplayState state, PanelSide? side = null)
    {
        if (state == PanelDisplayState.Panel)
            DockCommand.Execute(side);
        else
            DetachCommand.Execute(null);
    }
}

/// <summary>
/// A <see cref="StubPanelableViewModel"/> that also implements <see cref="IPanelPlacement"/>.
/// </summary>
public sealed class StubPlaceablePanelViewModel(
    string panelId,
    PanelZone preferredZone,
    PanelScope? preferredScope = null,
    PanelSizePreset sizePreset = PanelSizePreset.Medium)
    : StubPanelableViewModel(panelId, sizePreset: sizePreset), IPanelPlacement
{
    public PanelZone PreferredZone { get; } = preferredZone;
    public PanelScope PreferredScope { get; } = preferredScope ?? PanelScope.AppShell;
}
