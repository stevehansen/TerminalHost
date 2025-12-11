using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace TerminalHost.Services;

public class SystemTrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private bool _isEnabled;
    private bool _disposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                UpdateTrayVisibility();
            }
        }
    }

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;

        _notifyIcon = new NotifyIcon
        {
            Text = "TerminalHost",
            Visible = false
        };

        // Load the icon from resource
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
        if (File.Exists(iconPath))
        {
            _notifyIcon.Icon = new Icon(iconPath);
        }
        else
        {
            // Try to get from application resources
            try
            {
                var resourceStream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico"));
                if (resourceStream != null)
                {
                    _notifyIcon.Icon = new Icon(resourceStream.Stream);
                }
            }
            catch
            {
                // Use system default if icon not found
                _notifyIcon.Icon = SystemIcons.Application;
            }
        }

        // Create context menu
        var contextMenu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem("Show TerminalHost");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        UpdateTrayVisibility();
    }

    public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_notifyIcon != null && _isEnabled)
        {
            _notifyIcon.ShowBalloonTip(3000, title, text, icon);
        }
    }

    private void UpdateTrayVisibility()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = _isEnabled;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
