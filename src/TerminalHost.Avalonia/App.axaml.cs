using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Posix.Services;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Views;
#if LINUX
using TerminalHost.Linux.Services;
#endif
using ITimerService = TerminalHost.Services.ITimerService;

namespace TerminalHost;

public partial class App : Application
{
    private IServiceProvider? _services;
    private ISingleInstanceService? _singleInstanceService;

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

    public override void RegisterServices()
    {
        base.RegisterServices();
#if !LINUX
        AvaloniaWebView.AvaloniaWebViewBuilder.Initialize(default);
#endif
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

            // Start pipe server for receiving hook events from CLI invocations
            _singleInstanceService = new PosixSingleInstanceService();
            _singleInstanceService.TryAcquireLock();
            _singleInstanceService.StartPipeServer();
            _singleInstanceService.HookEventReceived += (_, hookEvent) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ProcessHookEvent(hookEvent));
            };

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

                // Initialize system tray
                InitializeSystemTray(mainWindow);

                // Auto-start API server if enabled
                _ = AutoStartApiServerAsync();
            }

            // Handle dock icon click on macOS — restore minimized window
            if (OperatingSystem.IsMacOS())
            {
                MacOsDockHelper.RegisterDockClickHandler(() =>
                {
                    if (desktop.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.BringToFront();
                    }
                });
            }

            // Close all windows (Spark Canvas, etc.) when main window closes
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

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

            // Initialize system tray
            InitializeSystemTray(mainWindow);

            // Auto-start API server if enabled
            _ = AutoStartApiServerAsync();
        };

        desktop.MainWindow = setupWindow;
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Platform Services — platform service for paths/shell, Avalonia decorator for fonts
#if MACOS
        services.AddSingleton<ISystemInfoService>(_ =>
            new AvaloniaSystemInfoDecorator(new macOS.Services.MacSystemInfoService()));
#elif LINUX
        services.AddSingleton<ISystemInfoService>(_ =>
            new AvaloniaSystemInfoDecorator(new Linux.Services.LinuxSystemInfoService()));
#endif
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStatisticsService, TerminalHost.Core.Services.StatisticsService>();
        services.AddSingleton<TimerService>(); // Concrete type for both interfaces
        services.AddSingleton<ITimerService>(sp => sp.GetRequiredService<TimerService>());
        services.AddSingleton<Core.Interfaces.ITimerService>(sp => sp.GetRequiredService<TimerService>());
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IScreenService, ScreenService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IFileSystem, FileSystem>();

        // Configuration Services
        services.AddSingleton<IConfigurationService, TerminalHost.Services.ConfigurationService>();
        services.AddSingleton<IProfileRegistry, TerminalHost.Core.Services.ProfileRegistry>();
        services.AddSingleton<ISessionManager, SessionManager>();

        // Terminal Services
        services.AddSingleton<ITerminalControlFactory, TerminalControlFactory>();

        // Container Services — pass the same config directory as ConfigurationService.
#if MACOS
        // macOS: ~/Library/Application Support/TerminalHost/ since the default
        // SpecialFolder.ApplicationData resolves to ~/.config/ on macOS .NET.
        var containerConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "TerminalHost");
#elif LINUX
        // Linux: ~/.config/TerminalHost/ (XDG Base Directory)
        var containerConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TerminalHost");
#endif
        services.AddSingleton<IContainerService>(sp =>
            new TerminalHost.Core.Services.ContainerService(
                sp.GetRequiredService<IConfigurationService>(),
                sp.GetRequiredService<IProcessService>(),
                sp.GetRequiredService<IFileSystem>(),
                containerConfigDir));

        // Git Services (use Core implementations for consistency with WPF)
        services.AddSingleton<IGitStatusService, TerminalHost.Core.Services.GitStatusService>();
        services.AddSingleton<IGitHubService, global::TerminalHost.Services.GitHubService>();
        services.AddSingleton<IGitProcessRunner, TerminalHost.Core.Services.GitProcessRunner>();
        services.AddSingleton<IGitPrService, TerminalHost.Core.Services.GitPrService>();
        services.AddSingleton<IGitWorktreeService, global::TerminalHost.Services.GitWorktreeService>();
        services.AddSingleton<IGitIgnoreService, TerminalHost.Core.Services.GitIgnoreService>();

        // File Services
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IFilePreviewService, FilePreviewService>();
        services.AddSingleton<IFileEditService, TerminalHost.Core.Services.FileEditService>();

        // Detection Services
        services.AddSingleton<ILinkDetectionService, LinkDetectionService>();
        services.AddSingleton<IProjectDetectionService, TerminalHost.Core.Services.ProjectDetectionService>();
        services.AddSingleton<IRunUrlDetectionService, TerminalHost.Core.Services.RunUrlDetectionService>();
        services.AddSingleton<IInputPromptDetectionService, TerminalHost.Core.Services.InputPromptDetectionService>();

        // System Tray
        services.AddSingleton<ISystemTrayService, SystemTrayService>();

        // Feature Services
        services.AddSingleton<IClaudeCommandService, TerminalHost.Core.Services.ClaudeCommandService>();
        services.AddSingleton<IClaudeSessionIndexService, TerminalHost.Core.Services.ClaudeSessionIndexService>();
        services.AddSingleton<IClaudeTaskFileService, TerminalHost.Core.Services.ClaudeTaskFileService>();
        services.AddSingleton<IClaudeTaskDetectionService, TerminalHost.Core.Services.ClaudeTaskDetectionService>();
        services.AddSingleton<ITaskService, TerminalHost.Core.Services.TaskService>();
        services.AddSingleton<IAiAssistantService, TerminalHost.Core.Services.AiAssistantService>();
        services.AddSingleton<IMarkdownService, TerminalHost.Core.Services.MarkdownService>();
