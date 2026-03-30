using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.Views;

/// <summary>
/// A transparent overlay window for displaying toast notifications.
/// This window floats over the main window to avoid airspace issues
/// with terminal controls.
/// </summary>
public partial class ToastWindow : Window
{
    private Window? _ownerWindow;
    private bool _hasBeenShown;

    public ToastWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the toast window with the owner window and toast service.
    /// Window is NOT shown until toasts appear — prevents transparent overlay from
    /// intercepting clicks on other apps (especially on the right side of the screen).
    /// </summary>
    public void Initialize(Window owner, IToastService toastService)
    {
        _ownerWindow = owner;
        ToastContainer.DataContext = toastService;

        // Track toast count to show/hide the window
        if (toastService is ToastService ts)
        {
            ts.Toasts.CollectionChanged += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var hasToasts = ts.Toasts.Count > 0;
                    if (hasToasts)
                    {
                        if (!_hasBeenShown)
                        {
                            _hasBeenShown = true;
                            Show();
                        }
                        else if (!IsVisible)
                        {
                            IsVisible = true;
                        }
                        UpdatePosition();
                    }
                    else if (_hasBeenShown && IsVisible)
                    {
                        IsVisible = false;
                    }
                });
            };
        }

        // Track owner window changes (position updates deferred until shown)
        owner.PositionChanged += (_, _) => { if (IsVisible) UpdatePosition(); };
        owner.Resized += (_, _) => { if (IsVisible) UpdatePosition(); };
        owner.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(WindowState))
            {
                OnOwnerStateChanged();
            }
        };
        owner.Activated += (_, _) => BringToFront();
        owner.Closing += (_, _) => Close();
    }

    private void OnOwnerStateChanged()
    {
        if (_ownerWindow == null) return;

        if (_ownerWindow.WindowState == WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            Show();
            UpdatePosition();
        }
    }

    private void UpdatePosition()
    {
        if (_ownerWindow == null) return;

        // Get owner window bounds
        var ownerPos = _ownerWindow.Position;
        var ownerWidth = _ownerWindow.Bounds.Width;
        var ownerHeight = _ownerWindow.Bounds.Height;

        // Handle maximized state
        if (_ownerWindow.WindowState == WindowState.Maximized)
        {
            // Get the screen the window is on
            var screen = _ownerWindow.Screens.ScreenFromWindow(_ownerWindow);
            if (screen != null)
            {
                ownerPos = screen.WorkingArea.TopLeft;
                ownerWidth = screen.WorkingArea.Width;
                ownerHeight = screen.WorkingArea.Height;
            }
        }

        // Skip if dimensions not yet available
        if (ownerWidth <= 0 || ownerHeight <= 0)
        {
            return;
        }

        // Calculate desired position (bottom-right corner of owner)
        var newLeft = ownerPos.X + (int)(ownerWidth - Width);
        var newTop = ownerPos.Y + (int)(ownerHeight - Height);

        // Get the screen to clamp to work area bounds
        var targetScreen = Screens.ScreenFromPoint(new PixelPoint(newLeft, newTop));
        if (targetScreen != null)
        {
            var workArea = targetScreen.WorkingArea;

            // Clamp to work area bounds
            if (newLeft < workArea.X) newLeft = workArea.X;
            if (newTop < workArea.Y) newTop = workArea.Y;
            if (newLeft + Width > workArea.Right)
                newLeft = workArea.Right - (int)Width;
            if (newTop + Height > workArea.Bottom)
                newTop = workArea.Bottom - (int)Height;
        }

        Position = new PixelPoint(newLeft, newTop);
    }

    private void BringToFront()
    {
        if (IsVisible)
        {
            // Avalonia automatically manages window z-order with Topmost property
            // No P/Invoke needed - Topmost="True" in XAML handles this
            Topmost = true;
        }
    }
}