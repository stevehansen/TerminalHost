using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services.Panels;

/// <summary>
/// WPF <see cref="IPanelSurface"/> adapter for <c>(PanelZone.Popup, PanelScope.AppShell)</c>.
/// Hosts at most one panel at a time inside a shared <see cref="Popup"/> control; mounting swaps
/// the <c>Content</c> of an internal <see cref="ContentPresenter"/> bound to the mounted VM, so
/// WPF picks the view via implicit <c>DataTemplate</c>. The popup zone is exclusive by design —
/// mounting a second VM unmounts whatever was there first.
/// </summary>
/// <remarks>
/// The host <see cref="Popup"/> control is owned by <c>MainWindow.xaml</c> and handed to this
/// surface via <see cref="AttachHost"/> after <c>MainWindow.OnLoaded</c> runs. Until then the
/// surface stores any pending mount and applies it once attached.
/// </remarks>
public sealed class WpfPopupSurface : IPanelSurface
{
    private Popup? _host;
    private ContentPresenter? _content;
    private IPanelableViewModel? _mounted;
    private (IPanelableViewModel Vm, PanelMountOptions Options)? _pending;
    private bool _suppressClosedEvent;

    public PanelZone Zone => PanelZone.Popup;
    public PanelScope Scope => PanelScope.AppShell;

    public event EventHandler<PanelDismissEventArgs>? DismissRequested;

    // Single-mount surface: the router updates active state on Mount/Focus; no user-driven
    // active changes can happen.
#pragma warning disable CS0067
    public event EventHandler<string?>? ActiveChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Wires the surface to a host <see cref="Popup"/> created in <c>MainWindow.xaml</c>.
    /// Must be called exactly once, after the main window's first <c>Loaded</c> event.
    /// </summary>
    public void AttachHost(Popup host, ContentPresenter content)
    {
        if (_host is not null) return;
        _host = host;
        _content = content;
        _host.Closed += OnHostClosed;
        _host.PreviewKeyDown += OnHostPreviewKeyDown;

        if (_pending is { } pending)
        {
            _pending = null;
            MountInternal(pending.Vm, pending.Options);
        }
    }

    public void Mount(IPanelableViewModel vm, PanelMountOptions options)
    {
        AssertUiThread();
        if (_host is null || _content is null)
        {
            _pending = (vm, options);
            return;
        }
        MountInternal(vm, options);
    }

    public void Unmount(string panelId)
    {
        AssertUiThread();
        if (_mounted?.PanelId != panelId) return;
        UnmountInternal();
    }

    public void Focus(string panelId)
    {
        if (_host is null || _mounted?.PanelId != panelId) return;
        var child = _host.Child;
        if (child is null) return;
        child.Focusable = true;
        child.Focus();
        Keyboard.Focus(child);
    }

    public bool IsMounted(string panelId) => _mounted?.PanelId == panelId;

    // PanelMountOptions.Size is intentionally ignored in Phase 1 — popup zone uses one preset.
    // TODO Phase 2: honor Size when popup gains size variations.
    private void MountInternal(IPanelableViewModel vm, PanelMountOptions _)
    {
        if (_host is null || _content is null) return;

        // Displacing a previously-mounted panel: clear local state and close the host first,
        // then raise DismissRequested so the router cleans up the displaced VM's registry
        // entry before we mount the new one. _mounted = null up front means the router's
        // synchronous Close → Unmount during the dismiss invocation correctly no-ops here.
        if (_mounted is not null)
        {
            var displaced = _mounted;
            _mounted = null;
            _suppressClosedEvent = true;
            _host.IsOpen = false;
            _suppressClosedEvent = false;
            DismissRequested?.Invoke(this, new PanelDismissEventArgs(displaced.PanelId, PanelDismissTrigger.ProgrammaticClose));
        }

        _mounted = vm;
        _content.Content = vm;
        _host.IsOpen = true;

        // The mounted view's own Loaded / IsVisibleChanged handler focuses its search box —
        // calling Focus on the popup Child here would race with that handler and overwrite the
        // descendant focus (the Child is the popup wrapper, not the actual input control). The
        // router still calls Focus(panelId) explicitly for re-focus (Show with ForceShow on an
        // already-mounted panel), so this isn't a behavior loss.
    }

    private void AssertUiThread()
    {
        Debug.Assert(_host?.Dispatcher.CheckAccess() ?? true, "WpfPopupSurface must be called on the UI thread");
    }

    private void UnmountInternal()
    {
        if (_host is null) return;
        _mounted = null;
        _suppressClosedEvent = true;
        _host.IsOpen = false;
        _suppressClosedEvent = false;
        if (_content is not null) _content.Content = null;
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        if (_suppressClosedEvent) return;
        var current = _mounted;
        if (current is null) return;
        _mounted = null;
        if (_content is not null) _content.Content = null;
        DismissRequested?.Invoke(this, new PanelDismissEventArgs(current.PanelId, PanelDismissTrigger.ClickOutside));
    }

    private void OnHostPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        var current = _mounted;
        if (current is null) return;
        e.Handled = true;
        DismissRequested?.Invoke(this, new PanelDismissEventArgs(current.PanelId, PanelDismissTrigger.Escape));
    }
}
