using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost;

public partial class App : Application
{
    private IServiceProvider? _services;

    public new static App Current => (App)Application.Current!;
    public IServiceProvider Services => _services!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Configure DI
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();

            // Create main window
            var mainWindow = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            // Handle shutdown
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
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

        // ViewModels
        services.AddSingleton<DetectedLinksViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<FileViewerViewModel>();
        services.AddTransient<FilePreviewViewModel>();
        services.AddSingleton<GitFilesViewModel>();
        services.AddSingleton<GitBranchViewModel>();
        services.AddTransient<FileExplorerViewModel>();
        services.AddSingleton<ScratchPadViewModel>();
        services.AddSingleton<MarkdownPreviewViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Dispose services on shutdown
        (_services?.GetService<IStatisticsService>() as IDisposable)?.Dispose();
    }
}
