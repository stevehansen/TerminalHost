using System.Collections.ObjectModel;
using System.ComponentModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services.Panels;

/// <summary>
/// WPF <see cref="IPanelSurface"/> adapter for <c>(PanelZone.RightDock, PanelScope.ForTab(...))</c>.
/// One instance per <c>TerminalPairTabViewModel</c>; constructed by the tab VM and registered with
/// the router on tab creation, unregistered on tab close.
/// </summary>
/// <remarks>
/// The surface owns its <see cref="Panels"/> <see cref="ObservableCollection{T}"/> imperatively and
/// is otherwise passive: it does NOT bind a <c>PanelHost</c> control. The hoisted dock coordinator
/// (owned by the main window) merges this surface's panels with the app-global surface's panels into
/// the single shared host. The surface exposes <see cref="HasMounted"/> and <see cref="ActiveChanged"/>
/// so the coordinator and the router can react to mount/unmount/focus without owning a host here.
/// </remarks>
public sealed class WpfRightDockSurface : IPanelSurface, INotifyPropertyChanged, IDisposable
{
    private readonly ObservableCollection<IPanelableViewModel> _panels = new();
    private string? _lastActivePanelId;
    private bool _disposed;

    public PanelZone Zone => PanelZone.RightDock;
    public PanelScope Scope { get; }

    // The RightDock surface has no source of dismiss events (no Escape capture, no click-outside,
    // no OS close). The event is required by IPanelSurface but is never raised here.
#pragma warning disable CS0067
    public event EventHandler<PanelDismissEventArgs>? DismissRequested;
#pragma warning restore CS0067
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler<string?>? ActiveChanged;

    /// <summary>The panels mounted on this surface, in mount order. Read-only view for the coordinator.</summary>
    public IReadOnlyList<IPanelableViewModel> Panels => _panels;

    /// <summary>
    /// True when at least one panel is mounted on this surface. Computed from the panels
    /// collection so consumers can derive visibility (e.g. dock-width binders) from one bit.
    /// </summary>
    public bool HasMounted => _panels.Count > 0;

    /// <summary>
    /// The panel id last activated on this surface, or null. The coordinator reads this when a
    /// workspace switch needs to restore the incoming tab's last-focused per-workspace panel.
    /// </summary>
    public string? LastActivePanelId => _lastActivePanelId;

    /// <summary>Raised when this surface's panel collection changes (mount/unmount).</summary>
    public event EventHandler? PanelsChanged;

    public WpfRightDockSurface(PanelScope scope)
    {
        if (scope.TabId is null)
            throw new ArgumentException("WpfRightDockSurface requires a tab-scoped PanelScope.", nameof(scope));
        Scope = scope;
    }

    public void Mount(IPanelableViewModel vm, PanelMountOptions options)
    {
        if (_disposed) return;
        var wasEmpty = _panels.Count == 0;
        if (!_panels.Contains(vm))
            _panels.Add(vm);

        _lastActivePanelId = vm.PanelId;
        PanelsChanged?.Invoke(this, EventArgs.Empty);
        ActiveChanged?.Invoke(this, vm.PanelId);

        if (wasEmpty) OnPropertyChanged(nameof(HasMounted));
    }

    public void Unmount(string panelId)
    {
        if (_disposed) return;
        var index = IndexOf(panelId);
        if (index < 0) return;

        var wasActive = _lastActivePanelId == panelId;
        _panels.RemoveAt(index);

        if (wasActive)
        {
            _lastActivePanelId = _panels.Count > 0
                ? _panels[Math.Min(index, _panels.Count - 1)].PanelId
                : null;
            ActiveChanged?.Invoke(this, _lastActivePanelId);
        }

        PanelsChanged?.Invoke(this, EventArgs.Empty);
        if (_panels.Count == 0) OnPropertyChanged(nameof(HasMounted));
    }

    public void Focus(string panelId)
    {
        if (_disposed) return;
        if (IndexOf(panelId) < 0) return;
        _lastActivePanelId = panelId;
        ActiveChanged?.Invoke(this, panelId);
    }

    public bool IsMounted(string panelId) => IndexOf(panelId) >= 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _panels.Clear();
    }

    private int IndexOf(string panelId)
    {
        for (var i = 0; i < _panels.Count; i++)
        {
            if (_panels[i].PanelId == panelId) return i;
        }
        return -1;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
