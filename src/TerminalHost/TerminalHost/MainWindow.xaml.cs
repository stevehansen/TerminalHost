using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Windows.Interfaces;
using TerminalHost.Windows.Platform;
using TerminalHost.Windows.Services;

namespace TerminalHost;

/// <summary>
/// Core window logic, constructor, and keyboard shortcuts.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IConfigurationService _configService;
    private readonly IProfileRegistry _profileRegistry;
    private readonly ISystemTrayService? _systemTrayService;
    private readonly ScratchPadViewModel _scratchPadViewModel;
    private readonly GitBranchViewModel _gitBranchViewModel;
    private readonly GitStashViewModel _gitStashViewModel;
    private readonly ReflogViewModel _reflogViewModel;
    private readonly ManageWorktreesViewModel _manageWorktreesViewModel;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly GitFilesViewModel _gitFilesViewModel;
    private readonly CommitHistoryViewModel _commitHistoryViewModel;
    private readonly GitTagsViewModel _gitTagsViewModel;
    private readonly FileHistoryViewModel _fileHistoryViewModel;
    private readonly FileBlameViewModel _fileBlameViewModel;
    private readonly FileViewerViewModel _fileViewerViewModel;
    private readonly RepositorySwitcherViewModel _repositorySwitcherViewModel;
    private readonly TestResultsViewModel _testResultsViewModel;
    private readonly PrReviewViewModel _prReviewViewModel;
    private readonly MarkdownPreviewViewModel _markdownPreviewViewModel;
    private readonly SearchAcrossFilesViewModel _searchAcrossFilesViewModel;
    private readonly BranchComparisonViewModel _branchComparisonViewModel;
    private readonly UnifiedGitPanelViewModel _unifiedGitPanelViewModel;
    private readonly ClaudeTasksPanelViewModel _claudeTasksPanelViewModel;
    private readonly MemoryBrowserViewModel _memoryBrowserViewModel;
    private readonly DebugLogViewModel _debugLogViewModel;
    private readonly MergeConflictViewModel _mergeConflictViewModel;
    private readonly RecentFeaturesViewModel _recentFeaturesViewModel;
    private readonly SessionsTreePanelViewModel _sessionsTreePanelViewModel;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IToastService _toastService;
    private readonly ITaskbarProgressService? _taskbarProgressService;
    private readonly ISoundService? _soundService;
    private readonly StatusOverlayService _statusOverlayService;
    private TerminalPairTabViewModel? _overlayFeaturedTab;
    private string _cachedAiName = "Claude Code";
    private bool _isExiting;
    private bool _isWindowActivated = true;
    private Services.PanelWindowManager? _panelWindowManager;
    private readonly IPanelRouter? _panelRouter;
    private readonly Services.Panels.WpfPopupSurface? _popupSurface;
    private Views.ToastWindow? _toastWindow;
    private TerminalPairTabViewModel? _previousSelectedTerminalTab;

    public MainWindow(MainViewModel viewModel, IConfigurationService configService, IProfileRegistry profileRegistry, ScratchPadViewModel scratchPadViewModel, GitBranchViewModel gitBranchViewModel, GitStashViewModel gitStashViewModel, ReflogViewModel reflogViewModel, ManageWorktreesViewModel manageWorktreesViewModel, DetectedLinksViewModel detectedLinksViewModel, GitFilesViewModel gitFilesViewModel, CommitHistoryViewModel commitHistoryViewModel, GitTagsViewModel gitTagsViewModel, FileHistoryViewModel fileHistoryViewModel, FileBlameViewModel fileBlameViewModel, FileViewerViewModel fileViewerViewModel, RepositorySwitcherViewModel repositorySwitcherViewModel, TestResultsViewModel testResultsViewModel, PrReviewViewModel prReviewViewModel, MarkdownPreviewViewModel markdownPreviewViewModel, SearchAcrossFilesViewModel searchAcrossFilesViewModel, BranchComparisonViewModel branchComparisonViewModel, UnifiedGitPanelViewModel unifiedGitPanelViewModel, ClaudeTasksPanelViewModel claudeTasksPanelViewModel, MemoryBrowserViewModel memoryBrowserViewModel, DebugLogViewModel debugLogViewModel, MergeConflictViewModel mergeConflictViewModel, RecentFeaturesViewModel recentFeaturesViewModel, SessionsTreePanelViewModel sessionsTreePanelViewModel, IFileSystem fileSystem, IToastService toastService, StatusOverlayService statusOverlayService, ISystemTrayService? systemTrayService = null, IDialogService dialogService = null!, ITaskbarProgressService? taskbarProgressService = null, ISoundService? soundService = null, IPanelRouter? panelRouter = null, Services.Panels.WpfPopupSurface? popupSurface = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        _profileRegistry = profileRegistry;
        _systemTrayService = systemTrayService;
        _scratchPadViewModel = scratchPadViewModel;
        _gitBranchViewModel = gitBranchViewModel;
        _gitStashViewModel = gitStashViewModel;
        _reflogViewModel = reflogViewModel;
        _manageWorktreesViewModel = manageWorktreesViewModel;
        _detectedLinksViewModel = detectedLinksViewModel;
        _gitFilesViewModel = gitFilesViewModel;
        _commitHistoryViewModel = commitHistoryViewModel;
        _gitTagsViewModel = gitTagsViewModel;
        _fileHistoryViewModel = fileHistoryViewModel;
        _fileBlameViewModel = fileBlameViewModel;
        _fileViewerViewModel = fileViewerViewModel;
        _repositorySwitcherViewModel = repositorySwitcherViewModel;
        _testResultsViewModel = testResultsViewModel;
        _prReviewViewModel = prReviewViewModel;
        _markdownPreviewViewModel = markdownPreviewViewModel;
        _searchAcrossFilesViewModel = searchAcrossFilesViewModel;
        _branchComparisonViewModel = branchComparisonViewModel;
        _unifiedGitPanelViewModel = unifiedGitPanelViewModel;
        _claudeTasksPanelViewModel = claudeTasksPanelViewModel;
        _memoryBrowserViewModel = memoryBrowserViewModel;
        _debugLogViewModel = debugLogViewModel;
        _mergeConflictViewModel = mergeConflictViewModel;
        _recentFeaturesViewModel = recentFeaturesViewModel;
        _sessionsTreePanelViewModel = sessionsTreePanelViewModel;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _toastService = toastService;
        _taskbarProgressService = taskbarProgressService;
        _soundService = soundService;
        _statusOverlayService = statusOverlayService;
        _panelRouter = panelRouter;
        _popupSurface = popupSurface;
        DataContext = viewModel;
        // GitBranch and GitStash popups removed - now accessed via Git GUI center panel tabs
        ReflogViewControl.DataContext = reflogViewModel;
        ManageWorktreesViewControl.DataContext = manageWorktreesViewModel;
        // DetectedLinks popup removed - now uses sidebar panel system
        // FileViewer popup removed - now rendered as center panel
        RepositorySwitcherViewControl.DataContext = repositorySwitcherViewModel;
        // TestResults popup removed - now rendered as center panel
        // PrReview popup removed - now rendered as center panel
        // BranchComparisonViewModel is now rendered as center panel (no popup view)
        // UnifiedGitPanelViewModel is now rendered as center panel (no popup view)
        // ClaudeTasksPanel popup removed - now uses sidebar panel system

        // Git Files, Commit History, and Scratch Pad use panel system only (no popup views in XAML, like Markdown Preview)

        // Subscribe to panel show events (single handler for all panels)
        _markdownPreviewViewModel.ShowRequested += OnPanelShowRequested;
        _gitFilesViewModel.ShowRequested += OnPanelShowRequested;
        _commitHistoryViewModel.ShowRequested += OnPanelShowRequested;
        _fileHistoryViewModel.ShowRequested += OnPanelShowRequested;
        _fileBlameViewModel.ShowRequested += OnPanelShowRequested;
        _scratchPadViewModel.ShowRequested += OnPanelShowRequested;
        _searchAcrossFilesViewModel.ShowRequested += OnPanelShowRequested;
        _branchComparisonViewModel.ShowRequested += OnPanelShowRequested;
        _fileViewerViewModel.ShowRequested += OnPanelShowRequested;
        _prReviewViewModel.ShowRequested += OnPanelShowRequested;
        _testResultsViewModel.ShowRequested += OnPanelShowRequested;
        _detectedLinksViewModel.ShowRequested += OnPanelShowRequested;
        _claudeTasksPanelViewModel.ShowRequested += OnPanelShowRequested;
        _memoryBrowserViewModel.ShowRequested += OnPanelShowRequested;
        _debugLogViewModel.ShowRequested += OnPanelShowRequested;
        _mergeConflictViewModel.ShowRequested += OnPanelShowRequested;
        _sessionsTreePanelViewModel.ShowRequested += OnPanelShowRequested;
        _sessionsTreePanelViewModel.OpenProjectRequested += (_, path) => _viewModel.OpenProjectTab(path);

        // Subscribe to merge conflict events from git files panel
        _gitFilesViewModel.MergeConflictRequested += OnMergeConflictRequested;

        // Subscribe to branch comparison events
        _gitBranchViewModel.CompareBranchesRequested += OnCompareBranchesRequested;

        // Subscribe to ManageWorktrees events and Git panel events
        if (_viewModel.WorkspaceSidebar != null)
        {
            _viewModel.WorkspaceSidebar.ManageWorktreesRequested += OnManageWorktreesRequested;
            _viewModel.WorkspaceSidebar.GitPanelRequested += OnSidebarGitPanelRequested;
            _viewModel.WorkspaceSidebar.StashPanelRequested += OnSidebarStashPanelRequested;
        }
        _manageWorktreesViewModel.OpenWorktreeRequested += OnManageWorktreesOpenWorktree;
        _manageWorktreesViewModel.CreateWorktreeRequested += OnManageWorktreesCreateWorktree;

        RestoreWindowState();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += OnSourceInitialized;

        // Subscribe to view model property changes to sync column widths
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Subscribe to config reload events to update tray setting
        _viewModel.ConfigReloaded += OnConfigReloaded;

        // Subscribe to file preview/edit events
        _viewModel.FilePreviewRequested += OnFilePreviewRequested;
        _detectedLinksViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _gitFilesViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _gitFilesViewModel.FileEditRequested += OnFileEditRequested;
        _fileViewerViewModel.DetachRequested += OnFileViewerDetachRequested;
        _fileViewerViewModel.FileHistoryRequested += OnFileHistoryRequested;
        _fileViewerViewModel.FileBlameRequested += OnFileBlameRequested;
        _viewModel.FileHistoryRequested += OnFileHistoryRequestedFromExplorer;
        _viewModel.FileBlameRequested += OnFileBlameRequestedFromExplorer;

        // Subscribe to help events
        _viewModel.GitChangesRequested += OnGitChangesRequested;
        _viewModel.ScratchPadRequested += OnScratchPadRequested;
        _viewModel.SetupRequested += OnSetupRequested;
        _viewModel.PrReviewRequested += OnPrReviewRequested;
        _viewModel.DashboardPrReviewRequested += OnDashboardPrReviewRequested;
        _viewModel.MarkdownPreviewRequested += OnMarkdownPreviewRequested;
        _viewModel.UnifiedGitPanelRequested += OnUnifiedGitPanelRequested;
        _viewModel.CenterPanelRestoreRequested += OnCenterPanelRestoreRequested;
        _viewModel.RightPanelRestoreRequested += OnRightPanelRestoreRequested;
        _viewModel.ReflogRequested += OnReflogRequested;
        _viewModel.RepositorySwitcherRequested += OnRepositorySwitcherRequested;
        _viewModel.SearchRequested += OnSearchRequested;
        _viewModel.ClaudeTasksRequested += OnClaudeTasksRequested;
        _viewModel.SessionsTreeRequested += OnSessionsTreeRequested;
        _viewModel.SessionsPanelVisibilityChanged += OnSessionsPanelVisibilityChanged;
        _viewModel.TestRunnerRequested += OnTestRunnerRequested;
        _viewModel.WhatsNewRequested += OnWhatsNewRequested;
        _viewModel.MemoryBrowserRequested += OnMemoryBrowserRequested;
        _viewModel.DebugLogRequested += OnDebugLogRequested;
        _viewModel.AiPanelCommandRequested += OnAiPanelCommandRequested;
        _recentFeaturesViewModel.ShowRequested += OnPanelShowRequested;

        // Set up the empty state Recent Features view
        EmptyStateRecentFeatures.DataContext = _recentFeaturesViewModel;
        _recentFeaturesViewModel.OnOpened();

        // Subscribe to run terminal events
        _viewModel.RunTerminalRequested += OnRunTerminalRequested;
    }

    #region Window State and Lifecycle

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        DarkModeHelper.EnableDarkMode(this);

        // Prevent white flash before WPF's first render by intercepting WM_ERASEBKGND
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(DarkModeHelper.CreateDarkBackgroundHook());
    }

    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        var config = _configService.Load();

        if (_systemTrayService != null)
        {
            _systemTrayService.IsEnabled = config.Settings.ShowInSystemTray;
        }

        // Refresh cached values so hot paths don't need to hit disk
        _cachedAiName = config.Settings.CustomCommandName;
        _statusOverlayService.RefreshCachedSettings();

        // Refresh sound service cached settings
        if (_soundService is SoundService soundService)
            soundService.RefreshCachedSettings(config.Settings.Sounds);

        // Eidet memory: connect/disconnect based on enabled setting
        var eidet = App.Current.Services.GetService<IEidetService>();
        if (eidet != null)
            _ = eidet.OnSettingsChangedAsync();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Minimize to tray when enabled
        if (WindowState == WindowState.Minimized && _systemTrayService?.IsEnabled == true)
        {
            Hide();
        }
    }

    public void ForceClose()
    {
        _isExiting = true;
        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Update Claude Tasks panel workspace when selected tab changes
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            if (_claudeTasksPanelViewModel.IsOpen)
            {
                // Always update workspace path so it's correct when toggling to "Current Workspace"
                var workspacePath = (_viewModel.SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory;
                _claudeTasksPanelViewModel.SetWorkspace(workspacePath);

                // Only refresh if filtering by current workspace (global shows all anyway)
                if (!_claudeTasksPanelViewModel.ShowGlobalTasks)
                {
                    _claudeTasksPanelViewModel.OnOpened();
                }
            }

            // Rebind center panel data when switching to a tab that has one.
            // Singleton panel VMs only hold data for one tab at a time, so we
            // must reload when the user switches to a different tab.
            if (_viewModel.SelectedTab is TerminalPairTabViewModel newTab &&
                newTab.ActiveCenterPanel != null)
            {
                if (newTab.ActiveCenterPanel == _unifiedGitPanelViewModel)
                    _ = _unifiedGitPanelViewModel.OpenOnTabAsync(newTab, _unifiedGitPanelViewModel.ActiveTab);
                else if (newTab.ActiveCenterPanel == _branchComparisonViewModel)
                    _ = _branchComparisonViewModel.OpenAsync(newTab);
                else if (newTab.ActiveCenterPanel == _searchAcrossFilesViewModel)
                    _ = _searchAcrossFilesViewModel.OpenAsync(newTab);
                else if (newTab.ActiveCenterPanel == _prReviewViewModel)
                    _ = _prReviewViewModel.OpenAsync(newTab.WorkingDirectory);
            }

            // Refresh git sidebar when tab changes
            if (_viewModel.WorkspaceSidebar != null)
            {
                var workDir = (_viewModel.SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory;
                _ = _viewModel.WorkspaceSidebar.RefreshGitSidebarAsync(workDir);
            }
        }
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new split ratio
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid to get actual column widths
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 3)
            {
                var customWidth = grid.ColumnDefinitions[0].ActualWidth;
                var shellWidth = grid.ColumnDefinitions[2].ActualWidth;
                terminalTab.UpdateSplitRatioFromColumnWidths(customWidth, shellWidth);
            }
        }
    }

    private void RunSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new run split ratio
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid to get actual column widths
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 5)
            {
                // Main terminals are columns 0-2, run terminal is column 4
                var mainWidth = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
                var runWidth = grid.ColumnDefinitions[4].ActualWidth;
                terminalTab.UpdateRunSplitRatioFromColumnWidths(mainWidth, runWidth);
            }
        }
    }

    private void OpenDetectedRunUrl_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && !string.IsNullOrEmpty(terminalTab.DetectedRunUrl))
        {
            _viewModel.RunUrlDetectionService.OpenInBrowser(terminalTab.DetectedRunUrl);
        }
    }

    private void OnRunTerminalRequested(object? sender, RunTerminalRequestedEventArgs e)
    {
        if (e.IsStop)
        {
            StopRunTerminal(e.Tab);
        }
        else
        {
            StartRunTerminal(e.Tab, e.Configuration);
        }
    }

    private void StartRunTerminal(TerminalPairTabViewModel tab, RunConfiguration config)
    {
        try
        {
            // Use cmd.exe directly with the run command inline for faster startup
            // This avoids PowerShell profile loading delays
            var runCommand = config.Command;
            var workingDir = tab.Pair.WorkingDirectory;

            // Create a profile that runs the command directly via cmd.exe
            // The command is embedded in the startup so it runs immediately
            var runProfile = new Profile
            {
                Id = "run",
                Name = "Run",
                Command = $"cmd.exe /K cd /d \"{workingDir}\" && {runCommand}",
                WorkingDir = "",  // Already handled in command
                Icon = "▶"
            };

            // Create the run terminal if it doesn't exist
            var runSession = tab.Pair.CreateRunTerminal(runProfile);

            // Create the terminal control
            var runControl = _viewModel.TerminalFactory.CreateTerminalControl(runSession);
            tab.SetRunTerminalControl(runControl);

            // Track the session
            _viewModel.SessionManager.TrackSession(runSession);

            // Subscribe to link click events
            runSession.LinkClicked += (s, text) => HandleRunLinkClick(text, tab.Pair.WorkingDirectory);

            // Mark as started (command runs automatically via startup)
            tab.OnRunStarted();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to start run terminal:\n{ex.Message}", "Run Error"); // Use injected IDialogService
            tab.OnRunStopped();
        }
    }

    private void StopRunTerminal(TerminalPairTabViewModel tab)
    {
        try
        {
            if (tab.Pair.RunTerminal == null)
            {
                tab.OnRunStopped();
                return;
            }

            // Send Ctrl+C to stop the process
            tab.Pair.RunTerminal.SendText("\x03", appendNewline: false);

            // Mark as stopped after a short delay to allow the process to terminate
            Dispatcher.BeginInvoke(new Action(() =>
            {
                tab.OnRunStopped();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to stop run terminal:\n{ex.Message}", "Run Error"); // Use injected IDialogService
            tab.OnRunStopped();
        }
    }

    private void HandleRunLinkClick(string recentOutput, string workingDirectory)
    {
        // For now, just use the same link handling as other terminals
        if (string.IsNullOrEmpty(recentOutput)) return;

        var link = _viewModel.LinkDetectionService.DetectLink(recentOutput, workingDirectory);
        if (link != null)
        {
            _viewModel.LinkDetectionService.OpenLink(link);
        }
    }

    private void RestoreWindowState()
    {
        var config = _configService.Load();
        var state = config.WindowState;

        // Validate position is on screen
        var left = state.Left;
        var top = state.Top;
        var width = Math.Max(400, state.Width);
        var height = Math.Max(300, state.Height);

        // Ensure window is visible on at least one monitor
        // Allow small negative values (-16px) since Windows 10/11 positions snapped/docked
        // windows slightly off-screen to hide window borders
        const int borderMargin = 16;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        if (left < virtualLeft - borderMargin || left > virtualLeft + virtualWidth - 100)
            left = 100;
        if (top < virtualTop - borderMargin || top > virtualTop + virtualHeight - 100)
            top = 100;

        Left = left;
        Top = top;
        Width = width;
        Height = height;

        if (state.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        var config = _configService.Load();

        // Save window state (use restore bounds if maximized)
        if (WindowState == WindowState.Maximized)
        {
            config.WindowState.Left = RestoreBounds.Left;
            config.WindowState.Top = RestoreBounds.Top;
            config.WindowState.Width = RestoreBounds.Width;
            config.WindowState.Height = RestoreBounds.Height;
            config.WindowState.IsMaximized = true;
        }
        else
        {
            config.WindowState.Left = Left;
            config.WindowState.Top = Top;
            config.WindowState.Width = Width;
            config.WindowState.Height = Height;
            config.WindowState.IsMaximized = false;
        }

        _configService.Save(config);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var sp = StartupProfiler.Instance;
        sp.Log("OnLoaded — enter");

        using (sp.Measure("ViewModel.Initialize"))
            _viewModel.Initialize();

        // Initialize panel window manager
        _panelWindowManager = new Services.PanelWindowManager(this);

        // Attach the routed popup host to the WPF popup surface. After this, the surface
        // can mount any popup-zone panel routed through IPanelRouter into RoutedPopupHost.
        _popupSurface?.AttachHost(RoutedPopupHost, RoutedPopupContent);

        // Create and show toast window (must be after main window is shown for Owner to work)
        _toastWindow = new Views.ToastWindow();
        _toastWindow.Initialize(this, _toastService);
        _toastWindow.Show();

        // Initialize taskbar progress service with window handle
        if (_taskbarProgressService is TerminalHost.Windows.Services.TaskbarProgressService taskbarService)
        {
            var windowInteropHelper = new System.Windows.Interop.WindowInteropHelper(this);
            taskbarService.Initialize(windowInteropHelper.Handle);
        }

        // Cache config values used in high-frequency paths (avoids disk I/O on every state change)
        _cachedAiName = _configService.Load().Settings.CustomCommandName;

        // Initialize status overlay service
        _statusOverlayService.Initialize(this);
        _statusOverlayService.FocusRequested += OnStatusOverlayFocusRequested;

        sp.Log("OnLoaded — done");
        sp.Flush();

        // Subscribe to window activation events to clear glow when focused
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;

        // Subscribe to tab selection changes to monitor terminal activity
        _viewModel.PropertyChanged += OnViewModelPropertyChangedForTaskbar;

        // Subscribe to all existing terminal tabs for overlay aggregation
        foreach (var tab in _viewModel.Tabs.OfType<TerminalPairTabViewModel>())
            tab.PropertyChanged += OnAnyTerminalTabPropertyChanged;
        _viewModel.Tabs.CollectionChanged += OnTabsCollectionChangedForOverlay;

        // Apply persisted global Sessions panel flag to all restored tabs (#77).
        if (_viewModel.ShowSessionsPanel)
        {
            OnSessionsPanelVisibilityChanged(this, true);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // If tray is enabled and not explicitly exiting, minimize to tray instead of closing
        if (_systemTrayService?.IsEnabled == true && !_isExiting)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }

        SaveWindowState();
        _statusOverlayService.Shutdown();
        _viewModel.Shutdown();

        Application.Current.Shutdown();
    }

    public void BringToFront()
    {
        // Show window if hidden (minimized to tray)
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    #endregion

    #region Keyboard Shortcuts

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Escape: Close voice bar first, then center panel, then popups
        if (e.Key == Key.Escape)
        {
            // Let routed popup views handle their own Escape key (the popup surface intercepts it).
            if (_panelRouter?.IsOpen("commandPalette") == true ||
                _panelRouter?.IsOpen("tabSwitcher") == true ||
                _panelRouter?.IsOpen("tabDropdown") == true)
                return;

            // First priority: dismiss voice bar if visible
            if (_viewModel.VoiceBar.IsVisible)
            {
                _viewModel.VoiceBar.Cancel();
                e.Handled = true;
                return;
            }

            // Second priority: close active center panel (return to terminals)
            if (_viewModel.SelectedTab is TerminalPairTabViewModel escTerminalTab && escTerminalTab.ActiveCenterPanel != null)
            {
                escTerminalTab.CloseCenterPanel();
                e.Handled = true;
                return;
            }

            // GitBranch and GitStash popups removed - accessed via Git GUI center panel tabs
            if (_reflogViewModel.IsOpen)
            {
                _reflogViewModel.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (_manageWorktreesViewModel.IsOpen)
            {
                _manageWorktreesViewModel.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (_scratchPadViewModel.IsOpen)
            {
                _scratchPadViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_detectedLinksViewModel.IsOpen)
            {
                _detectedLinksViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_gitFilesViewModel.IsOpen)
            {
                _gitFilesViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_fileViewerViewModel.IsOpen)
            {
                _fileViewerViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_claudeTasksPanelViewModel.IsOpen)
            {
                _claudeTasksPanelViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_panelRouter?.IsOpen("help") == true)
            {
                _panelRouter.Close("help");
                e.Handled = true;
                return;
            }
        }

        // Ctrl+F1: What's New / Recent Features
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenWhatsNewPanel();
            e.Handled = true;
            return;
        }

        // F1: Toggle help popup
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _panelRouter?.Show<HelpViewModel>();
            e.Handled = true;
            return;
        }

        // F5: Start/Stop run
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ToggleRunCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // Shift+F5: Force stop run
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && terminalTab.CanStop)
            {
                terminalTab.StopRunCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // F4: Toggle voice listening
        if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _viewModel.ToggleVoiceListening();
            e.Handled = true;
            return;
        }

        // F6: Run tests
        if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel termTab)
            {
                termTab.SetPanel(_testResultsViewModel);
                termTab.ShowCenterPanel(_testResultsViewModel);
            }
            await _testResultsViewModel.RunAllTestsAsync();
            e.Handled = true;
            return;
        }

        // Handle Tab and Shift+Tab specially for terminals - prevent WPF from stealing focus
        if (e.Key == Key.Tab && IsFocusInTerminal())
        {
            // Send Tab character to the terminal manually since we're blocking WPF navigation
            var tabChar = Keyboard.Modifiers == ModifierKeys.Shift ? "\x1b[Z" : "\t"; // Shift+Tab sends escape sequence
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.GetFocusedSession()?.SendText(tabChar, appendNewline: false);
            }
            else if (_viewModel.SelectedTab is ProfileTerminalTabViewModel profileTab)
            {
                profileTab.Session?.SendText(tabChar, appendNewline: false);
            }
            e.Handled = true;
            return;
        }

        // Handle Ctrl+V for paste into terminal.
        // Text paste is always handled by us (SendText) since raw Ctrl+V sends 0x16 to the PTY.
        // Image paste differs by environment:
        //   Container: let Ctrl+V pass through → Claude Code reads clipboard via our shims.
        //   Non-container: send Alt+V escape → Claude Code reads Windows clipboard directly.
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && IsFocusInTerminal())
        {
            var session = (_viewModel.SelectedTab as TerminalPairTabViewModel)?.GetFocusedSession()
                ?? (_viewModel.SelectedTab as ProfileTerminalTabViewModel)?.Session;

            if (Clipboard.ContainsImage())
            {
                if (session != null && !string.IsNullOrEmpty(session.Profile.ContainerName))
                {
                    // Container: let Ctrl+V reach Claude Code — it reads the image
                    // via our xclip shim that proxies to the host clipboard API.
                    return;
                }
                // Non-container: send Alt+V escape — Claude Code on Windows uses this
                session?.SendText("\x1bv", appendNewline: false);
            }
            else if (Clipboard.ContainsText())
            {
                // Text paste: always handle ourselves (both container and non-container)
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text) && session != null)
                    _ = PasteTextChunkedAsync(session, text);
            }
            e.Handled = true;
            return;
        }

        // Handle Ctrl+C for copy from terminal (only if there's a selection, otherwise let it pass through for SIGINT)
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && IsFocusInTerminal())
        {
            // Try to copy selected text - if successful, handle the event; otherwise let Ctrl+C pass through
            bool copied = false;
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                copied = terminalTab.GetFocusedSession()?.CopySelectionToClipboard() ?? false;
            }
            else if (_viewModel.SelectedTab is ProfileTerminalTabViewModel profileTab)
            {
                copied = profileTab.Session?.CopySelectionToClipboard() ?? false;
            }

            if (copied)
            {
                e.Handled = true;
                return;
            }
            // If no selection was copied, don't handle - let Ctrl+C pass through to terminal for interrupt
        }

        // Only handle shortcuts with modifiers - let other unmodified keys pass through to terminal
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            return; // Don't intercept unmodified keys
        }

        // Ctrl+PageDown: Next tab
        if (e.Key == Key.PageDown && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.CycleTabCommand.Execute(true);
            e.Handled = true;
        }
        // Ctrl+PageUp: Previous tab
        else if (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.CycleTabCommand.Execute(false);
            e.Handled = true;
        }
        // Ctrl+1-9: Jump to specific tab
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < _viewModel.Tabs.Count)
            {
                _viewModel.SelectedTab = _viewModel.Tabs[index];
            }
            e.Handled = true;
        }
        // Ctrl+W: Close current tab
        else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_viewModel.SelectedTab != null)
            {
                _viewModel.CloseTabCommand.Execute(_viewModel.SelectedTab);
            }
            e.Handled = true;
        }
        // Ctrl+`: Switch between custom and shell terminal
        else if (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control) // Oem3 is the ` key
        {
            _viewModel.SwitchActiveTerminalCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+N: New project
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenNewProjectCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+,: Open settings
        else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenSettingsCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+P: Open profiles
        else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenProfilesCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+E: Open in Explorer
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenInExplorerCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+T: Open tab switcher
        else if (e.Key == Key.T && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _panelRouter?.Show<TabSwitcherViewModel>();
            e.Handled = true;
        }
        // Ctrl+O: Open file viewer (preview mode) as center panel
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var initialDir = _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab
                ? terminalTab.Pair.WorkingDirectory
                : string.Empty;
            _fileViewerViewModel.OpenDialogCommand.Execute(initialDir);
            if (_fileViewerViewModel.IsOpen && _viewModel.SelectedTab is TerminalPairTabViewModel tab)
            {
                tab.SetPanel(_fileViewerViewModel);
                tab.ShowCenterPanel(_fileViewerViewModel);
            }
            e.Handled = true;
        }
        // Ctrl+Shift+E: Open file viewer (edit mode) as center panel
        else if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            var initialDir = _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab
                ? terminalTab.Pair.WorkingDirectory
                : string.Empty;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select File to Edit",
                Filter = "All Files (*.*)|*.*",
                InitialDirectory = initialDir
            };
            if (dialog.ShowDialog() == true)
            {
                _fileViewerViewModel.Open(dialog.FileName, FileViewerMode.Edit);
                if (_viewModel.SelectedTab is TerminalPairTabViewModel tab)
                {
                    tab.SetPanel(_fileViewerViewModel);
                    tab.ShowCenterPanel(_fileViewerViewModel);
                }
            }
            e.Handled = true;
        }
        // Ctrl+Shift+P: Open command palette
        else if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowCommandPalette();
            e.Handled = true;
        }
        // Ctrl+Shift+N: Open scratch pad
        else if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _viewModel.OpenScratchPadCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+F: Toggle file explorer
        else if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ToggleExplorerCommand.Execute(null);
            }
            e.Handled = true;
        }
        // Alt+G: Open unified Git panel on Changes tab (Ctrl+G reserved by Claude Code)
        // Note: Alt key combos come through as e.SystemKey in WPF (e.Key == Key.System)
        else if (e.SystemKey == Key.G && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            await OpenUnifiedGitPanelAsync(GitPanelTab.Changes);
            e.Handled = true;
        }
        // Ctrl+H: Open unified Git panel on History tab
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await OpenUnifiedGitPanelAsync(GitPanelTab.History);
            e.Handled = true;
        }
        // Ctrl+F3: Open search across files (center panel)
        else if (e.Key == Key.F3 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.SetPanel(_searchAcrossFilesViewModel);
                if (_searchAcrossFilesViewModel.IsOpen && terminalTab.ActiveCenterPanel == _searchAcrossFilesViewModel)
                {
                    terminalTab.CloseCenterPanel();
                }
                else
                {
                    await _searchAcrossFilesViewModel.OpenAsync(terminalTab);
                    terminalTab.ShowCenterPanel(_searchAcrossFilesViewModel);
                }
            }
            else
            {
                _dialogService.ShowInfo("Please select a project tab first.", "Search");
            }
        }
        // Ctrl+Shift+M: Open Memory Browser (center panel)
        else if (e.Key == Key.M && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            OnMemoryBrowserRequested(this, EventArgs.Empty);
        }
        // Ctrl+B: Open unified Git panel on Branches tab
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await OpenUnifiedGitPanelAsync(GitPanelTab.Branches);
            e.Handled = true;
        }
        // Ctrl+Alt+B: Open unified Git panel on Comparison tab
        // Note: Alt key combos come through as e.SystemKey in WPF
        else if (e.SystemKey == Key.B && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            await OpenUnifiedGitPanelAsync(GitPanelTab.Comparison);
            e.Handled = true;
        }
        // Ctrl+Shift+S: Open unified Git panel on Stash tab
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            await OpenUnifiedGitPanelAsync(GitPanelTab.Stash);
            e.Handled = true;
        }
        // Ctrl+Shift+G: Open git reflog (redirected to Git GUI)
        else if (e.Key == Key.G && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            // Open the unified Git panel with the Reflog shown via the Changes tab
            // (Reflog is not a separate tab in the unified panel, so we open Changes)
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                _reflogViewModel.SetTerminalTab(terminalTab);
                await _reflogViewModel.LoadAsync();
                _reflogViewModel.IsOpen = true;
            }
            e.Handled = true;
        }
        // Ctrl+Shift+O: Open repository switcher
        else if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            await _repositorySwitcherViewModel.OpenAsync();
            e.Handled = true;
        }
                // Ctrl+Shift+H: Open GitHub Dashboard
        else if (e.Key == Key.H && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            await _viewModel.OpenDashboardCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        // Ctrl+Shift+R: Open PR Review Mode (center panel)
        else if (e.Key == Key.R && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
            if (currentTab != null)
            {
                currentTab.SetPanel(_prReviewViewModel);
                if (_prReviewViewModel.IsOpen && currentTab.ActiveCenterPanel == _prReviewViewModel)
                {
                    currentTab.CloseCenterPanel();
                }
                else
                {
                    await _prReviewViewModel.OpenAsync(currentTab.WorkingDirectory);
                    currentTab.ShowCenterPanel(_prReviewViewModel);
                }
            }
            e.Handled = true;
        }
        // Ctrl+Shift+I: Open Timeline Mode
        else if (e.Key == Key.I && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _viewModel.OpenTimelineCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+K: Open Claude Tasks Panel
        else if (e.Key == Key.K && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenClaudeTasksPanel();
            e.Handled = true;
        }
        // Ctrl+Shift+A: Toggle Sessions Panel (global)
        else if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OnSessionsTreeRequested(this, EventArgs.Empty);
            e.Handled = true;
        }
        // Ctrl+M: Open Markdown Preview
        else if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await OpenMarkdownPreviewAsync();
            e.Handled = true;
        }
        // Ctrl+L: Toggle layout mode (Tabs/WorkspaceSidebar)
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.ToggleLayoutModeCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+Y: Toggle status overlay
        else if (e.Key == Key.Y && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _statusOverlayService.Toggle();
            e.Handled = true;
        }
        // Ctrl+Shift+V: Open Spark Canvas window
        else if (e.Key == Key.V && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _viewModel.OpenSparkCanvasWindow();
            e.Handled = true;
        }
        // Ctrl+Shift+D: Git Pull
        else if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel pullTab)
                pullTab.GitPullCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+U: Git Push
        else if (e.Key == Key.U && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel pushTab)
                pushTab.GitPushCommand.Execute(null);
            e.Handled = true;
        }
        // Check quick command shortcuts
        else if (TryExecuteQuickCommandShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
        // Check profile launch shortcuts
        else if (TryExecuteProfileShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
        // Check Claude command shortcuts
        else if (TryExecuteClaudeCommandShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    // Chunk large pastes so we don't overflow ConPTY's ~4KB input pipe buffer,
    // which silently drops everything but the tail of a single oversized write.
    private static async Task PasteTextChunkedAsync(TerminalSession session, string text)
    {
        const int chunkSize = 512;
        for (int i = 0; i < text.Length; i += chunkSize)
        {
            var chunk = text.Substring(i, Math.Min(chunkSize, text.Length - i));
            session.SendText(chunk, appendNewline: false);
            if (i + chunkSize < text.Length)
                await Task.Delay(5);
        }
    }

    private bool TryExecuteQuickCommandShortcut(Key key, ModifierKeys modifiers)
    {
        foreach (var command in _viewModel.QuickCommands)
        {
            if (string.IsNullOrEmpty(command.Shortcut)) continue;

            if (TryParseShortcut(command.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                if (key == expectedKey && modifiers == expectedModifiers)
                {
                    _viewModel.ExecuteQuickCommandCommand.Execute(command);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryParseShortcut(string shortcut, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        // Parse modifiers and key
        foreach (var part in parts)
        {
            var upperPart = part.ToUpperInvariant();
            switch (upperPart)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                default:
                    // Try to parse as a Key
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else if (part.Length == 1 && char.IsLetter(part[0]))
                    {
                        // Single letter key (A-Z)
                        key = (Key)Enum.Parse(typeof(Key), part.ToUpperInvariant());
                    }
                    else if (part.Length == 1 && char.IsDigit(part[0]))
                    {
                        // Number key (0-9) - use D0-D9 for top row
                        key = (Key)Enum.Parse(typeof(Key), "D" + part);
                    }
                    break;
            }
        }

        return key != Key.None && modifiers != ModifierKeys.None;
    }

    private bool TryExecuteProfileShortcut(Key key, ModifierKeys modifiers)
    {
        foreach (var profile in _profileRegistry.Profiles)
        {
            if (string.IsNullOrEmpty(profile.Shortcut)) continue;

            if (TryParseShortcut(profile.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                if (key == expectedKey && modifiers == expectedModifiers)
                {
                    _viewModel.OpenProfileTab(profile);
                    return true;
                }
            }
        }
        return false;
    }

    private bool TryExecuteClaudeCommandShortcut(Key key, ModifierKeys modifiers)
    {
        // Get all Claude commands for the current project
        var claudeCommands = _viewModel.GetClaudeCommandsForCurrentProject();

        foreach (var command in claudeCommands)
        {
            if (string.IsNullOrEmpty(command.Shortcut)) continue;

            if (TryParseShortcut(command.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                if (key == expectedKey && modifiers == expectedModifiers)
                {
                    _viewModel.ExecuteClaudeCommand(command);
                    return true;
                }
            }
        }
        return false;
    }

    #endregion

    #region Generic Panel Handlers

    /// <summary>
    /// Generic handler for all panel ShowRequested events.
    /// Routes to appropriate display mode based on panel's DisplayState.
    /// </summary>
    private void OnPanelShowRequested(object? sender, EventArgs e)
    {
        if (sender is not IPanelableViewModel panel) return;

        switch (panel.DisplayState)
        {
            case PanelDisplayState.Panel:
                if (IsCenterPanel(panel))
                    ShowCenterPanelInTab(panel);
                else
                    ShowPanelInTab(panel);
                break;

            case PanelDisplayState.Window:
                // Show in window
                _panelWindowManager?.ShowWindow(panel, OnPanelWindowDockRequested);
                break;
        }
    }

    /// <summary>
    /// Shows a panel in the current tab's right sidebar area.
    /// </summary>
    private void ShowPanelInTab(IPanelableViewModel panel)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel currentTab)
        {
            currentTab.SetPanel(panel);
            currentTab.ShowPanel(panel);
        }
    }

    /// <summary>
    /// Shows a panel in the current tab's center area, replacing terminals.
    /// If the panel is currently in a window, focuses the window instead.
    /// </summary>
    private void ShowCenterPanelInTab(IPanelableViewModel panel)
    {
        if (panel.DisplayState == PanelDisplayState.Window)
        {
            _panelWindowManager?.GetWindow(panel.PanelId)?.Activate();
            return;
        }

        if (_viewModel.SelectedTab is TerminalPairTabViewModel currentTab)
        {
            currentTab.SetPanel(panel);
            currentTab.ShowCenterPanel(panel);
        }
    }

    /// <summary>
    /// Generic handler for panel dock requests from windows.
    /// </summary>
    private void OnPanelWindowDockRequested(IPanelableViewModel panel)
    {
        _panelWindowManager?.CloseWindow(panel.PanelId);
        panel.DisplayState = PanelDisplayState.Panel;

        // Check if this is a center-type panel that should return to center
        if (_viewModel.SelectedTab is TerminalPairTabViewModel currentTab && currentTab.ActiveCenterPanel == null && IsCenterPanel(panel))
        {
            currentTab.SetPanel(panel);
            currentTab.ShowCenterPanel(panel);
        }
        else
        {
            ShowPanelInTab(panel);
        }
    }

    /// <summary>
    /// Determines if a panel is typically shown in the center area.
    /// </summary>
    private static bool IsCenterPanel(IPanelableViewModel panel) => panel.PanelId switch
    {
        "unifiedGit" or "branchComparison" or "searchFiles" or "markdownPreview"
            or "fileViewer" or "prReview" or "testResults" or "recentFeatures"
            or "mergeConflict" or "fileHistory" or "fileBlame" or "debugLog" => true,
        _ => false
    };

    #endregion

    #region Git Files Panel

    private async void OnGitChangesRequested(object? sender, EventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null)
        {
            _dialogService.ShowInfo("Please select a project tab first.", "Git Changes");
            return;
        }

        // Ensure the tab has the panel reference
        currentTab.SetPanel(_gitFilesViewModel);

        // If already open, use toggle behavior
        if (_gitFilesViewModel.IsOpen)
        {
            // If in window state, close the window
            if (_gitFilesViewModel.DisplayState == PanelDisplayState.Window)
            {
                _panelWindowManager?.CloseWindow(_gitFilesViewModel.PanelId);
                _gitFilesViewModel.IsOpen = false;
                return;
            }

            // Otherwise, toggle the docked panel (handles focus/visibility)
            currentTab.TogglePanel(_gitFilesViewModel);
            return;
        }

        // Not open yet - open and show
        await _gitFilesViewModel.OpenAsync(currentTab);
    }

    private async void OnSidebarGitPanelRequested(object? sender, string workspacePath)
    {
        // Open the unified git panel for the workspace
        await OpenUnifiedGitPanelAsync(GitPanelTab.Changes);
    }

    private async void OnSidebarStashPanelRequested(object? sender, EventArgs e)
    {
        await OpenUnifiedGitPanelAsync(GitPanelTab.Stash);
    }

    private async void OnMergeConflictRequested(object? sender, EventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null) return;

        currentTab.SetPanel(_mergeConflictViewModel);
        await _mergeConflictViewModel.OpenAsync(currentTab);
    }

    private async void OnUnifiedGitPanelRequested(object? sender, GitPanelTab tab)
    {
        await OpenUnifiedGitPanelAsync(tab);
    }

    private async void OnCenterPanelRestoreRequested(object? sender, CenterPanelRestoreEventArgs e)
    {
        if (e.Tab is not TerminalPairTabViewModel tab) return;

        // Helper: associate panel with tab and mark it as the active center panel.
        // When SkipDataLoad is true (non-selected tabs during startup), skip async data loading
        // to avoid race conditions with singleton panel ViewModels. Data loads on demand
        // when the user switches to the tab (via OnViewModelPropertyChanged rebind).
        void AssociateOnly(IPanelableViewModel panel)
        {
            tab.SetPanel(panel);
            tab.ShowCenterPanel(panel);
        }

        switch (e.PanelId)
        {
            case "unifiedGit":
                var gitTab = GitPanelTab.Changes;
                if (e.GitPanelActiveTab != null && Enum.TryParse<GitPanelTab>(e.GitPanelActiveTab, out var parsedTab))
                {
                    gitTab = parsedTab;
                }
                if (e.SkipDataLoad)
                {
                    AssociateOnly(_unifiedGitPanelViewModel);
                }
                else
                {
                    tab.SetPanel(_unifiedGitPanelViewModel);
                    await _unifiedGitPanelViewModel.OpenOnTabAsync(tab, gitTab);
                    tab.ShowCenterPanel(_unifiedGitPanelViewModel);
                }
                break;
            case "branchComparison":
                if (e.SkipDataLoad)
                {
                    AssociateOnly(_branchComparisonViewModel);
                }
                else
                {
                    tab.SetPanel(_branchComparisonViewModel);
                    await _branchComparisonViewModel.OpenAsync(tab);
                    tab.ShowCenterPanel(_branchComparisonViewModel);
                }
                break;
            case "searchFiles":
                if (e.SkipDataLoad)
                {
                    AssociateOnly(_searchAcrossFilesViewModel);
                }
                else
                {
                    tab.SetPanel(_searchAcrossFilesViewModel);
                    await _searchAcrossFilesViewModel.OpenAsync(tab);
                    tab.ShowCenterPanel(_searchAcrossFilesViewModel);
                }
                break;
            case "markdownPreview":
                tab.SetPanel(_markdownPreviewViewModel);
                _markdownPreviewViewModel.IsOpen = true;
                tab.ShowCenterPanel(_markdownPreviewViewModel);
                break;
            case "fileViewer":
                tab.SetPanel(_fileViewerViewModel);
                _fileViewerViewModel.IsOpen = true;
                tab.ShowCenterPanel(_fileViewerViewModel);
                break;
            case "prReview":
                if (e.SkipDataLoad)
                {
                    AssociateOnly(_prReviewViewModel);
                }
                else
                {
                    tab.SetPanel(_prReviewViewModel);
                    await _prReviewViewModel.OpenAsync(tab.WorkingDirectory);
                    tab.ShowCenterPanel(_prReviewViewModel);
                }
                break;
            case "testResults":
                tab.SetPanel(_testResultsViewModel);
                _testResultsViewModel.IsOpen = true;
                tab.ShowCenterPanel(_testResultsViewModel);
                break;
            case "recentFeatures":
                tab.SetPanel(_recentFeaturesViewModel);
                _recentFeaturesViewModel.OnOpened();
                tab.ShowCenterPanel(_recentFeaturesViewModel);
                break;
        }
    }

    private void OnRightPanelRestoreRequested(object? sender, RightPanelRestoreEventArgs e)
    {
        // Map panel IDs to ViewModel instances
        IPanelableViewModel? GetPanelById(string panelId) => panelId switch
        {
            "fileExplorer" => e.Tab.ExplorerPanelViewModel,
            "claudeTasks" => _claudeTasksPanelViewModel,
            "detectedLinks" => _detectedLinksViewModel,
            "scratchPad" => _scratchPadViewModel,
            "gitChanges" => _gitFilesViewModel,
            "sessionsTree" => _sessionsTreePanelViewModel,
            _ => null
        };

        foreach (var panelId in e.PanelIds)
        {
            var panel = GetPanelById(panelId);
            if (panel != null)
            {
                e.Tab.SetPanel(panel);
                panel.IsOpen = true;
                e.Tab.AddPanel(panel, PanelSide.Right);
            }
        }

        if (e.ActivePanelId != null)
        {
            var activePanel = GetPanelById(e.ActivePanelId);
            if (activePanel != null)
            {
                e.Tab.ActiveRightPanel = activePanel;
            }
        }

        if (e.PanelIds.Count > 0)
        {
            e.Tab.IsExplorerVisible = true;
        }
    }

    #endregion

    #region Scratch Pad Panel

    private void OnScratchPadRequested(object? sender, EventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;

        // Ensure the tab has the panel reference
        if (currentTab != null)
        {
            currentTab.SetPanel(_scratchPadViewModel);
        }

        // If already open, use toggle behavior
        if (_scratchPadViewModel.IsOpen)
        {
            // If in window state, close the window
            if (_scratchPadViewModel.DisplayState == PanelDisplayState.Window)
            {
                _panelWindowManager?.CloseWindow(_scratchPadViewModel.PanelId);
                _scratchPadViewModel.IsOpen = false;
                return;
            }

            // Otherwise, toggle the panel (handles focus/visibility)
            currentTab?.TogglePanel(_scratchPadViewModel);
            return;
        }

        // Not open yet - set display state to Panel for docked display
        _scratchPadViewModel.DisplayState = PanelDisplayState.Panel;
        _scratchPadViewModel.Open();
    }

    #endregion

    #region Claude Tasks Panel

    private void OpenClaudeTasksPanel()
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null) return;

        var workspacePath = currentTab.WorkingDirectory;
        currentTab.SetPanel(_claudeTasksPanelViewModel);
        _claudeTasksPanelViewModel.SetWorkspace(workspacePath);

        // Toggle sidebar panel
        if (_claudeTasksPanelViewModel.IsOpen)
        {
            _claudeTasksPanelViewModel.OnOpened();
            currentTab.TogglePanel(_claudeTasksPanelViewModel);
        }
        else
        {
            _claudeTasksPanelViewModel.Open(workspacePath);
        }
    }

    #endregion

    #region Sessions Tree Panel

    private void OnSessionsTreeRequested(object? sender, EventArgs e)
    {
        // Sessions panel visibility is global (#77): flip the flag and let
        // OnSessionsPanelVisibilityChanged handle the per-tab sync.
        _viewModel.ShowSessionsPanel = !_viewModel.ShowSessionsPanel;
    }

    /// <summary>
    /// Syncs the shared Sessions panel across every open terminal-pair tab
    /// when the global ShowSessionsPanel flag flips.
    /// </summary>
    private void OnSessionsPanelVisibilityChanged(object? sender, bool isVisible)
    {
        if (isVisible)
        {
            _sessionsTreePanelViewModel.DisplayState = PanelDisplayState.Panel;
            _sessionsTreePanelViewModel.Open();
        }

        foreach (var tab in _viewModel.Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.SetPanel(_sessionsTreePanelViewModel);
            if (isVisible)
            {
                tab.ShowPanel(_sessionsTreePanelViewModel);
            }
            else
            {
                tab.HidePanel(_sessionsTreePanelViewModel);
            }
        }

        if (!isVisible)
        {
            _sessionsTreePanelViewModel.IsOpen = false;
        }
    }

    #endregion

    #region What's New / Recent Features

    private void OnWhatsNewRequested(object? sender, EventArgs e)
    {
        OpenWhatsNewPanel();
    }

    private void OpenWhatsNewPanel()
    {
        // If no tab is selected, the empty state already shows What's New
        if (_viewModel.SelectedTab is not TerminalPairTabViewModel currentTab)
            return;

        // Show as center panel
        currentTab.SetPanel(_recentFeaturesViewModel);
        _recentFeaturesViewModel.OnOpened();
        currentTab.ShowCenterPanel(_recentFeaturesViewModel);
    }

    #endregion

    #region Palette Event Handlers

    private async void OnReflogRequested(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            _reflogViewModel.SetTerminalTab(terminalTab);
            await _reflogViewModel.LoadAsync();
            _reflogViewModel.IsOpen = true;
        }
    }

    private async void OnRepositorySwitcherRequested(object? sender, EventArgs e)
    {
        await _repositorySwitcherViewModel.OpenAsync();
    }

    private async void OnSearchRequested(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.SetPanel(_searchAcrossFilesViewModel);
            if (_searchAcrossFilesViewModel.IsOpen && terminalTab.ActiveCenterPanel == _searchAcrossFilesViewModel)
            {
                terminalTab.CloseCenterPanel();
            }
            else
            {
                await _searchAcrossFilesViewModel.OpenAsync(terminalTab);
                terminalTab.ShowCenterPanel(_searchAcrossFilesViewModel);
            }
        }
    }

    private async void OnMemoryBrowserRequested(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.SetPanel(_memoryBrowserViewModel);
            if (_memoryBrowserViewModel.IsOpen && terminalTab.ActiveCenterPanel == _memoryBrowserViewModel)
            {
                terminalTab.CloseCenterPanel();
            }
            else
            {
                await _memoryBrowserViewModel.OpenAsync(terminalTab);
                terminalTab.ShowCenterPanel(_memoryBrowserViewModel);
            }
        }
    }

    private void OnDebugLogRequested(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.SetPanel(_debugLogViewModel);
            if (_debugLogViewModel.IsOpen && terminalTab.ActiveCenterPanel == _debugLogViewModel)
            {
                terminalTab.CloseCenterPanel();
            }
            else
            {
                _debugLogViewModel.Open();
                terminalTab.ShowCenterPanel(_debugLogViewModel);
            }
        }
    }

    private void OnClaudeTasksRequested(object? sender, EventArgs e)
    {
        OpenClaudeTasksPanel();
    }

    private async void OnTestRunnerRequested(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel termTab)
        {
            termTab.SetPanel(_testResultsViewModel);
            termTab.ShowCenterPanel(_testResultsViewModel);
        }
        await _testResultsViewModel.RunAllTestsAsync();
    }

    private void OnAiPanelCommandRequested(object? sender, string action)
    {
        switch (action)
        {
            case "explain-blame":
                _fileBlameViewModel.ExplainBlameLineCommand.Execute(null);
                break;
            case "summarize-file-history":
                _fileHistoryViewModel.SummarizeFileHistoryCommand.Execute(null);
                break;
            case "explain-commit":
                _commitHistoryViewModel.ExplainCommitCommand.Execute(null);
                break;
            case "explain-reflog":
                _reflogViewModel.ExplainReflogCommand.Execute(null);
                break;
            case "generate-stash-name":
                _gitStashViewModel.GenerateStashNameCommand.Execute(null);
                break;
            case "assess-merge-risk":
                _branchComparisonViewModel.AssessMergeRiskCommand.Execute(null);
                break;
            case "suggest-version":
                _gitTagsViewModel.SuggestVersionCommand.Execute(null);
                break;
            case "analyze-ci-failure":
                var dashboard = _viewModel.Tabs.OfType<DashboardTabViewModel>().FirstOrDefault();
                dashboard?.AnalyzeCiFailureCommand.Execute(null);
                break;
            case "prioritize-prs":
                var dashboardForPr = _viewModel.Tabs.OfType<DashboardTabViewModel>().FirstOrDefault();
                dashboardForPr?.PrioritizePrsCommand.Execute(null);
                break;
            case "improve-markdown":
                _markdownPreviewViewModel.ImproveMarkdownCommand.Execute(null);
                break;
        }
    }

    #endregion

    #region Branch Comparison Panel

    private async void OnCompareBranchesRequested(object? sender, CompareBranchesRequestedEventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null)
        {
            _dialogService.ShowInfo("Please select a project tab first.", "Branch Comparison");
            return;
        }

        currentTab.SetPanel(_branchComparisonViewModel);
        // Open comparison view with the specified branches in center panel
        await _branchComparisonViewModel.OpenWithBranchesAsync(currentTab, e.BaseBranch, e.CompareBranch);
        currentTab.ShowCenterPanel(_branchComparisonViewModel);
    }

    private async Task OpenBranchComparisonAsync()
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null)
        {
            _dialogService.ShowInfo("Please select a project tab first.", "Branch Comparison");
            return;
        }

        currentTab.SetPanel(_branchComparisonViewModel);

        // If already open as center panel, toggle off
        if (_branchComparisonViewModel.IsOpen && currentTab.ActiveCenterPanel == _branchComparisonViewModel)
        {
            currentTab.CloseCenterPanel();
            return;
        }

        await _branchComparisonViewModel.OpenAsync(currentTab);
        currentTab.ShowCenterPanel(_branchComparisonViewModel);
    }

    private async Task OpenUnifiedGitPanelAsync(GitPanelTab? tab = null)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null)
        {
            _dialogService.ShowInfo("Please select a project tab first.", "Git Panel");
            return;
        }

        // Register the panel with the tab
        currentTab.SetPanel(_unifiedGitPanelViewModel);

        // If already open as center panel, toggle off or switch tab
        if (_unifiedGitPanelViewModel.IsOpen && currentTab.ActiveCenterPanel == _unifiedGitPanelViewModel)
        {
            if (tab.HasValue && _unifiedGitPanelViewModel.ActiveTab != tab.Value)
            {
                // Switch to the requested tab instead of closing
                _unifiedGitPanelViewModel.ActiveTab = tab.Value;
                return;
            }
            // Toggle off - return to terminals
            currentTab.CloseCenterPanel();
            return;
        }

        // Open in center panel
        await _unifiedGitPanelViewModel.OpenOnTabAsync(currentTab, tab ?? GitPanelTab.Changes);
        currentTab.ShowCenterPanel(_unifiedGitPanelViewModel);
    }

    #endregion

    #region Setup Window

    private void OnSetupRequested(object? sender, EventArgs e)
    {
        var setupViewModel = new SetupViewModel();
        var setupWindow = new Views.SetupWindow(setupViewModel, isStartupMode: false)
        {
            Owner = this
        };
        setupWindow.ShowDialog();
    }

    private async void OnPrReviewRequested(object? sender, EventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab != null)
        {
            await _prReviewViewModel.OpenAsync(currentTab.WorkingDirectory);
        }
    }

    private async void OnDashboardPrReviewRequested(object? sender, PrReviewRequestedEventArgs e)
    {
        // Find the tab for this working directory (OpenProjectTab was already called)
        var tab = _viewModel.Tabs.OfType<TerminalPairTabViewModel>()
            .FirstOrDefault(t => string.Equals(t.WorkingDirectory, e.WorkingDirectory, StringComparison.OrdinalIgnoreCase))
            ?? _viewModel.SelectedTab as TerminalPairTabViewModel;

        if (tab == null) return;

        // Switch to the tab and open the PR review panel
        _viewModel.SelectedTab = tab;
        tab.SetPanel(_prReviewViewModel);
        await _prReviewViewModel.OpenForPrAsync(e.WorkingDirectory, e.PullRequest);
        tab.ShowCenterPanel(_prReviewViewModel);
    }

    private async void OnMarkdownPreviewRequested(object? sender, EventArgs e)
    {
        await OpenMarkdownPreviewAsync();
    }

    private async Task OpenMarkdownPreviewAsync()
    {
        // Get current tab
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null) return;

        // Cycle behavior:
        //   FileViewer in center (with .md file) → move to sidebar as MarkdownPreview → close sidebar

        // State 1: FileViewer is the active center panel showing a markdown file
        if (currentTab.ActiveCenterPanel == _fileViewerViewModel &&
            _fileViewerViewModel.IsOpen &&
            !string.IsNullOrEmpty(_fileViewerViewModel.FilePath) &&
            _fileViewerViewModel.FilePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var mdPath = _fileViewerViewModel.FilePath;

            // Close center panel (FileViewer)
            currentTab.CloseCenterPanel();

            // Open MarkdownPreview in sidebar
            currentTab.SetPanel(_markdownPreviewViewModel);
            _markdownPreviewViewModel.DisplayState = PanelDisplayState.Panel;
            await _markdownPreviewViewModel.OpenAsync(mdPath);
            currentTab.ShowPanel(_markdownPreviewViewModel);
            return;
        }

        // State 2: MarkdownPreview is open in sidebar → close it
        if (_markdownPreviewViewModel.IsOpen && currentTab.RightPanels.Contains(_markdownPreviewViewModel))
        {
            currentTab.TogglePanel(_markdownPreviewViewModel);
            return;
        }

        // State 2b: MarkdownPreview is the active center panel → close it
        if (currentTab.ActiveCenterPanel == _markdownPreviewViewModel)
        {
            currentTab.CloseCenterPanel();
            return;
        }

        // State 2c: MarkdownPreview is in window state → close the window
        if (_markdownPreviewViewModel.IsOpen && _markdownPreviewViewModel.DisplayState == PanelDisplayState.Window)
        {
            _panelWindowManager?.CloseWindow(_markdownPreviewViewModel.PanelId);
            _markdownPreviewViewModel.OnWindowClosed();
            return;
        }

        // State 3: Nothing open → find a markdown file and open FileViewer in center
        var workingDir = currentTab.WorkingDirectory;
        if (string.IsNullOrEmpty(workingDir)) return;

        // Try to find a markdown file in the project
        var mdFiles = new[] { "README.md", "readme.md", "README.MD", "CHANGELOG.md", "CONTRIBUTING.md" };
        string? filePath = null;

        foreach (var mdFile in mdFiles)
        {
            var path = System.IO.Path.Combine(workingDir, mdFile);
            if (_fileSystem.FileExists(path))
            {
                filePath = path;
                break;
            }
        }

        if (filePath == null)
        {
            // Open file picker to select a markdown file
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                InitialDirectory = workingDir
            };

            if (dialog.ShowDialog() == true)
            {
                filePath = dialog.FileName;
            }
        }

        if (filePath != null)
        {
            // Open FileViewer in center (has edit/side-by-side capabilities)
            _fileViewerViewModel.Open(filePath, FileViewerMode.Preview);
            currentTab.SetPanel(_fileViewerViewModel);
            currentTab.ShowCenterPanel(_fileViewerViewModel);
        }
    }

    #endregion

    #region File Operation Handlers

    private void ShowFileViewerCenterPanel()
    {
        // If the file viewer is in a window, just focus it — Open() already updated the VM
        if (_fileViewerViewModel.DisplayState == PanelDisplayState.Window)
        {
            _panelWindowManager?.GetWindow(_fileViewerViewModel.PanelId)?.Activate();
            return;
        }

        if (_viewModel.SelectedTab is TerminalPairTabViewModel tab)
        {
            tab.SetPanel(_fileViewerViewModel);
            tab.ShowCenterPanel(_fileViewerViewModel);
        }
    }

    private void OnFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        var mode = e.OpenInEditMode ? FileViewerMode.Edit : FileViewerMode.Preview;
        _fileViewerViewModel.Open(e.FilePath, mode, e.Line);
        ShowFileViewerCenterPanel();
    }

    private void OnFileEditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Edit);
        ShowFileViewerCenterPanel();
    }

    private void OnFileViewerDetachRequested(object? sender, EventArgs e)
    {
        // Close the popup and open in a new window
        var filePath = _fileViewerViewModel.FilePath;
        var mode = _fileViewerViewModel.Mode;

        _fileViewerViewModel.IsOpen = false;

        if (!string.IsNullOrEmpty(filePath))
        {
            // Create a new FileViewerViewModel for the detached window
            var detachedViewModel = new FileViewerViewModel(
                App.Current.Services.GetRequiredService<IFilePreviewService>(),
                App.Current.Services.GetRequiredService<IFileEditService>(),
                App.Current.Services.GetRequiredService<IFileSystem>(),
                App.Current.Services.GetRequiredService<IDialogService>(),
                App.Current.Services.GetRequiredService<IMarkdownService>(),
                App.Current.Services.GetRequiredService<ITimerService>());
            detachedViewModel.IsDetached = true;
            detachedViewModel.Open(filePath, mode);

            var window = new Views.FileViewerWindow { DataContext = detachedViewModel };
            window.Show();
        }
    }

    private void OnFileHistoryRequested(object? sender, string filePath)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileHistory(terminalTab.Pair.WorkingDirectory, filePath);
        }
    }

    private void OnFileHistoryRequestedFromExplorer(object? sender, FileHistoryRequestedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileHistory(terminalTab.Pair.WorkingDirectory, e.FilePath);
        }
    }

    private void OnFileBlameRequested(object? sender, string filePath)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileBlame(terminalTab.Pair.WorkingDirectory, filePath);
        }
    }

    private void OnFileBlameRequestedFromExplorer(object? sender, FileBlameRequestedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileBlame(terminalTab.Pair.WorkingDirectory, e.FilePath);
        }
    }

    private async void OpenFileHistory(string workingDirectory, string filePath)
    {
        await _fileHistoryViewModel.OpenAsync(workingDirectory, filePath);
    }

    private async void OpenFileBlame(string workingDirectory, string filePath)
    {
        await _fileBlameViewModel.OpenAsync(workingDirectory, filePath);
    }

    #endregion

    #region Manage Worktrees

    private async void OnManageWorktreesRequested(object? sender, (string RepoPath, string RepoName) args)
    {
        await _manageWorktreesViewModel.OpenAsync(args.RepoPath, args.RepoName);
    }

    private void OnManageWorktreesOpenWorktree(object? sender, string worktreePath)
    {
        // Open the worktree as a new tab
        _viewModel.OpenProjectTab(worktreePath);
    }

    private async void OnManageWorktreesCreateWorktree(object? sender, string repoPath)
    {
        // Trigger the create worktree flow from WorkspaceSidebar
        if (_viewModel.WorkspaceSidebar != null)
        {
            var workspace = _viewModel.WorkspaceSidebar.Workspaces
                .FirstOrDefault(w => string.Equals(w.Path, repoPath, StringComparison.OrdinalIgnoreCase))
                ?? _viewModel.WorkspaceSidebar.Playgrounds
                .FirstOrDefault(w => string.Equals(w.Path, repoPath, StringComparison.OrdinalIgnoreCase));

            if (workspace != null)
            {
                await _viewModel.WorkspaceSidebar.CreateWorktreeCommand.ExecuteAsync(workspace);
            }
        }
    }

    #endregion

    #region Terminal Focus Detection

    /// <summary>
    /// Checks if the currently focused element is inside a terminal control.
    /// This uses multiple detection methods because EasyTerminalControl uses native HWND hosting.
    /// </summary>
    private bool IsFocusInTerminal()
    {
        // Method 1: Check if the selected tab is a terminal tab and no WPF input control has focus
        var focused = Keyboard.FocusedElement;

        // If a TextBox, ComboBox, or other WPF input control has focus, Tab should work normally there
        if (focused is System.Windows.Controls.TextBox ||
            focused is System.Windows.Controls.ComboBox ||
            focused is System.Windows.Controls.ListBox ||
            focused is System.Windows.Controls.ListView)
        {
            return false;
        }

        // If we're in a terminal tab, assume terminal has focus (HWND hosting doesn't report to WPF)
        if (_viewModel.SelectedTab is TerminalPairTabViewModel || _viewModel.SelectedTab is ProfileTerminalTabViewModel)
        {
            // Method 2: Walk up the visual tree from focused element to find EasyTerminalControl
            var focusedDep = focused as DependencyObject;
            while (focusedDep != null)
            {
                if (focusedDep is EasyWindowsTerminalControl.EasyTerminalControl)
                    return true;

                focusedDep = System.Windows.Media.VisualTreeHelper.GetParent(focusedDep);
            }

            // Method 3: If no WPF element really has focus, the HWND terminal likely has it
            // Check if focus is on the window itself or no specific element
            if (focused == null || focused == this)
            {
                return true;
            }

            // Method 4: Check if focus is on the ContentPresenter holding the terminal
            if (focused is System.Windows.Controls.ContentPresenter ||
                focused is System.Windows.Controls.ContentControl ||
                focused is System.Windows.Controls.Grid ||
                focused is System.Windows.Controls.UserControl)
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Taskbar Progress/Glow

    /// <summary>
    /// Called when window becomes active (gains focus).
    /// Clears taskbar glow since user is now looking at the app.
    /// </summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _isWindowActivated = true;
        _taskbarProgressService?.ClearGlow();
        _soundService?.SetAppFocused(true);
        _statusOverlayService.OnMainWindowActivated();
    }

    /// <summary>
    /// Called when window becomes inactive (loses focus).
    /// Enables taskbar notifications for terminal activity.
    /// </summary>
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _isWindowActivated = false;
        _soundService?.SetAppFocused(false);
        // Update taskbar state immediately based on current terminal activity
        UpdateTaskbarGlow();
        _statusOverlayService.OnMainWindowDeactivated();
    }

    /// <summary>
    /// Monitors ViewModel property changes to update taskbar glow when terminals change state.
    /// </summary>
    private void OnViewModelPropertyChangedForTaskbar(object? sender, PropertyChangedEventArgs e)
    {
        // Monitor selected tab changes (terminal activity indicators are tab-specific)
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            // Unsubscribe from previous tab
            if (_previousSelectedTerminalTab != null)
            {
                _previousSelectedTerminalTab.PropertyChanged -= OnTerminalTabPropertyChanged;
            }

            // Subscribe to new tab if it's a terminal tab
            if (_viewModel.SelectedTab is TerminalPairTabViewModel newTab)
            {
                newTab.PropertyChanged += OnTerminalTabPropertyChanged;
                _previousSelectedTerminalTab = newTab;
            }
            else
            {
                _previousSelectedTerminalTab = null;
            }

            UpdateTaskbarGlow();
        }
    }

    /// <summary>
    /// Monitors terminal tab property changes (activity state, waiting state, etc.)
    /// </summary>
    private void OnTerminalTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Update taskbar glow when activity indicators change
        if (e.PropertyName == nameof(TerminalPairTabViewModel.IsWaitingForInput) ||
            e.PropertyName == nameof(TerminalPairTabViewModel.HasUnreadActivity) ||
            e.PropertyName == nameof(TerminalPairTabViewModel.IsAnyTerminalActive) ||
            e.PropertyName == nameof(TerminalPairTabViewModel.ShowActivitySpinner))
        {
            UpdateTaskbarGlow();
        }

        // Play sound when terminal starts waiting for input
        if (e.PropertyName == nameof(TerminalPairTabViewModel.IsWaitingForInput) &&
            sender is TerminalPairTabViewModel tab && tab.IsWaitingForInput)
        {
            _soundService?.Play(SoundType.InputWaiting);
        }
    }

    /// <summary>
    /// Updates the taskbar glow based on current terminal activity state.
    /// Only shows glow when window is NOT active (user is in another app).
    /// Also updates the floating status overlay with the current state.
    /// </summary>
    private void UpdateTaskbarGlow()
    {
        // Always update the status overlay (it's visible when window is unfocused)
        UpdateStatusOverlay();

        if (_taskbarProgressService == null || _isWindowActivated)
        {
            // No service or window is active - no glow needed
            return;
        }

        // Check the selected tab's terminal activity state
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            // Priority: Waiting for input (amber) > Active output (indeterminate) > Completed activity (green) > No glow
            if (terminalTab.IsWaitingForInput)
            {
                // Amber glow: Claude is waiting for user input
                _taskbarProgressService.ShowAmberGlow();
            }
            else if (terminalTab.ShowActivitySpinner)
            {
                // Looping progress: terminals are actively producing output
                _taskbarProgressService.ShowIndeterminate();
            }
            else if (terminalTab.HasUnreadActivity && !terminalTab.IsAnyTerminalActive)
            {
                // Green glow: Terminal activity completed
                _taskbarProgressService.ShowGreenGlow();
            }
            else
            {
                // No activity to notify about
                _taskbarProgressService.ClearGlow();
            }
        }
        else
        {
            // Non-terminal tab selected (settings, dashboard, etc.)
            _taskbarProgressService.ClearGlow();
        }
    }

    /// <summary>
    /// Updates the floating status overlay with current terminal activity state.
    /// </summary>
    private void UpdateStatusOverlay()
    {
        if (_statusOverlayService.OverlayCount == 0) return;

        IoCounters.CurrentUiOperation = "UpdateStatusOverlay";
        try
        {
        var terminalTabs = _viewModel.Tabs.OfType<TerminalPairTabViewModel>().ToList();
        var aiName = _cachedAiName;

        // Priority: any waiting > any active > any completed > all idle
        var waitingTab = terminalTabs.FirstOrDefault(t => t.IsWaitingForInput);
        if (waitingTab != null)
        {
            _overlayFeaturedTab = waitingTab;
            var project = System.IO.Path.GetFileName(waitingTab.Pair.WorkingDirectory);
            _statusOverlayService.UpdateState("waiting", $"{project} \u2014 waiting for input");
            return;
        }

        var activeTabs = terminalTabs.Where(t => t.IsAnyTerminalActive).ToList();
        if (activeTabs.Count > 0)
        {
            if (activeTabs.Count == 1)
            {
                _overlayFeaturedTab = activeTabs[0];
                var project = System.IO.Path.GetFileName(activeTabs[0].Pair.WorkingDirectory);
                _statusOverlayService.UpdateState("active", $"{project} \u2014 {aiName} working");
            }
            else
            {
                _overlayFeaturedTab = null; // Multiple active — no single tab to focus
                _statusOverlayService.UpdateState("active", $"{activeTabs.Count} workspaces active");
            }
            return;
        }

        var completedTab = terminalTabs.FirstOrDefault(t => t.HasUnreadActivity);
        if (completedTab != null)
        {
            _overlayFeaturedTab = completedTab;
            var project = System.IO.Path.GetFileName(completedTab.Pair.WorkingDirectory);
            _statusOverlayService.UpdateState("completed", $"{project} \u2014 task completed");
            return;
        }

        // All idle - show selected tab name or generic
        _overlayFeaturedTab = null;
        if (_viewModel.SelectedTab is TerminalPairTabViewModel selectedTerminal)
        {
            var project = System.IO.Path.GetFileName(selectedTerminal.Pair.WorkingDirectory);
            _statusOverlayService.UpdateState("idle", $"{project} \u2014 idle");
        }
        else
        {
            _statusOverlayService.UpdateState("idle", "Idle");
        }
        }
        finally { IoCounters.CurrentUiOperation = null; }
    }

    /// <summary>
    /// When the overlay is clicked, switch to the featured tab (the workspace shown in the overlay).
    /// </summary>
    private void OnStatusOverlayFocusRequested(object? sender, EventArgs e)
    {
        if (_overlayFeaturedTab != null && _viewModel.Tabs.Contains(_overlayFeaturedTab))
        {
            _viewModel.SelectedTab = _overlayFeaturedTab;
        }
    }

    private void OnAnyTerminalTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TerminalPairTabViewModel.IsWaitingForInput) or
            nameof(TerminalPairTabViewModel.IsAnyTerminalActive) or
            nameof(TerminalPairTabViewModel.HasUnreadActivity))
        {
            UpdateStatusOverlay();
        }
    }

    private void OnTabsCollectionChangedForOverlay(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var tab in e.NewItems.OfType<TerminalPairTabViewModel>())
            {
                tab.PropertyChanged += OnAnyTerminalTabPropertyChanged;
                // New tab must respect the global Sessions panel flag (#77).
                if (_viewModel.ShowSessionsPanel)
                {
                    tab.SetPanel(_sessionsTreePanelViewModel);
                    tab.ShowPanel(_sessionsTreePanelViewModel);
                }
            }
        }
        if (e.OldItems != null)
            foreach (var tab in e.OldItems.OfType<TerminalPairTabViewModel>())
                tab.PropertyChanged -= OnAnyTerminalTabPropertyChanged;
        UpdateStatusOverlay();
    }

    #endregion
}