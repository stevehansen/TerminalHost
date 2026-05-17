using System;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Coordinates startup-restore center-panel events for the workspace.
/// During normal use, <see cref="Request"/> fires <see cref="RestoreRequested"/> immediately.
/// During a batch (between <see cref="BeginBatch"/> and <see cref="EndBatch"/>) requests are
/// queued; <see cref="EndBatch"/> then dispatches them with the selected tab's restore
/// fired last (without SkipDataLoad) and all others fired first with SkipDataLoad=true.
/// This prevents singleton center-panel ViewModels from racing each other during the
/// 60-tab startup restore.
///
/// Thread affinity: all members must be called on the UI/dispatcher thread. The
/// implementation holds unsynchronized state and dispatches handlers synchronously.
/// </summary>
public interface ITabRestoreCoordinator
{
    /// <summary>True between BeginBatch and EndBatch.</summary>
    bool IsBatching { get; }

    /// <summary>
    /// Raised when a tab's center panel should be restored. Hosts forward this to their
    /// own CenterPanelRestoreRequested event so views don't have to change wiring.
    /// </summary>
    event EventHandler<CenterPanelRestoreEventArgs>? RestoreRequested;

    /// <summary>
    /// Outside a batch: raises <see cref="RestoreRequested"/> immediately.
    /// Inside a batch: queues the args for later dispatch by <see cref="EndBatch"/>.
    /// </summary>
    void Request(CenterPanelRestoreEventArgs args);

    /// <summary>Open a batch. Subsequent <see cref="Request"/> calls are queued.</summary>
    void BeginBatch();

    /// <summary>
    /// Close the batch and dispatch all queued items via <see cref="RestoreRequested"/>.
    /// Non-selected tabs fire first with <see cref="CenterPanelRestoreEventArgs.SkipDataLoad"/>=true.
    /// The selected tab (matched by reference equality on <see cref="CenterPanelRestoreEventArgs.Tab"/>)
    /// fires last with its original args (SkipDataLoad unchanged).
    /// </summary>
    /// <param name="selectedTab">The tab that ended up selected after restore. May be null
    /// (nothing was selected) — in which case all queued items fire with SkipDataLoad=true.</param>
    void EndBatch(object? selectedTab);
}
