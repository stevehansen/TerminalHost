using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TerminalHost.Services;

/// <summary>
/// Helper class to enable dark mode title bar on Windows 10/11.
/// </summary>
public static class DarkModeHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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
}
