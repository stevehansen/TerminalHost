using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Views;

namespace TerminalHost.Services;

/// <summary>
/// Manages floating status overlay windows that show terminal activity state.
/// </summary>
public class StatusOverlayService
{
    private readonly IConfigurationService _configService;
    private readonly List<StatusOverlayWindow> _overlays = [];
    private readonly object _lock = new();
    private string _currentState = "idle";
    private string _currentStatusText = "Idle";
    private System.Windows.Window? _mainWindow;
    private bool _isVisible;

    public StatusOverlayService(IConfigurationService configService)
    {
        _configService = configService;
    }

    /// <summary>Number of active overlay instances.</summary>
    public int OverlayCount
    {
        get { lock (_lock) return _overlays.Count; }
    }

    /// <summary>Whether any overlays are currently visible.</summary>
    public bool IsVisible => _isVisible;

    /// <summary>
    /// Initializes the service with the main window reference (for focus-restore).
    /// </summary>
    public void Initialize(System.Windows.Window mainWindow)
    {
        _mainWindow = mainWindow;
        RestoreOverlays();
    }

    /// <summary>
    /// Recreates overlay windows from persisted instances on startup.
    /// </summary>
    private void RestoreOverlays()
    {
        var settings = _configService.Load().Settings.StatusOverlay;
        if (settings.Instances.Count == 0) return;

        foreach (var instance in settings.Instances.ToList())
        {
            CreateOverlay();
        }
    }

    /// <summary>
    /// Creates a new overlay window. Restores position from settings if available.
    /// </summary>
    public void CreateOverlay()
    {
        var settings = _configService.Load().Settings.StatusOverlay;
        var overlay = new StatusOverlayWindow();

        // Try to find a saved instance position
        var savedInstance = settings.Instances.FirstOrDefault(i =>
            !_overlays.Any(o => o.OverlayId == i.Id));

        if (savedInstance != null)
        {
            overlay.OverlayId = savedInstance.Id;
            if (!double.IsNaN(savedInstance.Left) && !double.IsNaN(savedInstance.Top))
            {
                overlay.Left = savedInstance.Left;
                overlay.Top = savedInstance.Top;
            }
            overlay.SetSize(savedInstance.Size);
        }
        else
        {
            overlay.SetSize(settings.Size);
        }

        overlay.SetOverlayOpacity(settings.Opacity);
        overlay.UpdateState(_currentState, _currentStatusText);

        overlay.FocusMainWindowRequested += OnFocusMainWindow;
        overlay.CloseRequested += (s, _) => CloseOverlay(((StatusOverlayWindow)s!).OverlayId);
        overlay.CloseAllRequested += (_, _) => CloseAll();
        overlay.PositionChanged += OnOverlayPositionChanged;
        overlay.SizeToggled += OnOverlaySizeToggled;

        lock (_lock)
        {
            _overlays.Add(overlay);
        }

        overlay.Show();
        _isVisible = true;

        // Persist the new instance
        SaveOverlayInstances();
    }

    /// <summary>
    /// Closes a specific overlay by its ID.
    /// </summary>
    public void CloseOverlay(string id)
    {
        StatusOverlayWindow? overlay;
        lock (_lock)
        {
            overlay = _overlays.FirstOrDefault(o => o.OverlayId == id);
            if (overlay != null) _overlays.Remove(overlay);
        }

        if (overlay != null)
        {
            overlay.FocusMainWindowRequested -= OnFocusMainWindow;
            overlay.PositionChanged -= OnOverlayPositionChanged;
            overlay.Close();
        }

        if (OverlayCount == 0) _isVisible = false;
        SaveOverlayInstances();
    }

    /// <summary>
    /// Closes all overlay windows.
    /// </summary>
    public void CloseAll()
    {
        List<StatusOverlayWindow> toClose;
        lock (_lock)
        {
            toClose = [.. _overlays];
            _overlays.Clear();
        }

        foreach (var overlay in toClose)
        {
            overlay.FocusMainWindowRequested -= OnFocusMainWindow;
            overlay.PositionChanged -= OnOverlayPositionChanged;
            overlay.Close();
        }

        _isVisible = false;

        // Clear persisted instances
        var config = _configService.Load();
        config.Settings.StatusOverlay.Instances.Clear();
        _configService.Save(config);
    }

    /// <summary>
    /// Toggles overlay visibility. Creates one if none exist, otherwise toggles show/hide.
    /// </summary>
    public void Toggle()
    {
        if (OverlayCount == 0)
        {
            CreateOverlay();
        }
        else if (_isVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    /// <summary>
    /// Shows all overlay windows.
    /// </summary>
    public void Show()
    {
        lock (_lock)
        {
            foreach (var overlay in _overlays)
                overlay.Show();
        }
        _isVisible = true;
    }

    /// <summary>
    /// Hides all overlay windows.
    /// </summary>
    public void Hide()
    {
        lock (_lock)
        {
            foreach (var overlay in _overlays)
                overlay.Hide();
        }
        _isVisible = false;
    }

    /// <summary>
    /// Updates all overlays with the current activity state.
    /// </summary>
    /// <param name="state">One of: "active", "waiting", "completed", "idle"</param>
    /// <param name="statusText">Descriptive text for medium mode.</param>
    public void UpdateState(string state, string statusText)
    {
        _currentState = state;
        _currentStatusText = statusText;

        lock (_lock)
        {
            foreach (var overlay in _overlays)
                overlay.UpdateState(state, statusText);
        }
    }

    /// <summary>
    /// Handles main window activation (auto-hide if configured).
    /// </summary>
    public void OnMainWindowActivated()
    {
        var settings = _configService.Load().Settings.StatusOverlay;
        if (settings.AutoShowOnUnfocus && OverlayCount > 0)
        {
            Hide();
        }
    }

    /// <summary>
    /// Handles main window deactivation (auto-show if configured).
    /// </summary>
    public void OnMainWindowDeactivated()
    {
        var settings = _configService.Load().Settings.StatusOverlay;
        if (settings.AutoShowOnUnfocus && OverlayCount > 0)
        {
            Show();
        }
    }

    /// <summary>
    /// Shuts down all overlays (called on app exit).
    /// </summary>
    public void Shutdown()
    {
        SaveOverlayInstances();

        List<StatusOverlayWindow> toClose;
        lock (_lock)
        {
            toClose = [.. _overlays];
            _overlays.Clear();
        }

        foreach (var overlay in toClose)
        {
            overlay.Close();
        }
    }

    private void OnFocusMainWindow(object? sender, EventArgs e)
    {
        if (_mainWindow == null) return;

        if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
            _mainWindow.WindowState = System.Windows.WindowState.Normal;

        _mainWindow.Activate();
    }

    private void OnOverlayPositionChanged(object? sender, EventArgs e)
    {
        // Debounce: save after position changes settle
        SaveOverlayInstances();
    }

    private void OnOverlaySizeToggled(object? sender, StatusOverlaySize newSize)
    {
        SaveOverlayInstances();
    }

    private void SaveOverlayInstances()
    {
        var config = _configService.Load();
        var settings = config.Settings.StatusOverlay;
        settings.Instances.Clear();

        lock (_lock)
        {
            foreach (var overlay in _overlays)
            {
                settings.Instances.Add(new StatusOverlayInstanceSettings
                {
                    Id = overlay.OverlayId,
                    Left = overlay.Left,
                    Top = overlay.Top,
                    Size = overlay.CurrentSize,
                });
            }
        }

        _configService.Save(config);
    }
}
