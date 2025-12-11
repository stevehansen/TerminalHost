using System.Windows;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using Application = System.Windows.Application;

namespace TerminalHost;

public partial class App : Application
{
    private SingleInstanceService? _singleInstanceService;
    private SystemTrayService? _systemTrayService;
    private ConfigurationService? _configService;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstanceService = new SingleInstanceService();

        // Check if another instance is already running
        if (!_singleInstanceService.TryAcquireLock())
        {
            // Another instance is running, send command line args and exit
            var args = CommandLineArgs.Parse(e.Args);
            if (args.HasValidRequest())
            {
                SingleInstanceService.SendToRunningInstance(args);
            }

            Shutdown();
            return;
        }

        // Start the IPC server to listen for commands from other instances
        _singleInstanceService.StartPipeServer();
        _singleInstanceService.CommandReceived += OnCommandReceived;

        // Create services
        _configService = new ConfigurationService();
        var profileRegistry = new ProfileRegistry(_configService);
        var sessionManager = new SessionManager();
        var terminalFactory = new TerminalControlFactory();

        // Create system tray service
        _systemTrayService = new SystemTrayService();

        // Create the main view model
        _mainViewModel = new MainViewModel(profileRegistry, sessionManager, terminalFactory, _configService);

        // Create and show the main window
        _mainWindow = new MainWindow(_mainViewModel, _configService, _systemTrayService);
        _mainWindow.Show();

        // Initialize system tray
        InitializeSystemTray();

        // Handle command line arguments for this instance
        var startupArgs = CommandLineArgs.Parse(e.Args);
        HandleCommandLineArgs(startupArgs);
    }

    private void InitializeSystemTray()
    {
        if (_systemTrayService == null || _mainWindow == null || _configService == null) return;

        _systemTrayService.Initialize(_mainWindow);

        // Set enabled state from config
        var config = _configService.Load();
        _systemTrayService.IsEnabled = config.Settings.ShowInSystemTray;

        // Handle tray events
        _systemTrayService.ShowRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => _mainWindow?.BringToFront());
        };

        _systemTrayService.ExitRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => Shutdown());
        };
    }

    private void OnCommandReceived(object? sender, CommandLineArgs args)
    {
        // This runs on a background thread, so dispatch to UI thread
        Dispatcher.Invoke(() =>
        {
            HandleCommandLineArgs(args);
            _mainWindow?.BringToFront();
        });
    }

    private void HandleCommandLineArgs(CommandLineArgs args)
    {
        if (_mainViewModel == null) return;

        // If a working directory is specified, open a project tab
        if (!string.IsNullOrEmpty(args.WorkingDir))
        {
            _mainViewModel.OpenProjectTab(args.WorkingDir);
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _systemTrayService?.Dispose();
        _singleInstanceService?.Dispose();
    }
}
