using System.ComponentModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services.Panels;

/// <summary>
/// WPF <see cref="IPanelSurface"/> adapter for <c>(PanelZone.Center, PanelScope.ForTab(...))</c>.
/// One instance per <c>TerminalPairTabViewModel</c>; constructed by the tab VM and registered with
/// the router on tab creation, unregistered on tab close.
/// </summary>
/// <remarks>
/// Single-slot surface: <see cref="Mount"/> evicts any prior mount before mounting the new VM.
/// The tab VM binds its center <c>ContentControl</c> to <see cref="MountedPanel"/> via
/// <see cref="INotifyPropertyChanged"/>, so there is no host control attach step (unlike
/// <c>WpfRightDockSurface</c>). <see cref="HasMounted"/> drives the terminals-vs-center
/// content switcher.
/// </remarks>
public sealed class WpfCenterSurface : IPanelSurface, INotifyPropertyChanged, IDisposable
{
    private IPanelableViewModel? _mounted;
    private bool _disposed;

    public PanelZone Zone => PanelZone.Center;
    public PanelScope Scope { get; }

    // The Center surface has no source of dismiss events (no Escape capture, no click-outside,
    // no OS close). The event is required by IPanelSurface but is never raised here. ActiveChanged
    // likewise unused — Center is single-slot, router updates active on Mount/Focus.
#pragma warning disable CS0067
    public event EventHandler<PanelDismissEventArgs>? DismissRequested;
    public event EventHandler<string?>? ActiveChanged;
#pragma warning restore CS0067
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The currently mounted panel, or null when the center slot is empty.</summary>
    public IPanelableViewModel? MountedPanel => _mounted;

    /// <summary>True when a panel is mounted in this surface's single slot.</summary>
    public bool HasMounted => _mounted is not null;

    public WpfCenterSurface(PanelScope scope)
    {
        if (scope.TabId is null)
            throw new ArgumentException("WpfCenterSurface requires a tab-scoped PanelScope.", nameof(scope));
        Scope = scope;
    }

    public void Mount(IPanelableViewModel vm, PanelMountOptions options)
    {
        if (_disposed) return;
        if (ReferenceEquals(_mounted, vm)) return;

        // Single-slot semantics: overwrite in-place. We deliberately do NOT route through Unmount
        // first — that would raise HasMounted=false then =true again, flickering bindings derived
        // from HasMounted (e.g. IsTerminalsVisible) on every Center→Center panel swap.
        var wasEmpty = _mounted is null;
        _mounted = vm;

        OnPropertyChanged(nameof(MountedPanel));
        if (wasEmpty) OnPropertyChanged(nameof(HasMounted));
    }

    public void Unmount(string panelId)
    {
        if (_disposed) return;
        if (_mounted is null || _mounted.PanelId != panelId) return;

        _mounted = null;
        OnPropertyChanged(nameof(MountedPanel));
        OnPropertyChanged(nameof(HasMounted));
    }

    public void Focus(string panelId)
    {
        // No-op: single-slot surface. The mounted panel is implicitly focused.
    }

    public bool IsMounted(string panelId) => _mounted?.PanelId == panelId;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mounted = null;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
