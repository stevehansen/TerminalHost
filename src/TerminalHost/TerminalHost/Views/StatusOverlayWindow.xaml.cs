using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using TerminalHost.Core.Domain;

namespace TerminalHost.Views;

/// <summary>
/// Floating always-on-top status overlay that shows terminal activity state.
/// </summary>
public partial class StatusOverlayWindow : Window
{
    private StatusOverlaySize _currentSize = StatusOverlaySize.Medium;
    private Storyboard? _spinnerStoryboard;
    private Storyboard? _pulseStoryboard;
    private string _currentState = "idle";

    /// <summary>Unique ID for this overlay instance (for position persistence).</summary>
    public string OverlayId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Fired when the user clicks to focus the main window.</summary>
    public event EventHandler? FocusMainWindowRequested;

    /// <summary>Fired when the user closes this overlay.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Fired when the user requests closing all overlays.</summary>
    public event EventHandler? CloseAllRequested;

    /// <summary>Fired when the overlay position changes (for persistence).</summary>
    public event EventHandler? PositionChanged;

    /// <summary>Fired when the size mode is toggled.</summary>
    public event EventHandler<StatusOverlaySize>? SizeToggled;

    public StatusOverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        LocationChanged += (_, _) => PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set WS_EX_NOACTIVATE so clicking doesn't steal focus from other apps
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            extendedStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        _spinnerStoryboard = (Storyboard)FindResource("SpinnerAnimation");
        _pulseStoryboard = (Storyboard)FindResource("PulseAnimation");

        // Default position: bottom-right of primary screen
        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 20;
            Top = workArea.Bottom - ActualHeight - 20;
        }
    }

    /// <summary>
    /// Updates the visual state of the overlay.
    /// </summary>
    /// <param name="state">One of: "active", "waiting", "completed", "idle"</param>
    /// <param name="statusText">Text to show in medium mode.</param>
    public void UpdateState(string state, string statusText)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateState(state, statusText));
            return;
        }

        // Stop all animations
        if (_currentState == "active") _spinnerStoryboard?.Stop(this);
        if (_currentState == "waiting") _pulseStoryboard?.Stop(this);

        _currentState = state;

        // Hide all icons
        SpinnerIcon.Visibility = Visibility.Collapsed;
        WaitingIcon.Visibility = Visibility.Collapsed;
        CompletedIcon.Visibility = Visibility.Collapsed;
        IdleIcon.Visibility = Visibility.Collapsed;

        switch (state)
        {
            case "active":
                SpinnerIcon.Visibility = Visibility.Visible;
                _spinnerStoryboard?.Begin(this, true);
                break;
            case "waiting":
                WaitingIcon.Visibility = Visibility.Visible;
                _pulseStoryboard?.Begin(this, true);
                break;
            case "completed":
                CompletedIcon.Visibility = Visibility.Visible;
                break;
            default: // idle
                IdleIcon.Visibility = Visibility.Visible;
                break;
        }

        StatusText.Text = statusText;
    }

    /// <summary>
    /// Sets the overlay size mode.
    /// </summary>
    public void SetSize(StatusOverlaySize size)
    {
        _currentSize = size;
        StatusText.Visibility = size == StatusOverlaySize.Medium ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Sets the overlay opacity.
    /// </summary>
    public void SetOverlayOpacity(double opacity)
    {
        RootBorder.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb((byte)(opacity * 255), 37, 37, 37));
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            // Single click while holding Ctrl: drag the window
            // Single click without Ctrl: focus main window
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                DragMove();
            }
            else
            {
                // Left-click: focus main window
                FocusMainWindowRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow context menu to open (drag on right-click)
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            DragMove();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // Allow dragging with left button held down
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnToggleSize(object sender, RoutedEventArgs e)
    {
        var newSize = _currentSize == StatusOverlaySize.Small
            ? StatusOverlaySize.Medium
            : StatusOverlaySize.Small;
        SetSize(newSize);
        SizeToggled?.Invoke(this, newSize);
    }

    private void OnCloseOverlay(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseAllOverlays(object sender, RoutedEventArgs e)
    {
        CloseAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    }
}
