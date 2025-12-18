using System.Windows;
using System.Windows.Interop;
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

        // Position at bottom-right of owner window
        var ownerLeft = _ownerWindow.Left;
        var ownerTop = _ownerWindow.Top;
        var ownerWidth = _ownerWindow.ActualWidth;
        var ownerHeight = _ownerWindow.ActualHeight;

        // Handle maximized state
        if (_ownerWindow.WindowState == WindowState.Maximized)
        {
            // Get working area of the screen
            var screen = System.Windows.Forms.Screen.FromHandle(
                new WindowInteropHelper(_ownerWindow).Handle);
            var workArea = screen.WorkingArea;

            ownerLeft = workArea.Left;
            ownerTop = workArea.Top;
            ownerWidth = workArea.Width;
            ownerHeight = workArea.Height;
        }

        // Position toast window at bottom-right corner of owner
        Left = ownerLeft + ownerWidth - Width;
        Top = ownerTop + ownerHeight - Height;
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
