using System;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Domain;

public class CenterPanelRestoreEventArgs : EventArgs
{
    public required ITabViewModel Tab { get; init; }
    public required string PanelId { get; init; }
    public string? GitPanelActiveTab { get; init; }

    /// <summary>
    /// When true, only associate the panel with the tab (set ActiveCenterPanel)
    /// without loading data. Used for non-selected tabs during startup to avoid
    /// race conditions with singleton panel ViewModels.
    /// </summary>
    public bool SkipDataLoad { get; init; }
}
