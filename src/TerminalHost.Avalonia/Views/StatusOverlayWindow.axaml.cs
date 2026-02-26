using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TerminalHost.Core.Domain;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace TerminalHost.Views;

/// <summary>
/// Floating always-on-top status overlay that shows terminal activity state.
/// Avalonia implementation for macOS.
/// </summary>
public partial class StatusOverlayWindow : Window
{
    private StatusOverlaySize _currentSize = StatusOverlaySize.Medium;

    /// <summary>Gets the current size mode of the overlay.</summary>
    public StatusOverlaySize CurrentSize => _currentSize;

    /// <summary>Unique ID for this overlay instance (for position persistence).</summary>
    public string OverlayId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Fired when the user clicks to focus the main window.</summary>
    public event EventHandler? FocusMainWindowRequested;

    /// <summary>Fired when the user closes this overlay.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Fired when the user requests closing all overlays.</summary>
    public event EventHandler? CloseAllRequested;

    /// <summary>Fired when the overlay position changes (for persistence).</summary>
    public new event EventHandler? PositionChanged;

    /// <summary>Fired when the size mode is toggled.</summary>
    public event EventHandler<StatusOverlaySize>? SizeToggled;

    public StatusOverlayWindow()
    {
        InitializeComponent();

        // Track position changes for persistence
        base.PositionChanged += (_, _) => PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the visual state of the overlay.
    /// </summary>
    /// <param name="state">One of: "active", "waiting", "completed", "idle"</param>
    /// <param name="statusText">Text to show in medium mode.</param>
    public void UpdateState(string state, string statusText)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateState(state, statusText));
            return;
        }

        var spinnerIcon = this.FindControl<Ellipse>("SpinnerIcon");
        var waitingIcon = this.FindControl<Ellipse>("WaitingIcon");
        var completedIcon = this.FindControl<AvaloniaPath>("CompletedIcon");
        var idleIcon = this.FindControl<Ellipse>("IdleIcon");
        var statusTextBlock = this.FindControl<TextBlock>("StatusText");

        // Hide all icons
        if (spinnerIcon != null) spinnerIcon.IsVisible = false;
        if (waitingIcon != null) waitingIcon.IsVisible = false;
        if (completedIcon != null) completedIcon.IsVisible = false;
        if (idleIcon != null) idleIcon.IsVisible = false;

        // Show the matching icon based on state
        // Avalonia Style.Animations trigger automatically when IsVisible becomes True
        switch (state)
        {
            case "active":
                if (spinnerIcon != null) spinnerIcon.IsVisible = true;
                break;
            case "waiting":
                if (waitingIcon != null) waitingIcon.IsVisible = true;
                break;
            case "completed":
                if (completedIcon != null) completedIcon.IsVisible = true;
                break;
            default: // idle
                if (idleIcon != null) idleIcon.IsVisible = true;
                break;
        }

        if (statusTextBlock != null)
            statusTextBlock.Text = statusText;
    }

    /// <summary>
    /// Sets the overlay size mode.
    /// </summary>
    public void SetSize(StatusOverlaySize size)
    {
        _currentSize = size;
        var statusTextBlock = this.FindControl<TextBlock>("StatusText");
        if (statusTextBlock != null)
            statusTextBlock.IsVisible = size == StatusOverlaySize.Medium;
    }

    /// <summary>
    /// Sets the overlay opacity.
    /// </summary>
    public void SetOverlayOpacity(double opacity)
    {
        var rootBorder = this.FindControl<Border>("RootBorder");
        if (rootBorder != null)
            rootBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 37, 37, 37));
    }

    /// <summary>
    /// Sets the default position to bottom-right of the primary screen.
    /// Called on first show if position has not been set.
    /// </summary>
    public void SetDefaultPosition()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        var workArea = screen.WorkingArea;
        // Estimate window size since SizeToContent may not have completed yet
        var estimatedWidth = _currentSize == StatusOverlaySize.Small ? 48 : 160;
        var estimatedHeight = 40;

        Position = new PixelPoint(
            workArea.Right - estimatedWidth - 20,
            workArea.Bottom - estimatedHeight - 20);
    }

    private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                // Cmd+Click: drag the window
                BeginMoveDrag(e);
            }
            else
            {
                // Regular click: focus the main window
                FocusMainWindowRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnToggleSize(object? sender, RoutedEventArgs e)
    {
        var newSize = _currentSize == StatusOverlaySize.Small
            ? StatusOverlaySize.Medium
            : StatusOverlaySize.Small;
        SetSize(newSize);
        SizeToggled?.Invoke(this, newSize);
    }

    private void OnCloseOverlay(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseAllOverlays(object? sender, RoutedEventArgs e)
    {
        CloseAllRequested?.Invoke(this, EventArgs.Empty);
    }
}
