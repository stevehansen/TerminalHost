using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Views;
using ITimerService = TerminalHost.Services.ITimerService;

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
        AppConfiguration config)
    {
        // Create SetupWindow
        var setupViewModel = new SetupViewModel(
            _services!.GetService<IProcessService>());

        var clipboardService = _services!.GetRequiredService<IClipboardService>();
        var timerService = _services!.GetRequiredService<ITimerService>();

        var setupWindow = new SetupWindow(setupViewModel, clipboardService, timerService, isStartupMode: true);

        setupWindow.Closed += (_, _) =>
        {
            // Mark first run as completed
            if (!config.Settings.FirstRunCompleted)
            {
                config.Settings.FirstRunCompleted = true;
                config.Settings.FirstRunDate = DateTime.UtcNow;
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
        services.AddSingleton<IProfileRegistry, TerminalHost.Core.Services.ProfileRegistry>();
        services.AddSingleton<ISessionManager, SessionManager>();

        // Terminal Services
        services.AddSingleton<ITerminalControlFactory, TerminalControlFactory>();

        // Git Services (use Core implementations for consistency with WPF)
        services.AddSingleton<IGitStatusService, TerminalHost.Core.Services.GitStatusService>();
        services.AddSingleton<IGitHubService, TerminalHost.Core.Services.GitHubService>();
        services.AddSingleton<IGitProcessRunner, TerminalHost.Core.Services.GitProcessRunner>();
        services.AddSingleton<IGitPrService, GitPrService>();
        services.AddSingleton<IGitWorktreeService, GitWorktreeService>();

        // File Services
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IFilePreviewService, FilePreviewService>();
        services.AddSingleton<IFileEditService, TerminalHost.Core.Services.FileEditService>();

        // Detection Services
        services.AddSingleton<ILinkDetectionService, LinkDetectionService>();
        services.AddSingleton<IProjectDetectionService, TerminalHost.Core.Services.ProjectDetectionService>();
        services.AddSingleton<IRunUrlDetectionService, TerminalHost.Core.Services.RunUrlDetectionService>();

        // Feature Services
        services.AddSingleton<IClaudeCommandService, TerminalHost.Core.Services.ClaudeCommandService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<IAiAssistantService, TerminalHost.Core.Services.AiAssistantService>();
        services.AddSingleton<IMarkdownService, TerminalHost.Core.Services.MarkdownService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ITimelineService, TimelineService>();
        services.AddSingleton<IDiffParserService, TerminalHost.Core.Services.DiffParserService>();
        services.AddSingleton<ITestRunnerService, TerminalHost.Core.Services.TestRunnerService>();

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