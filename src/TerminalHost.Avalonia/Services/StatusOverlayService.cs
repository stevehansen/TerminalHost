using Avalonia.Controls;
using Avalonia.Threading;
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
    private Window? _mainWindow;
    private bool _isVisible;

    public StatusOverlayService(IConfigurationService configService)
    {
        _configService = configService;
    }

    public int OverlayCount
    {
        get { lock (_lock) return _overlays.Count; }
    }

    public bool IsVisible => _isVisible;

    public void Initialize(Window mainWindow)
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
                overlay.Position = new Avalonia.PixelPoint((int)savedInstance.Left, (int)savedInstance.Top);
            }
            else
            {
                overlay.SetDefaultPosition();
            }
            overlay.SetSize(savedInstance.Size);
        }
        else
        {
            overlay.SetSize(settings.Size);
            overlay.SetDefaultPosition();
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
        SaveOverlayInstances();
    }

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

        var config = _configService.Load();
        config.Settings.StatusOverlay.Instances.Clear();
        _configService.Save(config);
    }

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

    public void Show()
    {
        lock (_lock)
        {
            foreach (var overlay in _overlays)
                overlay.Show();
        }
        _isVisible = true;
    }

    public void Hide()
    {
        lock (_lock)
        {
            foreach (var overlay in _overlays)
                overlay.Hide();
        }
        _isVisible = false;
    }

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

    public void OnMainWindowActivated()
    {
        var settings = _configService.Load().Settings.StatusOverlay;
        if (settings.AutoShowOnUnfocus && OverlayCount > 0)
        {
            Hide();
        }
    }

    public void OnMainWindowDeactivated()
    {
        var settings = _configService.Load().Settings.StatusOverlay;
        if (settings.AutoShowOnUnfocus && OverlayCount > 0)
        {
            Show();
        }
    }

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

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
        });
    }

    private void OnOverlayPositionChanged(object? sender, EventArgs e)
    {
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
                    Left = overlay.Position.X,
                    Top = overlay.Position.Y,
                    Size = overlay.CurrentSize
                });
            }
        }

        _configService.Save(config);
    }
}
