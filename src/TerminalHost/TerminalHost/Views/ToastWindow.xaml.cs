using System.Windows;
using System.Windows.Interop;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.Views;

/// <summary>
/// A transparent overlay window for displaying toast notifications.
/// This window floats over the main window to avoid the WPF airspace issue
/// with terminal controls (HwndHost).
/// </summary>
public partial class ToastWindow : Window
{
    private Window? _ownerWindow;

    public ToastWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the toast window with the owner window and toast service.
    /// </summary>
    public void Initialize(Window owner, IToastService toastService)
    {
        _ownerWindow = owner;
        Owner = owner;
        ToastContainer.DataContext = toastService;

        // Position initially
        UpdatePosition();

        // Also schedule a delayed position update after layout is complete
        // This helps with multi-monitor setups where dimensions may not be immediately available
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, UpdatePosition);

        // Track owner window changes
        owner.LocationChanged += (_, _) => UpdatePosition();
        owner.SizeChanged += (_, _) => UpdatePosition();
        owner.StateChanged += OnOwnerStateChanged;
        owner.Activated += (_, _) => BringToFront();
        owner.Closing += (_, _) => Close();

        // Make window non-activating (click-through for non-toast areas)
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set extended window style to make window non-activating
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            extendedStyle | NativeMethods.WS_EX_NOACTIVATE);

        // Re-update position now that both windows are fully loaded
        UpdatePosition();
    }

    private void OnOwnerStateChanged(object? sender, EventArgs e)
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

        // Get the DPI scaling factor
        var source = PresentationSource.FromVisual(_ownerWindow);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        // Get screen info (in screen pixels)
        var hwnd = new WindowInteropHelper(_ownerWindow).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var workArea = screen.WorkingArea;

        // Convert work area from screen pixels to WPF DIUs
        var workAreaLeft = workArea.Left / dpiX;
        var workAreaTop = workArea.Top / dpiY;
        var workAreaWidth = workArea.Width / dpiX;
        var workAreaHeight = workArea.Height / dpiY;

        // Get owner window bounds in WPF DIUs
        double ownerLeft, ownerTop, ownerWidth, ownerHeight;

        if (_ownerWindow.WindowState == WindowState.Maximized)
        {
            // For maximized windows, use the working area (converted to DIUs)
            ownerLeft = workAreaLeft;
            ownerTop = workAreaTop;
            ownerWidth = workAreaWidth;
            ownerHeight = workAreaHeight;
        }
        else
        {
            ownerLeft = _ownerWindow.Left;
            ownerTop = _ownerWindow.Top;
            ownerWidth = _ownerWindow.ActualWidth;
            ownerHeight = _ownerWindow.ActualHeight;

            // Fallback if ActualWidth/Height not yet available
            if (ownerWidth <= 0) ownerWidth = _ownerWindow.Width;
            if (ownerHeight <= 0) ownerHeight = _ownerWindow.Height;

            // Handle NaN values (can happen before window is shown)
            if (double.IsNaN(ownerLeft) || double.IsNaN(ownerTop) ||
                double.IsNaN(ownerWidth) || double.IsNaN(ownerHeight) ||
                ownerWidth <= 0 || ownerHeight <= 0)
            {
                return; // Skip positioning until window has valid dimensions
            }
        }

        // Calculate desired position (bottom-right corner of owner) in WPF DIUs
        var newLeft = ownerLeft + ownerWidth - Width;
        var newTop = ownerTop + ownerHeight - Height;

        // Clamp to work area bounds (in WPF DIUs)
        if (newLeft < workAreaLeft) newLeft = workAreaLeft;
        if (newTop < workAreaTop) newTop = workAreaTop;
        if (newLeft + Width > workAreaLeft + workAreaWidth)
            newLeft = workAreaLeft + workAreaWidth - Width;
        if (newTop + Height > workAreaTop + workAreaHeight)
            newTop = workAreaTop + workAreaHeight - Height;

        Left = newLeft;
        Top = newTop;
    }

    private void BringToFront()
    {
        if (IsVisible)
        {
            // Bring window to front without activating
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter,
            int x, int y, int cx, int cy, uint flags);
    }
}