#if MACOS
        services.AddSingleton<ISoundService, macOS.Services.MacSoundService>();
#elif LINUX
        services.AddSingleton<ISoundService, Linux.Services.LinuxSoundService>();
#endif

        // Voice Command Services (Whisper-based, cross-platform)
        services.AddSingleton<IAudioCaptureService, Posix.Services.PosixAudioCaptureService>();
        services.AddSingleton<TerminalHost.Core.Services.WhisperModelManager>();
        services.AddSingleton<IVoiceCommandService, Posix.Services.PosixWhisperVoiceCommandService>();

        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IHookInstaller, TerminalHost.Posix.Services.PosixHookInstaller>();
        services.AddSingleton<ITimelineService, TerminalHost.Core.Services.TimelineService>();
        services.AddSingleton<IDiffParserService, TerminalHost.Core.Services.DiffParserService>();
        services.AddSingleton<IInvisibleChangeService, TerminalHost.Core.Services.InvisibleChangeService>();
        services.AddSingleton<ITestRunnerService, global::TerminalHost.Services.TestRunnerService>();
        services.AddSingleton<IAiExecutionService, AiExecutionService>();

        // Session Activity & Transcript Watching
        services.AddSingleton<ITranscriptWatcher, TranscriptWatcher>();
        services.AddSingleton<ISessionActivityService, TerminalHost.Core.Services.SessionActivityService>();

        // API & Webhooks Services
        services.AddSingleton<IEventAggregatorService, EventAggregatorService>();
        services.AddSingleton<IWebhookDeliveryService, WebhookDeliveryService>();
        services.AddSingleton<ICollabService, CollabService>();
        services.AddSingleton<McpHandler>();
        services.AddSingleton<IApiServer, ApiServer>();

        // ViewModels
        services.AddSingleton<DetectedLinksViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<FileViewerViewModel>();
        services.AddTransient<FilePreviewViewModel>();
        services.AddSingleton<GitFilesViewModel>();
        services.AddSingleton<GitBranchViewModel>();
        services.AddSingleton<CommitHistoryViewModel>();
        services.AddSingleton<GitStashViewModel>();
        services.AddSingleton<GitTagsViewModel>();
        services.AddTransient<FileExplorerViewModel>();
        services.AddSingleton<ScratchPadViewModel>();
        services.AddSingleton<TaskPanelViewModel>();
        services.AddSingleton<ClaudeTasksPanelViewModel>();
        services.AddSingleton<MarkdownPreviewViewModel>();
        services.AddSingleton<SearchAcrossFilesViewModel>();
        services.AddSingleton<FileHistoryViewModel>();
        services.AddSingleton<FileBlameViewModel>();
        services.AddSingleton<ReflogViewModel>();
        services.AddSingleton<ManageWorktreesViewModel>();
        services.AddSingleton<WorkspaceSidebarViewModel>();
        services.AddSingleton<PrReviewViewModel>();
        services.AddSingleton<RecentFeaturesViewModel>();
        services.AddSingleton<BranchComparisonViewModel>();
        services.AddSingleton<MergeConflictViewModel>();
        services.AddSingleton<UnifiedGitPanelViewModel>();

        // Windows
        services.AddSingleton<StatusOverlayService>();

        services.AddSingleton<MainWindow>();
    }

    private async Task AutoStartApiServerAsync()
    {
        try
        {
            if (_services == null) return;

            // Wire ActivityEventProcessed → EventAggregator for SSE distribution
            var activityService = _services.GetService<ISessionActivityService>();
            var eventAggregator = _services.GetService<IEventAggregatorService>();
            if (activityService != null && eventAggregator != null)
            {
                activityService.ActivityEventProcessed += (_, evt) =>
                {
                    eventAggregator.Publish(new ApiEvent
                    {
                        Type = "activity.event",
                        Data = new
                        {
                            type = evt.Type.ToString(),
                            sessionId = evt.SessionId,
                            agentId = evt.AgentId,
                            timestamp = evt.Timestamp,
                            data = evt.Data
                        }
                    });
                };
            }

            var config = _services.GetRequiredService<IConfigurationService>().Load();
            if (config.Settings.Api.Enabled)
            {
                var apiServer = _services.GetRequiredService<IApiServer>();

                // Route hook events to SessionActivityService and TimelineService
                apiServer.HookEventReceived += (_, hookEvent) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ProcessHookEvent(hookEvent));
                };

                await apiServer.StartAsync();

                // Auto-install Claude Code hooks so sessions are tracked
                var timelineService = _services.GetService<ITimelineService>();
                if (timelineService != null && !timelineService.AreHooksInstalled())
                {
                    timelineService.InstallHooks();
                }
                else
                {
                    timelineService?.UpgradeHooksIfNeeded();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to auto-start API server: {ex.Message}");
            _services?.GetService<IToastService>()?.Show($"API server failed to start: {ex.Message}", ToastType.Error);
        }
    }

    private void ProcessHookEvent(HookEvent hookEvent)
    {
        if (_services == null) return;

        // For Container sessions, fix the transcript path: the proxy translates
        // /home/developer → host profile, but Docker overlay mounts map the container
        // project key to a different host project key in .claude/projects/.
        if (hookEvent.Source == SessionSource.Container &&
            !string.IsNullOrEmpty(hookEvent.TranscriptPath) &&
            !string.IsNullOrEmpty(hookEvent.Cwd))
        {
            hookEvent.TranscriptPath = ResolveContainerTranscriptPath(
                hookEvent.TranscriptPath, hookEvent.Cwd);
        }

        // Route to SessionActivityService for rich activity tracking
        var activityService = _services.GetService<ISessionActivityService>();
        activityService?.ProcessHookEvent(hookEvent);

        // Route to TimelineService for live session tracking
        var timelineService = _services.GetService<ITimelineService>();
        if (timelineService != null)
        {
            switch (hookEvent.EventType)
            {
                case HookEventType.SessionStart:
                    timelineService.HandleSessionStart(hookEvent);
                    break;
                case HookEventType.ToolStart:
                    timelineService.HandleToolStart(hookEvent);
                    break;
                case HookEventType.ToolEnd:
                case HookEventType.ToolError:
                    timelineService.HandleToolEnd(hookEvent);
                    break;
                case HookEventType.FileChanged:
                    timelineService.HandleFileChanged(hookEvent);
                    break;
                case HookEventType.SessionStop:
                case HookEventType.SessionEnd:
                    _ = timelineService.HandleSessionStopAsync(hookEvent);
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves a container-translated transcript path to the correct host path.
    /// Docker overlay mounts map container project key → host project key under .claude/projects/.
    /// </summary>
    private static string ResolveContainerTranscriptPath(string translatedPath, string hostCwd)
    {
        var normalized = translatedPath.Replace('\\', '/');
        const string marker = ".claude/projects/";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return translatedPath;

        var afterMarker = normalized[(idx + marker.Length)..];
        var slashIdx = afterMarker.IndexOf('/');
        if (slashIdx < 0)
            return translatedPath;

        var relativePath = afterMarker[(slashIdx + 1)..];
        var hostProjectKey = Core.Services.ContainerService.EncodeClaudeProjectPath(hostCwd);
        var claudeDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return System.IO.Path.Combine(claudeDir, "projects", hostProjectKey, relativePath);
    }

    private void InitializeSystemTray(MainWindow mainWindow)
    {
        var systemTrayService = _services!.GetRequiredService<ISystemTrayService>();
        var configService = _services!.GetRequiredService<IConfigurationService>();

        systemTrayService.Initialize(mainWindow);

        // Set enabled state from config
        var config = configService.Load();
        systemTrayService.IsEnabled = config.Settings.ShowInSystemTray;

        // Handle tray events
        systemTrayService.ShowRequested += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.BringToFront());
        };

        systemTrayService.ExitRequested += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => mainWindow.ForceClose());
        };
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Dispose API and webhook services
        _services?.GetService<IApiServer>()?.Dispose();
        (_services?.GetService<IWebhookDeliveryService>() as IDisposable)?.Dispose();

        // Dispose transcript watcher, timeline service, and single instance service
        (_services?.GetService<ITranscriptWatcher>() as IDisposable)?.Dispose();
        (_services?.GetService<ITimelineService>() as IDisposable)?.Dispose();
        _singleInstanceService?.Dispose();

        // Dispose system tray
        _services?.GetService<ISystemTrayService>()?.Dispose();

        // Dispose other services
        (_services?.GetService<IStatisticsService>() as IDisposable)?.Dispose();
        (_services?.GetService<IClaudeCommandService>() as IDisposable)?.Dispose();
    }
}