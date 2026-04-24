using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TerminalHost.Services;

/// <summary>
/// Cross-platform system tray service using Avalonia's built-in TrayIcon API.
/// Works on macOS (NSStatusBar) and Linux (D-Bus StatusNotifier/AppIndicator).
/// </summary>
internal sealed class SystemTrayService : ISystemTrayService
{
    private TrayIcon? _trayIcon;
    private bool _isEnabled;
    private bool _disposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                UpdateTrayVisibility();
            }
        }
    }

    public void Initialize(object mainWindow)
    {
        try
        {
            _trayIcon = new TrayIcon
            {
                ToolTipText = "TerminalHost",
                IsVisible = false
            };

            // Load icon from Avalonia embedded resources
            try
            {
                var uri = new Uri("avares://host/Resources/app.png");
                using var stream = AssetLoader.Open(uri);
                var bitmap = new Bitmap(stream);
                _trayIcon.Icon = new WindowIcon(bitmap);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemTray] Failed to load tray icon: {ex.Message}");
            }

            // Create context menu
            var menu = new NativeMenu();

            var showItem = new NativeMenuItem("Show TerminalHost");
            showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

            menu.Add(showItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            _trayIcon.Menu = menu;

            // Click to show window
            _trayIcon.Clicked += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

            UpdateTrayVisibility();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemTray] Failed to initialize: {ex.Message}");
        }
    }

    public void ShowBalloonTip(string title, string text, int icon = 0)
    {
        // Avalonia TrayIcon doesn't support balloon tips.
        // Callers should use IToastService for user-visible notifications.
        Debug.WriteLine($"[SystemTray Notification] {title}: {text}");
    }

    private void UpdateTrayVisibility()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = _isEnabled;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
