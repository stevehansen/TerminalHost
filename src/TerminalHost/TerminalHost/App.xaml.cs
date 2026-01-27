using System.Diagnostics;
using System.IO;
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

        // Process any queued hook events from when the app wasn't running
        _ = ProcessQueuedHookEventsAsync();
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
        services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
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
        services.AddSingleton<ITaskbarProgressService, TerminalHost.Windows.Services.TaskbarProgressService>();
        services.AddSingleton<IGitPrService, GitPrService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITimelineService, TimelineService>();
        services.AddSingleton<IAiAssistantService, AiAssistantService>();
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<ITestRunnerService, TestRunnerService>();
        services.AddSingleton<IMarkdownService, MarkdownService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDiffParserService, DiffParserService>();
        services.AddSingleton<ITimerService, TimerService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IViewModelFactory, ViewModelFactory>();

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

    private void OnHookEventReceived(object? sender, HookEvent hookEvent)
    {
        // This runs on a background thread, so dispatch to UI thread
        Dispatcher.Invoke(() =>
        {
            ProcessHookEvent(hookEvent);
        });
    }

    private void ProcessHookEvent(HookEvent hookEvent)
    {
        var timelineService = Services.GetService<ITimelineService>();
        if (timelineService == null) return;

        switch (hookEvent.EventType)
        {
            case HookEventType.SessionStart:
                timelineService.HandleSessionStart(hookEvent);
                break;

            case HookEventType.FileChanged:
                timelineService.HandleFileChanged(hookEvent);
                break;

            case HookEventType.SessionStop:
                // Run async but don't block
                _ = timelineService.HandleSessionStopAsync(hookEvent);
                break;
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
                "file-changed" => hookData.IsFileModificationTool() ? HookEvent.CreateFileChanged(hookData) : null,
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
