using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Views;
using TerminalHost.Windows.Interfaces;
using TerminalHost.Windows.Services;

namespace TerminalHost;

public partial class App : Application
{
    private ISingleInstanceService? _singleInstanceService;
    private IServiceProvider? _services;
    private bool _ignoreFocusChange;

    public new static App Current => (App)Application.Current;
    public IServiceProvider Services => _services!;

    public App()
    {
        // Register class handlers to fix WPF popup focus synchronization issues
        // when the main window isn't active
        EventManager.RegisterClassHandler(
            typeof(Popup),
            Keyboard.GotKeyboardFocusEvent,
            new RoutedEventHandler(OnPopupGotKeyboardFocus));

        // Also handle mouse clicks to force activation before focus is set
        EventManager.RegisterClassHandler(
            typeof(Popup),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPopupPreviewMouseDown),
            handledEventsToo: true);
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var startupArgs = CommandLineArgs.Parse(e.Args);

        // Handle Claude Code hook callbacks early (before any WPF initialization)
        // Hooks should read stdin, forward event, and exit immediately
        if (startupArgs.IsHookMode)
        {
            HandleHookAndExit(startupArgs.HookType!);
            return;
        }

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
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Take control of application shutdown so the app doesn't exit when the modal setup window closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Check for first-run setup (automatic, before explicit /setup check)
        if (IsFirstRun(startupArgs))
        {
            var setupViewModel = new SetupViewModel();
            var setupWindow = new SetupWindow(setupViewModel, isStartupMode: true);
            if (setupWindow.ShowDialog() != true)
            {
                // User clicked Close (not Continue), exit the app
                Shutdown();
                return;
            }
            // User clicked Continue - mark first run as completed
            MarkFirstRunCompleted();
        }
        // Check for explicit /setup flag
        else if (startupArgs.IsSetupMode)
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
                // Another instance is running
                if (startupArgs.HasValidRequest())
                {
                    // Has path or show-dialog command - send and exit
                    SingleInstanceService.SendToRunningInstance(startupArgs);
                }
                else
                {
                    // No arguments - check behavior setting
                    var behavior = GetSingleInstanceBehavior();

                    switch (behavior)
                    {
                        case SingleInstanceBehavior.ShowDialog:
                            // Tell first instance to show choice dialog
                            SingleInstanceService.SendToRunningInstance(
                                new CommandLineArgs { IsShowChoiceDialog = true });
                            break;

                        case SingleInstanceBehavior.SilentFocus:
                            // Just send empty args to focus existing window
                            SingleInstanceService.SendToRunningInstance(new CommandLineArgs());
                            break;

                        case SingleInstanceBehavior.AllowMultiple:
                            // Restart with -multi flag
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = Environment.ProcessPath,
                                Arguments = "-multi",
                                UseShellExecute = true
                            });
                            break;
                    }
                }

                Shutdown();
                return;
            }

            // Start the IPC server to listen for commands from other instances
            _singleInstanceService.StartPipeServer();
            _singleInstanceService.CommandReceived += OnCommandReceived;
            _singleInstanceService.HookEventReceived += OnHookEventReceived;
        }

        // Configure Services
        var sp = TerminalHost.Core.Services.StartupProfiler.Instance;
        sp.Log("App.OnStartup — configuring services");

        var serviceCollection = new ServiceCollection();
        using (sp.Measure("ConfigureServices"))
            ConfigureServices(serviceCollection, startupArgs);
        using (sp.Measure("BuildServiceProvider"))
            _services = serviceCollection.BuildServiceProvider();

        // Initialize system tray
        using (sp.Measure("InitializeSystemTray"))
            InitializeSystemTray();

        // Start UI thread watchdog (detects hangs and breaks into debugger)
        _services.GetRequiredService<UiThreadWatchdog>().Start();

        // Show Main Window
        sp.Log("Creating MainWindow");
        var mainWindow = _services.GetRequiredService<MainWindow>();

        // Ensure that closing the main window shuts down the application
        mainWindow.Closed += (s, a) => Shutdown();
        sp.Log("MainWindow.Show()");
        mainWindow.Show();

        // Handle command line arguments for this instance
        HandleCommandLineArgs(startupArgs);

        // Process any queued hook events from when the app wasn't running
        _ = ProcessQueuedHookEventsAsync();

        // Start inactivity timer for detecting stuck sessions
        var timelineService = Services.GetService<ITimelineService>();
        timelineService?.StartInactivityTimer();

        // Auto-upgrade hooks if partially installed (e.g., old 4-hook version → 9 hooks)
        timelineService?.UpgradeHooksIfNeeded();

        // Clean up old session archive entries (devcontainer sessions older than 7 days)
        var archiveService = Services.GetService<ISessionArchiveService>();
        archiveService?.CleanupOldEntries(TimeSpan.FromDays(7));

        // Bridge ActivityEvents to EventAggregator for SSE distribution
        var activityService = Services.GetService<ISessionActivityService>();
        var eventAggregator = Services.GetService<IEventAggregatorService>();
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

        // Auto-start API server if enabled
        _ = AutoStartApiServerAsync();
        _ = AutoConnectMemoryAsync();
    }

    private async Task AutoStartApiServerAsync()
    {
        try
        {
            if (_services == null) return;
            var config = _services.GetRequiredService<IConfigurationService>().Load();
            if (config.Settings.Api.Enabled)
            {
                var apiServer = _services.GetRequiredService<IApiServer>();
                apiServer.HookEventReceived += (_, hookEvent) => Dispatcher.BeginInvoke(() => ProcessHookEvent(hookEvent));
                await apiServer.StartAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to auto-start API server: {ex.Message}");
            _services?.GetService<IToastService>()?.Show($"API server failed to start: {ex.Message}", ToastType.Error);
        }
    }

    private async Task AutoConnectMemoryAsync()
    {
        try
        {
            if (_services == null) return;
            var eidet = _services.GetRequiredService<IEidetService>();

            // Pass currently-open project paths so intake runs for restored tabs
            var config = _services.GetRequiredService<IConfigurationService>().Load();
            var openPaths = config.OpenFolders.ToList();

            await eidet.TryConnectAsync(openPaths);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Eidet memory init failed: {ex.Message}");
        }
    }

    private void ConfigureServices(IServiceCollection services, CommandLineArgs args)
    {
        // Services
        services.AddSingleton(_singleInstanceService!); // Register the already active instance
        services.AddSingleton<IConfigurationService>(sp =>
            new ConfigurationService(sp.GetRequiredService<IFileSystem>(), args.UserDataDir));
        services.AddSingleton<ISystemInfoService, WindowsSystemInfoService>();
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
        services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
        services.AddSingleton<IGitWorkspaceFactory, GitWorkspaceFactory>();
        services.AddSingleton<IGitIgnoreService, GitIgnoreService>();
        services.AddSingleton<ILinkDetectionService, LinkDetectionService>();
        services.AddSingleton<IInputPromptDetectionService, InputPromptDetectionService>();
        services.AddSingleton<IRunUrlDetectionService, RunUrlDetectionService>();
        services.AddSingleton<IProjectDetectionService, ProjectDetectionService>();
        services.AddSingleton<IFileEditService, FileEditService>();
        services.AddSingleton<IFilePreviewService, FilePreviewService>();
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IClaudeCommandService, ClaudeCommandService>();
        services.AddSingleton<IClaudeTaskDetectionService, ClaudeTaskDetectionService>();
        services.AddSingleton<IClaudeSessionIndexService, ClaudeSessionIndexService>();
        services.AddSingleton<IClaudeTaskFileService, ClaudeTaskFileService>();
        services.AddSingleton<ITaskbarProgressService, TerminalHost.Windows.Services.TaskbarProgressService>();
        services.AddSingleton<IGitPrService, GitPrService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITaskAggregator, TaskAggregator>();
        services.AddSingleton<IHookInstaller, TerminalHost.Windows.Services.WindowsHookInstaller>();
        services.AddSingleton<ISessionStateStore, SessionStateStore>();
        services.AddSingleton<ILiveSessionTracker, LiveSessionTracker>();
        services.AddSingleton<ITimelineService, TimelineService>();
        services.AddSingleton<ITranscriptWatcher, TranscriptWatcher>();
        services.AddSingleton<ISessionActivityService, SessionActivityService>();
        services.AddSingleton<IInactivityClock, SystemInactivityClock>();
        services.AddSingleton<ISessionLifecycleCoordinator, SessionLifecycleCoordinator>();
        services.AddSingleton<ISessionArchiveService, SessionArchiveService>();
        services.AddSingleton<IAiAssistantService, AiAssistantService>();
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<ITestRunnerService, TestRunnerService>();
        services.AddSingleton<IMarkdownService, MarkdownService>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<WhisperModelManager>();
        services.AddSingleton<IVoiceCommandService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigurationService>().Load();
            if (config.Settings.Voice.SpeechEngine == VoiceRecognitionEngine.Whisper)
                return new WhisperVoiceCommandService(
                    sp.GetRequiredService<IConfigurationService>(),
                    sp.GetRequiredService<IDispatcherService>(),
                    sp.GetRequiredService<WhisperModelManager>());
            return new VoiceCommandService(
                sp.GetRequiredService<IConfigurationService>(),
                sp.GetRequiredService<IDispatcherService>());
        });
        services.AddSingleton<IEventAggregatorService, EventAggregatorService>();
        services.AddSingleton<IApiStateProjector, ApiStateProjector>();
        services.AddSingleton<ITerminalProfilesBuilder, TerminalProfilesBuilder>();
        services.AddSingleton<ITabRestoreCoordinator, TabRestoreCoordinator>();
        services.AddSingleton<ExplorerEventRouter>();
        services.AddSingleton<LinkClickHandler>();
        services.AddSingleton<IWebhookDeliveryService, WebhookDeliveryService>();
        services.AddSingleton<ICollabService, CollabService>();
        services.AddSingleton<McpHandler>();
        services.AddSingleton<ApiServer>();
        services.AddSingleton<IApiServer>(sp => sp.GetRequiredService<ApiServer>());
        services.AddSingleton<IEidetService, HttpEidetService>();
        services.AddSingleton<TranscriptParserService>();
        services.AddSingleton<TerminalHost.Core.Interfaces.Spark.ISessionCatalog, TerminalHost.Core.Services.Spark.TimelineSessionCatalog>();
        services.AddSingleton<TerminalHost.Core.Interfaces.Spark.IThemeStore, TerminalHost.Core.Services.Spark.ConfigThemeStore>();
        services.AddTransient<TerminalHost.Core.Interfaces.Spark.ISparkPayloadComposer, TerminalHost.Core.Services.Spark.SparkPayloadComposer>();
        services.AddTransient<TerminalHost.Core.Services.Spark.SparkCanvasOrchestrator>();
        services.AddTransient<SparkCanvasViewModel>();
        services.AddSingleton<IClipboardService, TerminalHost.Windows.Services.ClipboardService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDebugLogService, DebugLogService>();
        services.AddSingleton<StatusOverlayService>();
        services.AddSingleton<IContainerConfiguration, ContainerConfiguration>();
        services.AddSingleton<IContainerService, ContainerService>();
        services.AddSingleton<IAiExecutionService, AiExecutionService>();
        services.AddSingleton<IDiffParserService, DiffParserService>();
        services.AddSingleton<IInvisibleChangeService, InvisibleChangeService>();
        services.AddSingleton<ICommitGraphService, CommitGraphService>();
        services.AddSingleton<ITimerService, TimerService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<UiThreadWatchdog>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IViewModelFactory, ViewModelFactory>();
        services.AddSingleton<ITabFactory, TabFactory>();
        services.AddSingleton<IWorkspaceStateStore, WorkspaceStateStore>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ScratchPadViewModel>();
        services.AddSingleton<GitBranchViewModel>();
        services.AddSingleton<GitStashViewModel>();
        services.AddSingleton<ReflogViewModel>();
        services.AddSingleton<ManageWorktreesViewModel>();
        services.AddSingleton<DetectedLinksViewModel>();
        services.AddSingleton<GitFilesViewModel>();
        services.AddSingleton<CommitHistoryViewModel>();
        services.AddSingleton<GitTagsViewModel>();
        services.AddSingleton<FileHistoryViewModel>();
        services.AddSingleton<FileBlameViewModel>();
        services.AddSingleton<FileViewerViewModel>();
        services.AddSingleton<RepositorySwitcherViewModel>();
        services.AddSingleton<TestResultsViewModel>();
        services.AddSingleton<PrReviewViewModel>();
        services.AddSingleton<MarkdownPreviewViewModel>();
        services.AddSingleton<SearchAcrossFilesViewModel>();
        services.AddSingleton<BranchComparisonViewModel>();
        services.AddSingleton<UnifiedGitPanelViewModel>();
        services.AddSingleton<ClaudeTasksPanelViewModel>();
        services.AddSingleton<MemoryBrowserViewModel>();
        services.AddSingleton<DebugLogViewModel>();
        services.AddSingleton<MergeConflictViewModel>();
        services.AddSingleton<RecentFeaturesViewModel>();
        services.AddSingleton<SessionsTreePanelViewModel>();
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
            Dispatcher.BeginInvoke(() => mainWindow?.BringToFront());
        };

        systemTrayService.ExitRequested += (_, _) =>
        {
            mainWindow?.ForceClose();
        };
    }

    private void OnCommandReceived(object? sender, CommandLineArgs args)
    {
        // This runs on a background thread, so dispatch to UI thread
        Dispatcher.BeginInvoke(() =>
        {
            HandleCommandLineArgs(args);
            var mainWindow = Services.GetService<MainWindow>();
            mainWindow?.BringToFront();
        });
    }

    private void OnHookEventReceived(object? sender, HookEvent hookEvent)
    {
        // Log named-pipe hooks to the API debug log too (so all sources are visible)
        var apiServer = Services.GetService<IApiServer>() as TerminalHost.Core.Services.ApiServer;
        apiServer?.AddPipeHookDebugEntry(hookEvent);

        // This runs on a background thread, so dispatch to UI thread
        Dispatcher.BeginInvoke(() =>
        {
            ProcessHookEvent(hookEvent);
        });
    }

    private async void ProcessHookEvent(HookEvent hookEvent)
    {
        var timelineService = Services.GetService<ITimelineService>();
        if (timelineService == null) return;

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
        var activityService = Services.GetService<ISessionActivityService>();
        activityService?.ProcessHookEvent(hookEvent);

        try
        {
            switch (hookEvent.EventType)
            {
                case HookEventType.SessionStart:
                    timelineService.HandleSessionStart(hookEvent);
                    break;

                case HookEventType.FileChanged:
                    timelineService.HandleFileChanged(hookEvent);
                    break;

                case HookEventType.SessionStop:
                    await timelineService.HandleSessionStopAsync(hookEvent);
                    // Enrich activity state from transcript after session ends
                    if (activityService != null)
                    {
                        try { await activityService.EnrichFromTranscriptAsync(hookEvent.SessionId); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Transcript enrichment error: {ex.Message}"); }
                        // Archive devcontainer sessions (they can't be rediscovered from host file system)
                        ArchiveDevcontainerSession(activityService, hookEvent.SessionId);
                    }
                    break;

                case HookEventType.ToolStart:
                    timelineService.HandleToolStart(hookEvent);
                    break;

                case HookEventType.ToolEnd:
                    timelineService.HandleToolEnd(hookEvent);
                    break;

                case HookEventType.ToolError:
                    timelineService.HandleToolEnd(hookEvent);
                    break;

                case HookEventType.SubagentStart:
                case HookEventType.SubagentStop:
                case HookEventType.Notification:
                    // Route through HandleToolStart so EnsureLiveSession runs if the
                    // SessionStart hook was missed. Activity timestamps are bumped by
                    // LiveSessionTracker's ActivityEventProcessed subscription.
                    timelineService.HandleToolStart(hookEvent);
                    break;

                case HookEventType.SessionEnd:
                    // SessionEnd is a fallback for Stop — only process if not already stopped
                    await timelineService.HandleSessionStopAsync(hookEvent);
                    if (activityService != null)
                    {
                        try { await activityService.EnrichFromTranscriptAsync(hookEvent.SessionId); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Transcript enrichment error: {ex.Message}"); }
                        ArchiveDevcontainerSession(activityService, hookEvent.SessionId);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Hook event processing error: {ex.Message}");
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

    private void ArchiveDevcontainerSession(ISessionActivityService activityService, string sessionId)
    {
        var state = activityService.GetState(sessionId);
        if (state?.Source == SessionSource.DevContainer)
        {
            var archiveService = Services.GetService<ISessionArchiveService>();
            archiveService?.ArchiveSession(state);
        }
    }

    private async Task ProcessQueuedHookEventsAsync()
    {
        var queue = new HookEventQueue();
        var events = await queue.DequeueAllAsync();

        if (events.Count == 0) return;

        foreach (var hookEvent in events)
        {
            ProcessHookEvent(hookEvent);
        }
    }

    private void HandleCommandLineArgs(CommandLineArgs args)
    {
        var mainViewModel = Services.GetService<MainViewModel>();
        if (mainViewModel == null) return;

        // Handle single instance choice dialog request
        if (args.IsShowChoiceDialog)
        {
            ShowSingleInstanceChoiceDialog();
            return;
        }

        // If a working directory is specified, open a project tab
        if (!string.IsNullOrEmpty(args.WorkingDir))
        {
            mainViewModel.OpenProjectTab(args.WorkingDir, args.ForceNewTab);
        }
    }

    /// <summary>
    /// Shows a dialog when another instance tried to start without arguments.
    /// </summary>
    private void ShowSingleInstanceChoiceDialog()
    {
        var dialogService = Services.GetService<IDialogService>();
        if (dialogService == null) return;

        var result = dialogService.ShowCustomButtons(
            "TerminalHost is already running.\n\nUse 'host <path>' to open a project, or click 'Open New Instance' to start another instance.",
            "Already Running",
            "Focus Existing", "Open New Instance", "Cancel");

        switch (result)
        {
            case 0: // Focus Existing (already focused by bringing window to front)
                break;
            case 1: // Open New Instance
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    Arguments = "-multi",
                    UseShellExecute = true
                });
                break;
            // 2 or -1 = Cancel, do nothing
        }
    }

    /// <summary>
    /// Reads the single instance behavior setting from config (before DI is set up).
    /// </summary>
    private static SingleInstanceBehavior GetSingleInstanceBehavior()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TerminalHost", "config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<AppConfiguration>(json);
                return config?.Settings?.SingleInstanceBehavior ?? SingleInstanceBehavior.ShowDialog;
            }
        }
        catch
        {
            // Ignore config read errors
        }
        return SingleInstanceBehavior.ShowDialog;
    }

    /// <summary>
    /// Determines if this is a first-run scenario where setup should be shown automatically.
    /// </summary>
    private static bool IsFirstRun(CommandLineArgs args)
    {
        // Skip if --no-setup flag passed
        if (args.SkipSetup) return false;

        // Skip if testing/debug flags (likely development/testing)
        if (args.DisableSingleInstance) return false;

        // Skip if explicitly requesting setup (will show anyway)
        if (args.IsSetupMode) return false;

        // Skip if opened with a path (user knows how to use the app)
        if (args.HasValidRequest()) return false;

        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TerminalHost", "config.json");

            // No config file = first run
            if (!File.Exists(configPath)) return true;

            // Check if config is default/untouched
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<AppConfiguration>(json);
            return config?.IsDefault() ?? true;
        }
        catch
        {
            // Error reading config - assume first run
            return true;
        }
    }

    /// <summary>
    /// Marks first-run setup as completed and saves to config.
    /// </summary>
    private static void MarkFirstRunCompleted()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TerminalHost", "config.json");

            AppConfiguration config;
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                config = JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
            }
            else
            {
                config = new AppConfiguration();
            }

            config.Settings.FirstRunCompleted = true;
            config.Settings.FirstRunDate = DateTime.UtcNow;

            // Ensure directory exists
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, options));
        }
        catch
        {
            // Ignore save errors - not critical
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        if (_services != null)
        {
            _services.GetService<UiThreadWatchdog>()?.Dispose();
            _services.GetService<IApiServer>()?.Dispose();
            _services.GetService<IWebhookDeliveryService>()?.Dispose();
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

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Keep the app alive for unobserved background task exceptions, but persist diagnostics.
        var reportPath = WriteCrashReport("Unobserved Task Exception", e.Exception);
        if (!string.IsNullOrEmpty(reportPath))
        {
            Debug.WriteLine($"Unobserved task exception report written: {reportPath}");
        }

        e.SetObserved();
    }

    private void ShowUnhandledException(Exception ex, string type)
    {
        var reportPath = WriteCrashReport(type, ex);
        var errorMessage = $"{type}:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
        if (!string.IsNullOrEmpty(reportPath))
        {
            errorMessage += $"\n\nCrash report saved to:\n{reportPath}";
        }

        System.Diagnostics.Debug.WriteLine(errorMessage); // Log to debug output

        MessageBox.Show(
            errorMessage,
            "Application Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown();
    }

    private static string? WriteCrashReport(string type, Exception ex)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appData, "TerminalHost", "logs");
            Directory.CreateDirectory(logDirectory);

            var timestamp = DateTime.UtcNow;
            var fileName = $"crash-{timestamp:yyyyMMdd-HHmmss-fff}.log";
            var reportPath = Path.Combine(logDirectory, fileName);

            var report = new StringBuilder();
            report.AppendLine("TerminalHost Crash Report");
            report.AppendLine("=========================");
            report.AppendLine($"TimestampUtc: {timestamp:O}");
            report.AppendLine($"Type: {type}");
            report.AppendLine($"ProcessPath: {Environment.ProcessPath}");
            report.AppendLine($"BaseDirectory: {AppContext.BaseDirectory}");
            report.AppendLine($"OSVersion: {Environment.OSVersion}");
            report.AppendLine($"RuntimeVersion: {Environment.Version}");
            report.AppendLine($"Is64BitProcess: {Environment.Is64BitProcess}");
            report.AppendLine();
            report.AppendLine("Exception:");
            report.AppendLine(ex.ToString());

            File.WriteAllText(reportPath, report.ToString());
            return reportPath;
        }
        catch (Exception writeEx)
        {
            Debug.WriteLine($"Failed to write crash report: {writeEx.Message}");
            return null;
        }
    }

    #region Claude Code Hook Handling

    /// <summary>
    /// Handles a Claude Code hook callback.
    /// Reads JSON from stdin, parses it, forwards to main instance or queues for later.
    /// Exits immediately after processing.
    /// IMPORTANT: Must exit quickly (< 5 seconds) to not block Claude Code.
    /// </summary>
    private void HandleHookAndExit(string hookType)
    {
        // Use Environment.Exit instead of Shutdown - we're not running a WPF app
        try
        {
            // Read JSON from stdin with timeout to prevent hanging
            string? stdinJson = ReadStdinWithTimeout(TimeSpan.FromSeconds(2));

            if (string.IsNullOrWhiteSpace(stdinJson))
            {
                // No stdin data - nothing to process
                Environment.Exit(0);
                return;
            }

            // Parse the raw hook data
            var hookData = HookEventData.Parse(stdinJson);
            if (hookData == null)
            {
                // Failed to parse - exit silently
                Environment.Exit(1);
                return;
            }

            // Convert to our HookEvent based on hook type
            HookEvent? hookEvent = hookType switch
            {
                "session-start" => HookEvent.CreateSessionStart(hookData),
                "session-stop" => HookEvent.CreateSessionStop(hookData),
                "session-end" => HookEvent.CreateSessionEnd(hookData),
                "tool-start" => HookEvent.CreateToolStart(hookData),
                "tool-end" => HookEvent.CreateToolEnd(hookData),
                "tool-error" => HookEvent.CreateToolError(hookData),
                "subagent-start" => HookEvent.CreateSubagentStart(hookData),
                "subagent-stop" => HookEvent.CreateSubagentStop(hookData),
                "notification" => HookEvent.CreateNotification(hookData),
                "file-changed" => hookData.IsFileModificationTool() ? HookEvent.CreateFileChanged(hookData) : null, // backward compat
                _ => null
            };

            if (hookEvent == null)
            {
                // Unknown hook type or not applicable
                Environment.Exit(0);
                return;
            }

            // Try to send to running main instance (with short timeout)
            if (SingleInstanceService.IsMainInstanceRunningStatic())
            {
                if (SingleInstanceService.SendHookEventToRunningInstance(hookEvent))
                {
                    // Successfully sent to main instance
                    Environment.Exit(0);
                    return;
                }
            }

            // Main instance not running or send failed - queue for later
            var queue = new HookEventQueue();
            // Use synchronous file write to avoid async issues
            queue.EnqueueSync(hookEvent);

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            // Log error but don't crash - hooks should fail silently
            Debug.WriteLine($"Hook handling error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Reads stdin with a timeout to prevent hanging.
    /// </summary>
    private static string? ReadStdinWithTimeout(TimeSpan timeout)
    {
        if (!Console.IsInputRedirected)
            return null;

        var result = new System.Text.StringBuilder();
        var readTask = Task.Run(() =>
        {
            try
            {
                // Read character by character with a buffer
                var buffer = new char[4096];
                int charsRead;
                while ((charsRead = Console.In.Read(buffer, 0, buffer.Length)) > 0)
                {
                    result.Append(buffer, 0, charsRead);
                }
            }
            catch
            {
                // Ignore read errors
            }
        });

        // Wait with timeout
        if (readTask.Wait(timeout))
        {
            return result.ToString();
        }

        // Timeout - return what we have so far
        return result.Length > 0 ? result.ToString() : null;
    }

    #endregion

    #region Popup Focus Fix
    // Workaround for WPF popup focus synchronization issues when main window isn't active.
    // See: https://github.com/dotnet/wpf/issues/1150

    private void OnPopupPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_ignoreFocusChange)
            return;

        if (sender is not Popup popup)
            return;

        // Check if main window is active - if not, we need to handle focus specially
        var mainWindow = MainWindow;
        if (mainWindow == null || mainWindow.IsActive)
            return;

        // Main window is not active - force activation and then focus the clicked element
        _ignoreFocusChange = true;
        try
        {
            // Get the main window's handle and activate it
            var mainWindowHandle = new WindowInteropHelper(mainWindow).Handle;
            if (mainWindowHandle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(mainWindowHandle);
            }

            // Find and focus the clicked element after activation
            if (e.OriginalSource is DependencyObject source)
            {
                popup.Dispatcher.InvokeAsync(() =>
                {
                    // Find the focusable element (TextBox, etc.) that was clicked
                    IInputElement? element = source as IInputElement;
                    if (element == null || (element is FrameworkElement fe && !fe.Focusable))
                    {
                        // Walk up to find a focusable parent
                        var current = source as DependencyObject;
                        while (current != null)
                        {
                            if (current is FrameworkElement fwe && fwe.Focusable)
                            {
                                element = fwe;
                                break;
                            }
                            current = VisualTreeHelper.GetParent(current);
                        }
                    }

                    if (element != null)
                    {
                        Keyboard.Focus(element);
                    }
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }
        finally
        {
            _ignoreFocusChange = false;
        }
    }

    private void OnPopupGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        var popup = (UIElement)sender;
        var windowHandle = (PresentationSource.FromVisual(popup) as HwndSource)?.Handle;
        var focusedNativeWindow = NativeMethods.GetFocus();

        if (_ignoreFocusChange)
            return;

        if (focusedNativeWindow == windowHandle)
            return;

        popup.Dispatcher.InvokeAsync(
            () => VerifyPopupFocus(popup, e),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void VerifyPopupFocus(UIElement popup, RoutedEventArgs e)
    {
        var focusedElement = Keyboard.FocusedElement;
        var focusedNativeWindow = NativeMethods.GetFocus();

        if (focusedElement is not Visual focusedVisual
            || PresentationSource.FromVisual(focusedVisual) is not HwndSource nativeWindow)
        {
            // The element with keyboard focus is not a WPF element
            return;
        }

        var focusScope = FocusManager.GetFocusScope((DependencyObject)e.Source);
        var focusScopeWindowHandle = (PresentationSource.FromVisual((Visual)focusScope) as HwndSource)?.Handle;

        if (focusedNativeWindow == focusScopeWindowHandle)
        {
            // Focused native window is the focus scope window, no change needed
            return;
        }

        var nativeWindowHandle = nativeWindow.Handle;

        if (nativeWindowHandle == focusedNativeWindow)
        {
            // The focus is within WPF. Nothing needs to be done.
            return;
        }

        var activeWindow = Windows.Cast<Window>().FirstOrDefault(x => x.IsActive);

        var windowToFocus = nativeWindowHandle;
        var focusScopeToFocus = focusScope;

        if (activeWindow != null)
        {
            var activeWindowHandle = new WindowInteropHelper(activeWindow).Handle;
            if (activeWindowHandle == focusedNativeWindow)
            {
                // The focus is within WPF. Nothing needs to be done.
                return;
            }

            windowToFocus = activeWindowHandle;
            focusScopeToFocus = activeWindow;
        }

        ForceFocusToWpfElement(windowToFocus, focusScopeToFocus, focusedElement);
    }

    private void ForceFocusToWpfElement(IntPtr windowHandle, DependencyObject focusScope, IInputElement focusedElement)
    {
        // The focused HWND is not a WPF window, but WPF thinks it has Keyboard Focus.
        // This means that the focus is out of sync. (The HWND that has focus will receive keyboard input,
        // but WPF will render the UI as if the focused element has focus.)
        //
        // To fix this state, we need to focus the WPF window so that it receives the input.
        // We then have to restore the focus to the previously focused element in WPF.
        _ignoreFocusChange = true;
        try
        {
            NativeMethods.SetFocus(windowHandle);
            var elementFocusedByMovingFocusToWpfWindow = FocusManager.GetFocusedElement(focusScope);

            if (elementFocusedByMovingFocusToWpfWindow != null
                && elementFocusedByMovingFocusToWpfWindow != focusedElement)
            {
                // Focus the element from WPF as well, since WPF seems to not fully update its internal state
                // when just restoring focus when the main window receives focus by a native call.
                elementFocusedByMovingFocusToWpfWindow.Focus();
            }

            focusedElement.Focus();
        }
        finally
        {
            _ignoreFocusChange = false;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        internal static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    #endregion
}
