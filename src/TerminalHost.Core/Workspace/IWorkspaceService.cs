using System.Collections.ObjectModel;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Owns the workspace's tab collection and selection state, plus the
/// mutation/query operations that don't require host-specific tab construction.
/// <para>
/// Step 4a introduced collection ownership; Step 4b (this revision) adds
/// selection ownership and the close-and-pick-next helper that both hosts
/// previously duplicated at the end of their <c>CloseTab</c> methods.
/// The actual <c>OpenProjectTab</c> / <c>CloseTab</c> / <c>RestoreOpenFolders</c>
/// lifecycle still lives in <c>MainViewModel</c> — moving it requires pulling
/// host services (terminal factory, container service, sidebar, panel restore)
/// into a port boundary, which is a later slice.
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
    /// The currently selected tab, or <c>null</c> if no tab is selected.
    /// Setting this property toggles <see cref="ITabViewModel.IsSelected"/> on
    /// the old and new tabs and raises <see cref="SelectedTabChanged"/>.
    /// Setting to the current value is a no-op (no event).
    /// </summary>
    ITabViewModel? SelectedTab { get; set; }

    /// <summary>
    /// Raised after <see cref="SelectedTab"/> changes — host code subscribes to
    /// run its presentation-layer side effects (focus tracking, panel updates,
    /// API events, etc.). The <c>IsSelected</c> flags on the old/new tabs are
    /// already updated by the time this fires.
    /// </summary>
    event EventHandler<TabSelectionChangedEventArgs>? SelectedTabChanged;

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
    /// Removes <paramref name="tab"/> from the collection. If it was the
    /// <see cref="SelectedTab"/>, the new selection becomes the last remaining
    /// tab (or <c>null</c> if the collection is now empty). No-op if the tab
    /// is null or not in the collection.
    /// </summary>
    /// <remarks>
    /// This replaces the duplicated <c>Tabs.Remove(tab); if (SelectedTab == tab
    /// &amp;&amp; Tabs.Count &gt; 0) SelectedTab = Tabs[^1];</c> tail of both
    /// hosts' <c>CloseTab</c> methods. Host-side disposal (terminals, event
    /// unsubscription) still runs in <c>CloseTab</c> before this is called.
    /// </remarks>
    void RemoveAndPickNext(ITabViewModel? tab);

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
