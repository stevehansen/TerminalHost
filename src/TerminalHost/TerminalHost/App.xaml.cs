using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Views;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace TerminalHost;

public partial class App : Application
{
    private SingleInstanceService? _singleInstanceService;
    private IServiceProvider? _services;

    public new static App Current => (App)Application.Current;
    public IServiceProvider Services => _services!;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Initialize LiveCharts
        LiveCharts.Configure(config =>
            config
                // registers SkiaSharp as the library backend
                .AddSkiaSharp()
                // adds the default supported types
                .AddDefaultMappers()
                // select a theme, default is Light
                .AddDarkTheme());

        // Take control of application shutdown so the app doesn't exit when the modal setup window closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var startupArgs = CommandLineArgs.Parse(e.Args);

        if (startupArgs.IsSetupMode)
        {
            var setupViewModel = new SetupViewModel();
            var setupWindow = new SetupWindow(setupViewModel);
            if (setupWindow.ShowDialog() != true)
            {
                Shutdown(); // User cancelled setup, so exit.
                return;
            }
        }

        _singleInstanceService = new SingleInstanceService();

        // Check if another instance is already running
        if (!_singleInstanceService.TryAcquireLock())
        {
            // Another instance is running, send command line args and exit
            if (startupArgs.HasValidRequest())
            {
                SingleInstanceService.SendToRunningInstance(startupArgs);
            }

            Shutdown();
            return;
        }

        // Start the IPC server to listen for commands from other instances
        _singleInstanceService.StartPipeServer();
        _singleInstanceService.CommandReceived += OnCommandReceived;

        // Configure Services
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        _services = serviceCollection.BuildServiceProvider();

        // Initialize system tray
        InitializeSystemTray();

        // Show Main Window
        var mainWindow = _services.GetRequiredService<MainWindow>();
        
        // Ensure that closing the main window shuts down the application
        mainWindow.Closed += (s, a) => Shutdown();
        mainWindow.Show();

        // Handle command line arguments for this instance
        HandleCommandLineArgs(startupArgs);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton(_singleInstanceService!); // Register the already active instance
        services.AddSingleton<ConfigurationService>();
        services.AddSingleton<StatisticsService>();
        services.AddSingleton<SystemTrayService>();
        services.AddSingleton<ProfileRegistry>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<TerminalControlFactory>();
        services.AddSingleton<GitStatusService>();
        services.AddSingleton<LinkDetectionService>();
        services.AddSingleton<RunUrlDetectionService>();
        services.AddSingleton<ProjectDetectionService>();
        services.AddSingleton<FileEditService>();
        services.AddSingleton<FilePreviewService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ScratchPadViewModel>();
        services.AddTransient<SetupViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddTransient<SetupWindow>();
    }

    private void InitializeSystemTray()
    {
        var systemTrayService = Services.GetRequiredService<SystemTrayService>();
        var mainWindow = Services.GetRequiredService<MainWindow>();
        var configService = Services.GetRequiredService<ConfigurationService>();

        systemTrayService.Initialize(mainWindow);

        // Set enabled state from config
        var config = configService.Load();
        systemTrayService.IsEnabled = config.Settings.ShowInSystemTray;

        // Handle tray events
        systemTrayService.ShowRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => mainWindow?.BringToFront());
        };

        systemTrayService.ExitRequested += (_, _) =>
        {
            mainWindow?.ForceClose();
        };
    }

    private void OnCommandReceived(object? sender, CommandLineArgs args)
    {
        // This runs on a background thread, so dispatch to UI thread
        Dispatcher.Invoke(() =>
        {
            HandleCommandLineArgs(args);
            var mainWindow = Services.GetService<MainWindow>();
            mainWindow?.BringToFront();
        });
    }

    private void HandleCommandLineArgs(CommandLineArgs args)
    {
        var mainViewModel = Services.GetService<MainViewModel>();
        if (mainViewModel == null) return;

        // If a working directory is specified, open a project tab
        if (!string.IsNullOrEmpty(args.WorkingDir))
        {
            mainViewModel.OpenProjectTab(args.WorkingDir);
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        if (_services != null)
        {
            _services.GetService<SystemTrayService>()?.Dispose();
            _services.GetService<SingleInstanceService>()?.Dispose();
            _services.GetService<StatisticsService>()?.Dispose();
        }
    }
}
