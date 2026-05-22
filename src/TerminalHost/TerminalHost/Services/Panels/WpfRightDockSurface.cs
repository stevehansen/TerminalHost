using System.Collections.ObjectModel;
using System.ComponentModel;
using TerminalHost.Controls;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services.Panels;

/// <summary>
/// WPF <see cref="IPanelSurface"/> adapter for <c>(PanelZone.RightDock, PanelScope.ForTab(...))</c>.
/// One instance per <c>TerminalPairTabViewModel</c>; constructed by the tab VM and registered with
/// the router on tab creation, unregistered on tab close.
/// </summary>
/// <remarks>
/// The surface owns its <see cref="Panels"/> <see cref="ObservableCollection{T}"/> imperatively
/// and only hands it to a <see cref="PanelHost"/> control once the view loads (lazy attach via
/// <see cref="Attach"/>). Mounts that arrive before <see cref="Attach"/> are buffered in the
/// collection and replay automatically once a host is bound. The surface exposes
/// <see cref="HasMounted"/> via <see cref="INotifyPropertyChanged"/> so the tab VM can derive
/// its <c>IsExplorerVisible</c> from this single source of truth.
/// </remarks>
public sealed class WpfRightDockSurface : IPanelSurface, INotifyPropertyChanged, IDisposable
{
    private readonly ObservableCollection<IPanelableViewModel> _panels = new();
    private PanelHost? _host;
    private bool _disposed;

    public PanelZone Zone => PanelZone.RightDock;
    public PanelScope Scope { get; }

    // The RightDock surface has no source of dismiss events (no Escape capture, no click-outside,
    // no OS close). The event is required by IPanelSurface but is never raised here.
#pragma warning disable CS0067
    public event EventHandler<PanelDismissEventArgs>? DismissRequested;
#pragma warning restore CS0067
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// True when at least one panel is mounted on this surface. Computed from the panels
    /// collection so consumers can derive visibility (e.g. column-width binders) from one bit.
    /// </summary>
    public bool HasMounted => _panels.Count > 0;

    public WpfRightDockSurface(PanelScope scope)
    {
        if (scope.TabId is null)
            throw new ArgumentException("WpfRightDockSurface requires a tab-scoped PanelScope.", nameof(scope));
        Scope = scope;
    }

    /// <summary>
    /// Wires this surface to a <see cref="PanelHost"/> created by the tab's view. Safe to call
    /// after mounts have already been queued — the host picks them up immediately. Calling
    /// <c>Attach</c> a second time on the same surface replaces the host (e.g. view reload).
    /// </summary>
    public void Attach(PanelHost host)
    {
        if (_disposed) return;
        if (ReferenceEquals(_host, host)) return;

        if (_host is not null)
            _host.ActivePanelChanged -= OnHostActivePanelChanged;

        _host = host;
        host.Panels = _panels;
        host.ActivePanelChanged += OnHostActivePanelChanged;

        // Seed the host's active panel with whatever is currently first; the router will
        // re-focus the correct entry after Restore completes via IPanelSurface.Focus.
        if (host.ActivePanel is null && _panels.Count > 0)
            host.ActivePanel = _panels[0];
    }

    public void Mount(IPanelableViewModel vm, PanelMountOptions options)
    {
        if (_disposed) return;
        var wasEmpty = _panels.Count == 0;
        if (!_panels.Contains(vm))
            _panels.Add(vm);

        if (_host is not null)
            _host.ActivePanel = vm;

        if (wasEmpty) OnPropertyChanged(nameof(HasMounted));
    }

    public void Unmount(string panelId)
    {
        if (_disposed) return;
        var index = IndexOf(panelId);
        if (index < 0) return;

        var panel = _panels[index];
        var wasActive = _host?.ActivePanel == panel;
        _panels.RemoveAt(index);

        if (_host is not null && wasActive)
        {
            _host.ActivePanel = _panels.Count > 0
                ? _panels[Math.Min(index, _panels.Count - 1)]
                : null;
        }

        if (_panels.Count == 0) OnPropertyChanged(nameof(HasMounted));
    }

    public void Focus(string panelId)
    {
        if (_host is null) return;
        var index = IndexOf(panelId);
        if (index < 0) return;
        _host.ActivePanel = _panels[index];
    }

    public bool IsMounted(string panelId) => IndexOf(panelId) >= 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_host is not null)
        {
            _host.ActivePanelChanged -= OnHostActivePanelChanged;
            // Leave host.Panels pointing at our (empty after router CloseZone) collection; the
            // host control is being torn down with the tab view anyway.
            _host = null;
        }
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

    private void OnHostActivePanelChanged(object? sender, IPanelableViewModel? _)
    {
        // The PanelHost binding two-way-changed; surface tracking happens in PanelRouter via
        // Focus(). The router's _activePanel map is updated when callers ask for a Focus —
        // direct user tab clicks reach the router via Show()/Focus() through other plumbing.
        // No action required here; the event hook exists so that future hooks (telemetry,
        // tests) have a single attach point.
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
