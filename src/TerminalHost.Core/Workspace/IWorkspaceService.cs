using System.Collections.ObjectModel;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Owns the workspace's tab collection and exposes the mutation/query operations
/// that don't require host-specific tab construction.
/// <para>
/// Step 4a of the manager decomposition (issue #48) — this slice owns the
/// <see cref="ObservableCollection{T}"/> instance so future slices can move
/// tab-lifecycle code (Open/Close/Restore) here without rewiring callers.
/// Selection state and the actual <c>OpenProjectTab</c> / <c>CloseTab</c>
/// implementations still live in <c>MainViewModel</c>.
/// </para>
/// </summary>
/// <remarks>
/// UI-thread-only. The collection is mutated directly so WPF/Avalonia bindings
/// pick up changes via <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>.
/// </remarks>
public interface IWorkspaceService
{
    /// <summary>
    /// The live tab collection. The concrete <see cref="ObservableCollection{T}"/>
    /// type is exposed (rather than <see cref="IReadOnlyList{T}"/>) because XAML
    /// bindings and the existing <c>TabRouter</c> require the change-notifying
    /// collection reference directly.
    /// </summary>
    ObservableCollection<ITabViewModel> Tabs { get; }

    /// <summary>
    /// Moves the tab at <paramref name="oldIndex"/> to <paramref name="newIndex"/>.
    /// Both indices are clamped to the valid range; out-of-range or equal indices
    /// are a no-op (errors defined out of existence — callers don't precheck).
    /// </summary>
    void Move(int oldIndex, int newIndex);

    /// <summary>
    /// Moves <paramref name="tab"/> to index 0. No-op if the tab is missing,
    /// null, or already at the front.
    /// </summary>
    void MoveToFront(ITabViewModel? tab);

    /// <summary>
    /// Moves <paramref name="tab"/> to the last position. No-op if the tab is
    /// missing, null, or already at the end.
    /// </summary>
    void MoveToEnd(ITabViewModel? tab);

    /// <summary>
    /// Returns the closeable tabs that are not <paramref name="keep"/>, in
    /// current order. Callers iterate the result and invoke their own close
    /// action — closing involves host-side disposal (terminals, file watchers,
    /// event subscriptions) that the service doesn't own yet.
    /// </summary>
    IReadOnlyList<ITabViewModel> GetTabsToCloseExcept(ITabViewModel? keep);

    /// <summary>
    /// Returns the closeable tabs positioned after <paramref name="tab"/> in
    /// the collection. Empty if the tab is missing, null, or last.
    /// </summary>
    IReadOnlyList<ITabViewModel> GetTabsToCloseToRightOf(ITabViewModel? tab);
}
