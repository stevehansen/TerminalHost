using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Views;

namespace TerminalHost;

public partial class App : Application
{
    private IServiceProvider? _services;

    public new static App Current => (App)Application.Current!;
    public IServiceProvider Services => _services!;

    /// <summary>
    /// Parsed command line arguments.
    /// </summary>
    public CommandLineArgs? StartupArgs { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Parse command line arguments
            StartupArgs = CommandLineArgs.Parse(desktop.Args ?? []);

            // Configure DI
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();

            // Load configuration to check for first run
            var configService = _services.GetRequiredService<IConfigurationService>();
            var config = configService.Load();

            // Show setup window if --setup flag or first run
            if (StartupArgs.IsSetupMode || config.IsDefault())
            {
                ShowSetupWindowThenMain(desktop, configService, config);
            }
            else
            {
                // Create main window directly
                var mainWindow = _services.GetRequiredService<MainWindow>();
                desktop.MainWindow = mainWindow;
            }

            // Handle shutdown
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowSetupWindowThenMain(
        IClassicDesktopStyleApplicationLifetime desktop,
        IConfigurationService configService,
        Domain.AppConfiguration config)
    {
        // Create SetupWindow
        var setupViewModel = new SetupViewModel(
            _services!.GetService<ISystemInfoService>(),
            _services!.GetService<IProcessService>());

        var clipboardService = _services!.GetRequiredService<IClipboardService>();
        var timerService = _services!.GetRequiredService<ITimerService>();

        var setupWindow = new SetupWindow(setupViewModel, clipboardService, timerService, isStartupMode: true);

        setupWindow.Closed += (_, _) =>
        {
            // Mark first run as completed
            if (!config.FirstRunCompleted)
            {
                config.FirstRunCompleted = true;
                config.FirstRunDate = DateTime.UtcNow;
                configService.Save(config);
            }

            // Now show the main window
            var mainWindow = _services!.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        };

        desktop.MainWindow = setupWindow;
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Platform Services (new for macOS migration)
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<ITimerService, TimerService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IScreenService, ScreenService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IFileSystem, FileSystem>();

        // Configuration Services
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IProfileRegistry, ProfileRegistry>();
        services.AddSingleton<ISessionManager, SessionManager>();

        // Terminal Services
        services.AddSingleton<ITerminalControlFactory, TerminalControlFactory>();

        // Git Services
        services.AddSingleton<IGitStatusService, GitStatusService>();
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
        services.AddSingleton<IGitPrService, GitPrService>();
        services.AddSingleton<IGitWorktreeService, GitWorktreeService>();

        // File Services
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IFilePreviewService, FilePreviewService>();
        services.AddSingleton<IFileEditService, FileEditService>();

        // Detection Services
        services.AddSingleton<ILinkDetectionService, LinkDetectionService>();
        services.AddSingleton<IProjectDetectionService, ProjectDetectionService>();
        services.AddSingleton<IRunUrlDetectionService, RunUrlDetectionService>();

        // Feature Services
        services.AddSingleton<IClaudeCommandService, ClaudeCommandService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<IAiAssistantService, AiAssistantService>();
        services.AddSingleton<IMarkdownService, MarkdownService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ITimelineService, TimelineService>();
        services.AddSingleton<IDiffParserService, DiffParserService>();
        services.AddSingleton<ITestRunnerService, TestRunnerService>();

        // ViewModels
        services.AddSingleton<DetectedLinksViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<FileViewerViewModel>();
        services.AddTransient<FilePreviewViewModel>();
        services.AddSingleton<GitFilesViewModel>();
        services.AddSingleton<GitBranchViewModel>();
        services.AddSingleton<CommitHistoryViewModel>();
        services.AddSingleton<GitStashViewModel>();
        services.AddTransient<FileExplorerViewModel>();
        services.AddSingleton<ScratchPadViewModel>();
        services.AddSingleton<TaskPanelViewModel>();
        services.AddSingleton<MarkdownPreviewViewModel>();
        services.AddSingleton<SearchAcrossFilesViewModel>();
        services.AddSingleton<FileHistoryViewModel>();
        services.AddSingleton<FileBlameViewModel>();
        services.AddSingleton<ReflogViewModel>();
        services.AddSingleton<ManageWorktreesViewModel>();
        services.AddSingleton<WorkspaceSidebarViewModel>();
        services.AddSingleton<PrReviewViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Dispose services on shutdown
        (_services?.GetService<IStatisticsService>() as IDisposable)?.Dispose();
    }
}
