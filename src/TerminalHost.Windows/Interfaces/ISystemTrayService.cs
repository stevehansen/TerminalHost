using System.Windows;
using System.Windows.Forms;

namespace TerminalHost.Windows.Interfaces;

public interface ISystemTrayService : IDisposable
{
    event EventHandler? ShowRequested;
    event EventHandler? ExitRequested;
    bool IsEnabled { get; set; }
    void Initialize(Window mainWindow);
    void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info);
}
