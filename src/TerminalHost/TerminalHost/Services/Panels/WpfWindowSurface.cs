using System.Diagnostics;
using System.Windows;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services.Panels;

/// <summary>
/// WPF <see cref="IPanelSurface"/> adapter for <c>(PanelZone.Window, PanelScope.AppShell)</c>.
/// Subsumes the legacy <c>PanelWindowManager</c>: maintains a per-panel-id cache of
/// <see cref="Views.PanelWindow"/> instances and pins owner to the main window via a lazy provider.
/// </summary>
/// <remarks>
/// User-driven dock-back travels via <c>BasePanelViewModel.DockCommand</c> →
/// <c>StateChangeRequested(Panel)</c> → the router's existing subscription, which calls
/// <see cref="IPanelRouter.Move"/>. This surface is not involved in the dock-back path; it only
/// handles OS-window close (raises <see cref="DismissRequested"/>) and programmatic Unmount.
///
/// Multi-instance support: the window cache is <c>Dictionary&lt;string, List&lt;WindowEntry&gt;&gt;</c>
/// so multiple VMs sharing a <c>PanelId</c> (e.g. detached file viewers) coexist. The router's
/// <c>IPanelSurface.Unmount(string panelId)</c> contract is panel-id-keyed, so when several windows
/// share an id this surface closes the most-recently-mounted one (LIFO).
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
        entry.ClosedHandler = (s, e) => OnWindowClosed(entry);
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
        if (entry.ClosedHandler is { } closed) entry.Window.Closed -= closed;
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
        public EventHandler? ClosedHandler { get; set; }
    }
}
