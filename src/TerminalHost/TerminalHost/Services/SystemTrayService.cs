namespace TerminalHost.Services;

/// <summary>
/// macOS implementation of system tray service.
/// Note: Full implementation requires native macOS interop for NSStatusBar.
/// This is a stub that can be enhanced later.
/// </summary>
internal sealed class SystemTrayService : ISystemTrayService
{
    private bool _isEnabled;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value; // No-op for now
    }

    public void Initialize(object mainWindow)
    {
        // No-op: macOS menu bar implementation would go here
        // Could use Avalonia.Native or direct ObjC interop
    }

    public void ShowBalloonTip(string title, string text, int icon = 0)
    {
        // macOS notification could be implemented via NSUserNotificationCenter
        // or the newer UNUserNotificationCenter
        System.Diagnostics.Debug.WriteLine($"[Notification] {title}: {text}");
    }

    public void Dispose()
    {
        // Nothing to dispose in stub
    }

    // Suppress unused event warnings - these will be used when full implementation is added
    private void OnShowRequested() => ShowRequested?.Invoke(this, EventArgs.Empty);
    private void OnExitRequested() => ExitRequested?.Invoke(this, EventArgs.Empty);
}
