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
    private ISingleInstanceService? _singleInstanceService;
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

        // Add global exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Take control of application shutdown so the app doesn't exit when the modal setup window closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var startupArgs = CommandLineArgs.Parse(e.Args);

        if (startupArgs.IsSetupMode)
        {
            var setupViewModel = new SetupViewModel();
            var setupWindow = new SetupWindow(setupViewModel);
            if (setupWindow.ShowDialog() != true)
            {
                // User clicked Close (not Continue), exit the app
                Shutdown();
                return;
            }
            // User clicked Continue, proceed to main app
        }

        _singleInstanceService = new SingleInstanceService();

        // Check if another instance is already running
        if (!startupArgs.DisableSingleInstance)
        {
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
        }

        // Configure Services
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, startupArgs);
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

    private void ConfigureServices(IServiceCollection services, CommandLineArgs args)
    {
        // Services
        services.AddSingleton(_singleInstanceService!); // Register the already active instance
        services.AddSingleton<IConfigurationService>(sp => 
            new ConfigurationService(sp.GetRequiredService<IFileSystem>(), args.UserDataDir));
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<ISystemTrayService, SystemTrayService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IProfileRegistry, ProfileRegistry>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<ITerminalControlFactory, TerminalControlFactory>();
        services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IGitStatusService, GitStatusService>();
        services.AddSingleton<ILinkDetectionService, LinkDetectionService>();
        services.AddSingleton<IRunUrlDetectionService, RunUrlDetectionService>();
        services.AddSingleton<IProjectDetectionService, ProjectDetectionService>();
        services.AddSingleton<IFileEditService, FileEditService>();
        services.AddSingleton<IFilePreviewService, FilePreviewService>();
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IClaudeCommandService, ClaudeCommandService>();
        services.AddSingleton<IGitPrService, GitPrService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<IAiAssistantService, AiAssistantService>();
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<ITestRunnerService, TestRunnerService>();
        services.AddSingleton<IMarkdownService, MarkdownService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TaskPanelViewModel>();
        services.AddSingleton<ScratchPadViewModel>();
        services.AddSingleton<GitBranchViewModel>();
        services.AddSingleton<DetectedLinksViewModel>();
        services.AddSingleton<GitFilesViewModel>();
        services.AddSingleton<FileViewerViewModel>();
        services.AddSingleton<RepositorySwitcherViewModel>();
        services.AddSingleton<TestResultsViewModel>();
        services.AddSingleton<PrReviewViewModel>();
        services.AddSingleton<MarkdownPreviewViewModel>();
        services.AddTransient<SetupViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddTransient<SetupWindow>();
    }

    private void InitializeSystemTray()
    {
        var systemTrayService = Services.GetRequiredService<ISystemTrayService>();
        var mainWindow = Services.GetRequiredService<MainWindow>();
        var configService = Services.GetRequiredService<IConfigurationService>();

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
            _services.GetService<ISystemTrayService>()?.Dispose();
            _services.GetService<ISingleInstanceService>()?.Dispose();
            _services.GetService<IStatisticsService>()?.Dispose();
            (_services.GetService<IClaudeCommandService>() as IDisposable)?.Dispose();
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = (Exception)e.ExceptionObject;
        ShowUnhandledException(ex, "Unhandled AppDomain Exception");
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ShowUnhandledException(e.Exception, "Unhandled UI Thread Exception");
        e.Handled = true; // Prevent the application from terminating immediately
    }

    private void ShowUnhandledException(Exception ex, string type)
    {
        var errorMessage = $"{type}:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
        System.Diagnostics.Debug.WriteLine(errorMessage); // Log to debug output

        MessageBox.Show(
            errorMessage,
            "Application Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        
        Shutdown(); 
    }
}
