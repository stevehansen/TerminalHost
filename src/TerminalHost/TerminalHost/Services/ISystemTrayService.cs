namespace TerminalHost.Services;

public interface ISystemTrayService : IDisposable
{
    event EventHandler? ShowRequested;
    event EventHandler? ExitRequested;
    bool IsEnabled { get; set; }

    /// <summary>
    /// Initializes the system tray service with the main window.
    /// </summary>
    /// <param name="mainWindow">The main application window</param>
    void Initialize(object mainWindow);

    /// <summary>
    /// Shows a notification balloon/toast.
    /// </summary>
    /// <param name="title">Notification title</param>
    /// <param name="text">Notification text</param>
    /// <param name="icon">Icon type (0=None, 1=Info, 2=Warning, 3=Error)</param>
    void ShowBalloonTip(string title, string text, int icon = 0);
}
