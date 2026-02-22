using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TerminalHost.Windows.Platform;

/// <summary>
/// Helper class to enable dark mode title bar on Windows 10/11.
/// </summary>
public static class DarkModeHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Enables dark mode title bar for the specified window.
    /// Call this after the window's SourceInitialized event.
    /// </summary>
    public static void EnableDarkMode(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var useDarkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
    }

    /// <summary>
    /// Returns a WndProc hook that fills the window background with a dark color
    /// on WM_ERASEBKGND, preventing the white flash before WPF's first render.
    /// Add this hook in the window's SourceInitialized handler.
    /// </summary>
    /// <param name="bgColor">BGR color value (default: 0x1E1E1E = #1E1E1E dark background)</param>
    public static HwndSourceHook CreateDarkBackgroundHook(uint bgColor = 0x1E1E1E)
    {
        return (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            const int WM_ERASEBKGND = 0x0014;
            if (msg == WM_ERASEBKGND)
            {
                var rect = new RECT();
                GetClientRect(hwnd, ref rect);
                var brush = CreateSolidBrush(bgColor);
                FillRect(wParam, ref rect, brush);
                DeleteObject(brush);
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        };
    }
}
