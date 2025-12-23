using System.Windows;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Manages panel windows for all panelable ViewModels.
/// Centralizes window creation, tracking, and lifecycle management.
/// </summary>
public class PanelWindowManager
{
    private readonly Dictionary<string, Views.PanelWindow> _windows = new();
    private readonly Window _owner;

    public PanelWindowManager(Window owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Shows a panel in window mode, creating a new window if needed.
    /// </summary>
    /// <param name="panel">The panel to show.</param>
    /// <param name="onDockRequested">Callback when dock is requested from the window.</param>
    public void ShowWindow(IPanelableViewModel panel, Action<IPanelableViewModel> onDockRequested)
    {
        if (!_windows.TryGetValue(panel.PanelId, out var window) || !window.IsLoaded)
        {
            window = new Views.PanelWindow
            {
                DataContext = panel,
                Owner = _owner,
                Width = panel.Width,
                Height = panel.Height
            };

            window.DockRequested += (s, p) => onDockRequested(p);
            window.Closed += (s, e) =>
            {
                _windows.Remove(panel.PanelId);
                // Reset to panel state for next toggle
                panel.DisplayState = PanelDisplayState.Panel;
            };

            _windows[panel.PanelId] = window;
            window.Show();
        }
        else
        {
            window.Activate();
        }
    }

    /// <summary>
    /// Closes the window for a specific panel if it exists.
    /// </summary>
    /// <param name="panelId">The panel ID.</param>
    public void CloseWindow(string panelId)
    {
        if (_windows.TryGetValue(panelId, out var window))
        {
            window.Close();
            _windows.Remove(panelId);
        }
    }

    /// <summary>
    /// Checks if a window exists for the specified panel.
    /// </summary>
    public bool HasWindow(string panelId)
    {
        return _windows.TryGetValue(panelId, out var window) && window.IsLoaded;
    }

    /// <summary>
    /// Gets the window for a panel if it exists.
    /// </summary>
    public Views.PanelWindow? GetWindow(string panelId)
    {
        return _windows.TryGetValue(panelId, out var window) && window.IsLoaded ? window : null;
    }

    /// <summary>
    /// Closes all panel windows.
    /// </summary>
    public void CloseAll()
    {
        foreach (var window in _windows.Values.ToList())
        {
            window.Close();
        }
        _windows.Clear();
    }
}
