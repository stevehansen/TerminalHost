using System.Collections.ObjectModel;
using System.ComponentModel;
using TerminalHost.Controls;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Services.Panels;

/// <summary>
/// Main-window-owned coordinator for the single hoisted right dock. Binds the one shared
/// <see cref="PanelHost"/> and merges the active workspace's per-tab right-dock surface with the
/// app-global AppShell right-dock surface, using <see cref="RightDockComposition"/> for ordering and
/// sticky-by-kind active selection. Owns dock visibility (merged set non-empty) and the global dock
/// width, both surfaced via <see cref="INotifyPropertyChanged"/> for the window's column bindings.
/// </summary>
public sealed class RightDockCoordinator : INotifyPropertyChanged
{
    private readonly WpfAppShellRightDockSurface _appShell;
    private readonly ObservableCollection<IPanelableViewModel> _hostPanels = new();

    private PanelHost? _host;
    private WpfRightDockSurface? _activeTab;
    private bool _suppressHostActiveChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RightDockCoordinator(WpfAppShellRightDockSurface appShell)
    {
        _appShell = appShell;
        _appShell.PanelsChanged += OnSourcePanelsChanged;
        _appShell.ActiveChanged += OnSourceActiveChanged;
    }

    /// <summary>True when any panel (per-workspace or global) is mounted in the dock.</summary>
    public bool IsVisible => _hostPanels.Count > 0;

    /// <summary>
    /// Wires the single hoisted <see cref="PanelHost"/> rendered by the main window. Called once
    /// after the window loads.
    /// </summary>
    public void AttachHost(PanelHost host)
    {
        if (ReferenceEquals(_host, host)) return;
        if (_host is not null)
            _host.ActivePanelChanged -= OnHostActivePanelChanged;

        _host = host;
        host.Panels = _hostPanels;
        host.ActivePanelChanged += OnHostActivePanelChanged;
        Recompose();
    }

    /// <summary>
    /// Sets the active workspace's per-tab right-dock surface (or null for non-workspace tabs and the
    /// empty state). Recomposes the merged dock, preserving a focused global panel across the switch
    /// and otherwise restoring the incoming workspace's last-focused per-workspace panel.
    /// </summary>
    public void SetActiveTabSurface(WpfRightDockSurface? surface)
    {
        if (ReferenceEquals(_activeTab, surface)) return;

        if (_activeTab is not null)
        {
            _activeTab.PanelsChanged -= OnSourcePanelsChanged;
            _activeTab.ActiveChanged -= OnSourceActiveChanged;
        }

        _activeTab = surface;

        if (_activeTab is not null)
        {
            _activeTab.PanelsChanged += OnSourcePanelsChanged;
            _activeTab.ActiveChanged += OnSourceActiveChanged;
        }

        Recompose();
    }

    private void OnSourcePanelsChanged(object? sender, EventArgs e) => Recompose();

    private void OnSourceActiveChanged(object? sender, string? newActivePanelId)
    {
        // The router/surface focused a panel programmatically; reflect it in the host without
        // re-entrantly recomposing from the host's own ActivePanelChanged.
        if (newActivePanelId is null) return;
        var panel = _hostPanels.FirstOrDefault(p => p.PanelId == newActivePanelId);
        if (panel is null) return;
        _suppressHostActiveChanged = true;
        try
        {
            if (_host is not null) _host.ActivePanel = panel;
        }
        finally
        {
            _suppressHostActiveChanged = false;
        }
    }

    private void OnHostActivePanelChanged(object? sender, IPanelableViewModel? activePanel)
    {
        if (_suppressHostActiveChanged) return;
        if (activePanel is null) return;

        // Propagate the user's tab click to whichever source surface owns the panel so the router's
        // active tracking and the surface's LastActivePanelId stay in sync.
        if (_appShell.IsMounted(activePanel.PanelId))
            _appShell.Focus(activePanel.PanelId);
        else
            _activeTab?.Focus(activePanel.PanelId);
    }

    private void Recompose()
    {
        var perWorkspace = _activeTab?.Panels ?? (IReadOnlyList<IPanelableViewModel>)Array.Empty<IPanelableViewModel>();
        var global = _appShell.Panels;
        var currentActiveId = _host?.ActivePanel?.PanelId;
        var incomingLastActive = _activeTab?.LastActivePanelId;

        var (merged, activeId) = RightDockComposition.Compose(
            perWorkspace, global, currentActiveId, incomingLastActive);

        var wasVisible = IsVisible;

        SyncHostPanels(merged);

        if (_host is not null)
        {
            var next = activeId is null ? null : _hostPanels.FirstOrDefault(p => p.PanelId == activeId);
            _suppressHostActiveChanged = true;
            try
            {
                _host.ActivePanel = next;
            }
            finally
            {
                _suppressHostActiveChanged = false;
            }

            // Parity with a user click (OnHostActivePanelChanged): tell the owning source surface it
            // is focused so PanelRouter's per-(zone,scope) active tracking doesn't go stale after a
            // coordinator-driven workspace switch — otherwise the incoming tab's first Show toggle
            // decides toggle-vs-focus against stale state.
            if (activeId is not null)
            {
                if (_appShell.IsMounted(activeId))
                    _appShell.Focus(activeId);
                else
                    _activeTab?.Focus(activeId);
            }
        }

        if (wasVisible != IsVisible)
            OnPropertyChanged(nameof(IsVisible));
    }

    private void SyncHostPanels(IReadOnlyList<IPanelableViewModel> merged)
    {
        // Mutate the existing collection in place so the host's binding keeps tracking it.
        for (var i = _hostPanels.Count - 1; i >= 0; i--)
        {
            if (!merged.Contains(_hostPanels[i]))
                _hostPanels.RemoveAt(i);
        }
        for (var i = 0; i < merged.Count; i++)
        {
            var panel = merged[i];
            var existingIndex = _hostPanels.IndexOf(panel);
            if (existingIndex < 0)
                _hostPanels.Insert(Math.Min(i, _hostPanels.Count), panel);
            else if (existingIndex != i)
                _hostPanels.Move(existingIndex, i);
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
