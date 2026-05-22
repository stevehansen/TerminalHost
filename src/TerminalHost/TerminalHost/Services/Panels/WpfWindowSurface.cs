using System.Diagnostics;
using System.Windows;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services.Panels;

/// <summary>
/// WPF <see cref="IPanelSurface"/> adapter for <c>(PanelZone.Window, PanelScope.AppShell)</c>.
/// Subsumes the legacy <c>PanelWindowManager</c>: maintains a per-panel-id cache of
/// <see cref="Views.PanelWindow"/> instances, pins owner to the main window via a lazy provider,
/// and translates user-driven dock-back into a surface-specific event for <c>MainWindow</c>
/// to forward back into panel-state restoration.
/// </summary>
/// <remarks>
/// Dock-back deliberately does NOT round-trip through <see cref="IPanelRouter.Move"/>. The
/// Center and RightDock surfaces don't exist in Phase 2 (they land in Phase 3/4), so there's
/// no router target to <c>Move</c> to. Instead, this surface raises <see cref="DockBackRequested"/>;
/// <c>MainWindow.OnWindowSurfaceDockBackRequested</c> calls <c>Close</c> and re-shows the panel
/// in its previous tab placement. Phase 4 will fold this back into <c>router.Move</c> once
/// Center/RightDock are routable.
///
/// Multi-instance support: the window cache is <c>Dictionary&lt;string, List&lt;WindowEntry&gt;&gt;</c>
/// so multiple VMs sharing a <c>PanelId</c> (e.g. detached file viewers) coexist. The router's
/// <c>IPanelSurface.Unmount(string panelId)</c> contract is panel-id-keyed, so when several windows
/// share an id this surface closes the most-recently-mounted one (LIFO). The router's
/// <c>FindRegistrationKeyByPanelId</c> picks the first registration in its registry; the pairing of
/// router-registration to surface-window for multi-instance dispatch is therefore not strictly
/// ordered. This is acceptable for Phase 2: each Close call closes one window and removes one
/// registration. Phase 4 (passing VM identity through the surface contract) will tighten this.
/// </remarks>
public sealed class WpfWindowSurface : IPanelSurface
{
    private readonly Func<Window?> _ownerProvider;
    private readonly IDispatcherService _dispatcher;
    private readonly Dictionary<string, List<WindowEntry>> _windows = new(StringComparer.Ordinal);
    private readonly HashSet<string> _suppressClosedFor = new(StringComparer.Ordinal);

    public PanelZone Zone => PanelZone.Window;
    public PanelScope Scope => PanelScope.AppShell;

    public event EventHandler<PanelDismissEventArgs>? DismissRequested;

    /// <summary>
    /// Raised when the user clicks the dock-back chrome button on a hosted window. The
    /// recipient is responsible for closing this panel via the router and re-showing it in
    /// its previous tab placement. Not part of <see cref="IPanelSurface"/> because dock-back
    /// targets (Center/RightDock) aren't routable yet (Phase 4).
    /// </summary>
    public event EventHandler<IPanelableViewModel>? DockBackRequested;

    public WpfWindowSurface(Func<Window?> ownerProvider, IDispatcherService dispatcher)
    {
        _ownerProvider = ownerProvider;
        _dispatcher = dispatcher;
    }

    // PanelMountOptions.Size is intentionally ignored — Window zone honors vm.Width/vm.Height
    // (auto-persisted via existing bindings). TODO Phase 4: honor Size presets if a caller asks.
    public void Mount(IPanelableViewModel vm, PanelMountOptions options)
    {
        AssertUiThread();

        var window = new Views.PanelWindow
        {
            DataContext = vm,
            Owner = _ownerProvider(),
            Width = vm.Width,
            Height = vm.Height
        };

        if (options.AlwaysOnTop) window.Topmost = true;

        var entry = new WindowEntry(window, vm);
        entry.DockHandler = (s, p) => OnWindowDockRequested(entry);
        entry.ClosedHandler = (s, e) => OnWindowClosed(entry);
        window.DockRequested += entry.DockHandler;
        window.Closed += entry.ClosedHandler;

        if (!_windows.TryGetValue(vm.PanelId, out var list))
        {
            list = new List<WindowEntry>();
            _windows[vm.PanelId] = list;
        }
        list.Add(entry);

        try
        {
            window.Show();
        }
        catch
        {
            list.Remove(entry);
            if (list.Count == 0) _windows.Remove(vm.PanelId);
            DetachHandlers(entry);
            throw;
        }
    }

    public void Unmount(string panelId)
    {
        AssertUiThread();
        if (!_windows.TryGetValue(panelId, out var list) || list.Count == 0) return;

        var entry = list[^1];
        list.RemoveAt(list.Count - 1);
        if (list.Count == 0) _windows.Remove(panelId);

        _suppressClosedFor.Add(panelId);
        DetachHandlers(entry);
        try
        {
            entry.Window.BeginProgrammaticClose();
            entry.Window.Close();
        }
        finally
        {
            _suppressClosedFor.Remove(panelId);
        }
    }

    public void Focus(string panelId)
    {
        AssertUiThread();
        if (_windows.TryGetValue(panelId, out var list) && list.Count > 0)
        {
            var entry = list[^1];
            if (entry.Window.IsLoaded) entry.Window.Activate();
        }
    }

    public bool IsMounted(string panelId) =>
        _windows.TryGetValue(panelId, out var list) && list.Count > 0 && list[^1].Window.IsLoaded;

    private void OnWindowDockRequested(WindowEntry entry)
    {
        var panelId = entry.Vm.PanelId;
        if (_windows.TryGetValue(panelId, out var list))
        {
            list.Remove(entry);
            if (list.Count == 0) _windows.Remove(panelId);
        }

        _suppressClosedFor.Add(panelId);
        DetachHandlers(entry);
        try
        {
            entry.Window.BeginProgrammaticClose();
            entry.Window.Close();
        }
        finally
        {
            _suppressClosedFor.Remove(panelId);
        }

        DockBackRequested?.Invoke(this, entry.Vm);
    }

    private void OnWindowClosed(WindowEntry entry)
    {
        var panelId = entry.Vm.PanelId;
        if (_suppressClosedFor.Contains(panelId))
        {
            // Programmatic close (Unmount, dock-back) — already cleaned up by the caller.
            return;
        }

        if (_windows.TryGetValue(panelId, out var list))
        {
            list.Remove(entry);
            if (list.Count == 0) _windows.Remove(panelId);
        }
        DetachHandlers(entry);
        DismissRequested?.Invoke(this, new PanelDismissEventArgs(panelId, PanelDismissTrigger.OwnerClosed));
    }

    private static void DetachHandlers(WindowEntry entry)
    {
        if (entry.DockHandler is { } dock) entry.Window.DockRequested -= dock;
        if (entry.ClosedHandler is { } closed) entry.Window.Closed -= closed;
        entry.DockHandler = null;
        entry.ClosedHandler = null;
    }

    private void AssertUiThread()
    {
        Debug.Assert(_dispatcher.CheckAccess(), "WpfWindowSurface must be called on the UI thread");
    }

    private sealed class WindowEntry(Views.PanelWindow window, IPanelableViewModel vm)
    {
        public Views.PanelWindow Window { get; } = window;
        public IPanelableViewModel Vm { get; } = vm;
        public EventHandler<IPanelableViewModel>? DockHandler { get; set; }
        public EventHandler? ClosedHandler { get; set; }
    }
}
