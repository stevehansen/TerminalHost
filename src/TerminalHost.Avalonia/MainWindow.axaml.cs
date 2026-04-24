using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Views;

namespace TerminalHost;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private readonly GitBranchViewModel _gitBranchViewModel;
    private readonly GitFilesViewModel _gitFilesViewModel;
    private readonly CommitHistoryViewModel _commitHistoryViewModel;
    private readonly GitStashViewModel _gitStashViewModel;
    private readonly GitTagsViewModel _gitTagsViewModel;
    private readonly ScratchPadViewModel _scratchPadViewModel;
    private readonly FileViewerViewModel _fileViewerViewModel;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly TaskPanelViewModel _taskPanelViewModel;
    private readonly ClaudeTasksPanelViewModel _claudeTasksPanelViewModel;
    private readonly SearchAcrossFilesViewModel _searchAcrossFilesViewModel;
    private readonly FileHistoryViewModel _fileHistoryViewModel;
    private readonly FileBlameViewModel _fileBlameViewModel;
    private readonly ReflogViewModel _reflogViewModel;
    private readonly ManageWorktreesViewModel _manageWorktreesViewModel;
    private readonly WorkspaceSidebarViewModel _workspaceSidebarViewModel;
    private readonly PrReviewViewModel _prReviewViewModel;
    private readonly RecentFeaturesViewModel _recentFeaturesViewModel;
    private readonly BranchComparisonViewModel _branchComparisonViewModel;
    private readonly MergeConflictViewModel _mergeConflictViewModel;
    private readonly UnifiedGitPanelViewModel _unifiedGitPanelViewModel;
    private readonly MarkdownPreviewViewModel _markdownPreviewViewModel;
    private readonly IFilePickerService _filePickerService;
    private readonly StatusOverlayService _statusOverlayService;
    private readonly IToastService _toastService;
    private readonly ISoundService? _soundService;
    private ISystemTrayService? _systemTrayService;
    private bool _isExiting;
    private TerminalPairTabViewModel? _subscribedOverlayTab;

    public MainWindow(
        MainViewModel mainViewModel,
        IConfigurationService configService,
        IDialogService dialogService,
        GitBranchViewModel gitBranchViewModel,
        GitFilesViewModel gitFilesViewModel,
        CommitHistoryViewModel commitHistoryViewModel,
        GitStashViewModel gitStashViewModel,
        GitTagsViewModel gitTagsViewModel,
        ScratchPadViewModel scratchPadViewModel,
        FileViewerViewModel fileViewerViewModel,
        DetectedLinksViewModel detectedLinksViewModel,
        TaskPanelViewModel taskPanelViewModel,
        ClaudeTasksPanelViewModel claudeTasksPanelViewModel,
        SearchAcrossFilesViewModel searchAcrossFilesViewModel,
        FileHistoryViewModel fileHistoryViewModel,
        FileBlameViewModel fileBlameViewModel,
        ReflogViewModel reflogViewModel,
        ManageWorktreesViewModel manageWorktreesViewModel,
        WorkspaceSidebarViewModel workspaceSidebarViewModel,
        PrReviewViewModel prReviewViewModel,
        RecentFeaturesViewModel recentFeaturesViewModel,
        BranchComparisonViewModel branchComparisonViewModel,
        MergeConflictViewModel mergeConflictViewModel,
        UnifiedGitPanelViewModel unifiedGitPanelViewModel,
        MarkdownPreviewViewModel markdownPreviewViewModel,
        IFilePickerService filePickerService,
        StatusOverlayService statusOverlayService,
        IToastService toastService,
        ISoundService? soundService = null)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _configService = configService;
        _toastService = toastService;
        _dialogService = dialogService;
        _gitBranchViewModel = gitBranchViewModel;
        _gitFilesViewModel = gitFilesViewModel;
        _commitHistoryViewModel = commitHistoryViewModel;
        _gitStashViewModel = gitStashViewModel;
        _gitTagsViewModel = gitTagsViewModel;
        _scratchPadViewModel = scratchPadViewModel;
        _fileViewerViewModel = fileViewerViewModel;
        _detectedLinksViewModel = detectedLinksViewModel;
        _taskPanelViewModel = taskPanelViewModel;
        _claudeTasksPanelViewModel = claudeTasksPanelViewModel;
        _searchAcrossFilesViewModel = searchAcrossFilesViewModel;
        _fileHistoryViewModel = fileHistoryViewModel;
        _fileBlameViewModel = fileBlameViewModel;
        _reflogViewModel = reflogViewModel;
        _manageWorktreesViewModel = manageWorktreesViewModel;
        _workspaceSidebarViewModel = workspaceSidebarViewModel;
        _prReviewViewModel = prReviewViewModel;
        _recentFeaturesViewModel = recentFeaturesViewModel;
        _branchComparisonViewModel = branchComparisonViewModel;
        _mergeConflictViewModel = mergeConflictViewModel;
        _unifiedGitPanelViewModel = unifiedGitPanelViewModel;
        _markdownPreviewViewModel = markdownPreviewViewModel;
        _filePickerService = filePickerService;
        _statusOverlayService = statusOverlayService;
        _soundService = soundService;

        // Wire up sidebar view model bidirectional reference
        _mainViewModel.SidebarViewModel = _workspaceSidebarViewModel;
        _workspaceSidebarViewModel.MainViewModel = _mainViewModel;

        // Wire up Claude Tasks panel to MainViewModel
        _mainViewModel.ClaudeTasksPanelViewModel = _claudeTasksPanelViewModel;

        DataContext = _mainViewModel;

        // Set sidebar DataContext
        WorkspaceSidebar.DataContext = _workspaceSidebarViewModel;

        // Set popup DataContexts
        // Git views (Branch, Files, History, Stash, Tags, BranchComparison) now render
        // inline via UnifiedGitPanel center panel DataTemplates
        ScratchPadPopup.DataContext = _scratchPadViewModel;
        FileViewerPopup.DataContext = _fileViewerViewModel;
        DetectedLinksPopup.DataContext = _detectedLinksViewModel;
        TaskPanelPopup.DataContext = _taskPanelViewModel;
        ClaudeTasksPanelPopup.DataContext = _claudeTasksPanelViewModel;
        SearchAcrossFilesPopup.DataContext = _searchAcrossFilesViewModel;
        FileHistoryPopup.DataContext = _fileHistoryViewModel;
        FileBlamePopup.DataContext = _fileBlameViewModel;
        ReflogPopup.DataContext = _reflogViewModel;
        ManageWorktreesPopup.DataContext = _manageWorktreesViewModel;
        PrReviewPopup.DataContext = _prReviewViewModel;
        RecentFeaturesPopup.DataContext = _recentFeaturesViewModel;
        MergeConflictPopup.DataContext = _mergeConflictViewModel;

        // Wire up MainViewModel events
        // Note: ScratchPadViewModel and TaskPanelViewModel subscribe to their events internally
        _mainViewModel.GitChangesRequested += OnGitChangesRequested;
        _mainViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _mainViewModel.FilePopOutRequested += OnFilePopOutRequested;
        _mainViewModel.SetupRequested += OnSetupRequested;
        _mainViewModel.FileHistoryRequested += OnFileHistoryRequested;
        _mainViewModel.FileBlameRequested += OnFileBlameRequested;
        _mainViewModel.PrReviewRequested += OnPrReviewRequested;
        _mainViewModel.DashboardPrReviewRequested += OnDashboardPrReviewRequested;
        _mainViewModel.RunTerminalRequested += OnRunTerminalRequested;
        _mainViewModel.CenterPanelRestoreRequested += OnCenterPanelRestoreRequested;
        _mainViewModel.AiPanelCommandRequested += OnAiPanelCommandRequested;

        // Subscribe to view model property changes for tab-switch rebinding
        _mainViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Wire up GitFilesViewModel events for file preview/edit from Git Changes popup
        _gitFilesViewModel.FilePreviewRequested += OnGitFilesFilePreviewRequested;
        _gitFilesViewModel.FileEditRequested += OnGitFilesFileEditRequested;
        _gitFilesViewModel.MergeConflictRequested += OnMergeConflictRequested;

        // Wire up detected links file preview event
        _detectedLinksViewModel.FilePreviewRequested += OnDetectedLinksFilePreviewRequested;

        // Wire up file viewer detach event
        _fileViewerViewModel.DetachRequested += OnFileViewerDetachRequested;

        // Wire up search across files events
        _searchAcrossFilesViewModel.FilePreviewRequested += OnSearchFilePreviewRequested;
        _searchAcrossFilesViewModel.FileEditRequested += OnSearchFileEditRequested;

        // Wire up manage worktrees events
        _manageWorktreesViewModel.OpenWorktreeRequested += OnOpenWorktreeRequested;

        // Wire up workspace sidebar events
        _workspaceSidebarViewModel.ManageWorktreesRequested += OnManageWorktreesRequested;

        // Event handlers
        Opened += OnOpened;
        Closing += OnClosing;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;

    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Set up macOS native menu
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetupMacOSMenu();
        }
    }

    private void SetupMacOSMenu()
    {
        var menu = NativeMenu.GetMenu(this);
        if (menu == null)
        {
            menu = new NativeMenu();
            NativeMenu.SetMenu(this, menu);
        }

        // File menu
        var fileMenu = new NativeMenuItem("File") { Menu = new NativeMenu() };

        var newProjectItem = new NativeMenuItem("New Project...")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta)
        };
        newProjectItem.Click += (_, _) => _mainViewModel.OpenNewProjectCommand.Execute(null);
        fileMenu.Menu.Add(newProjectItem);

        fileMenu.Menu.Add(new NativeMenuItemSeparator());

        var closeTabItem = new NativeMenuItem("Close Tab")
        {
            Gesture = new KeyGesture(Key.W, KeyModifiers.Meta)
        };
        closeTabItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab != null)
                _mainViewModel.CloseTabCommand.Execute(_mainViewModel.SelectedTab);
        };
        fileMenu.Menu.Add(closeTabItem);

        menu.Add(fileMenu);

        // Edit menu
        var editMenu = new NativeMenuItem("Edit") { Menu = new NativeMenu() };
        editMenu.Menu.Add(new NativeMenuItem("Copy")
        {
            Gesture = new KeyGesture(Key.C, KeyModifiers.Meta)
        });
        editMenu.Menu.Add(new NativeMenuItem("Paste")
        {
            Gesture = new KeyGesture(Key.V, KeyModifiers.Meta)
        });
        editMenu.Menu.Add(new NativeMenuItemSeparator());
        editMenu.Menu.Add(new NativeMenuItem("Select All")
        {
            Gesture = new KeyGesture(Key.A, KeyModifiers.Meta)
        });
        menu.Add(editMenu);

        // View menu
        var viewMenu = new NativeMenuItem("View") { Menu = new NativeMenu() };

        var settingsItem = new NativeMenuItem("Settings...")
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta)
        };
        settingsItem.Click += (_, _) => _mainViewModel.OpenSettingsCommand.Execute(null);
        viewMenu.Menu.Add(settingsItem);

        var statisticsItem = new NativeMenuItem("Statistics");
        statisticsItem.Click += (_, _) => _mainViewModel.OpenStatisticsCommand.Execute(null);
        viewMenu.Menu.Add(statisticsItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var commandPaletteItem = new NativeMenuItem("Command Palette...")
        {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        commandPaletteItem.Click += (_, _) => _mainViewModel.IsCommandPaletteOpen = true;
        viewMenu.Menu.Add(commandPaletteItem);

        var tabSwitcherItem = new NativeMenuItem("Tab Switcher...")
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        tabSwitcherItem.Click += (_, _) => _mainViewModel.IsTabSwitcherOpen = true;
        viewMenu.Menu.Add(tabSwitcherItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var gitBranchItem = new NativeMenuItem("Git Branches...")
        {
            Gesture = new KeyGesture(Key.B, KeyModifiers.Meta)
        };
        gitBranchItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.Branches);
            }
        };
        viewMenu.Menu.Add(gitBranchItem);

        var gitChangesItem = new NativeMenuItem("Git Changes...")
        {
            Gesture = new KeyGesture(Key.G, KeyModifiers.Meta)
        };
        gitChangesItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.Changes);
            }
        };
        viewMenu.Menu.Add(gitChangesItem);

        var commitHistoryItem = new NativeMenuItem("Commit History...")
        {
            Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        commitHistoryItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.History);
            }
        };
        viewMenu.Menu.Add(commitHistoryItem);

        var gitStashItem = new NativeMenuItem("Git Stash...")
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        gitStashItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.Stash);
            }
        };
        viewMenu.Menu.Add(gitStashItem);

        var gitReflogItem = new NativeMenuItem("Git Reflog...")
        {
            Gesture = new KeyGesture(Key.G, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        gitReflogItem.Click += (_, _) => _ = _reflogViewModel.OpenCommand.ExecuteAsync(null);
        viewMenu.Menu.Add(gitReflogItem);

        var prReviewItem = new NativeMenuItem("PR Review...")
        {
            Gesture = new KeyGesture(Key.R, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        prReviewItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
                _ = _prReviewViewModel.OpenAsync(terminalTab.WorkingDirectory);
        };
        viewMenu.Menu.Add(prReviewItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var searchFilesItem = new NativeMenuItem("Search in Files...")
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Meta)
        };
        searchFilesItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
                _searchAcrossFilesViewModel.OpenCommand.Execute(terminalTab);
        };
        viewMenu.Menu.Add(searchFilesItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var scratchPadItem = new NativeMenuItem("Scratch Pad")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        scratchPadItem.Click += (_, _) => _scratchPadViewModel.Open();
        viewMenu.Menu.Add(scratchPadItem);

        var taskPanelItem = new NativeMenuItem("Task Panel")
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Meta)
        };
        taskPanelItem.Click += (_, _) => _taskPanelViewModel.Open();
        viewMenu.Menu.Add(taskPanelItem);

        var timelineItem = new NativeMenuItem("Timeline Mode")
        {
            Gesture = new KeyGesture(Key.I, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        timelineItem.Click += (_, _) => _mainViewModel.OpenTimelineCommand.Execute(null);
        viewMenu.Menu.Add(timelineItem);

        var statusOverlayItem = new NativeMenuItem("Toggle Status Overlay")
        {
            Gesture = new KeyGesture(Key.Y, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        statusOverlayItem.Click += (_, _) => _statusOverlayService.Toggle();
        viewMenu.Menu.Add(statusOverlayItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var toggleFullScreenItem = new NativeMenuItem("Toggle Full Screen")
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control)
        };
        toggleFullScreenItem.Click += (_, _) =>
        {
            WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        };
        viewMenu.Menu.Add(toggleFullScreenItem);

        menu.Add(viewMenu);

        // Window menu
        var windowMenu = new NativeMenuItem("Window") { Menu = new NativeMenu() };

        var minimizeItem = new NativeMenuItem("Minimize")
        {
            Gesture = new KeyGesture(Key.M, KeyModifiers.Meta)
        };
        minimizeItem.Click += (_, _) => WindowState = WindowState.Minimized;
        windowMenu.Menu.Add(minimizeItem);

        var nextTabItem = new NativeMenuItem("Next Tab")
        {
            Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control)
        };
        nextTabItem.Click += (_, _) => _mainViewModel.CycleTabCommand.Execute(true);
        windowMenu.Menu.Add(nextTabItem);

        var prevTabItem = new NativeMenuItem("Previous Tab")
        {
            Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift)
        };
        prevTabItem.Click += (_, _) => _mainViewModel.CycleTabCommand.Execute(false);
        windowMenu.Menu.Add(prevTabItem);

        menu.Add(windowMenu);

        // Help menu
        var helpMenu = new NativeMenuItem("Help") { Menu = new NativeMenu() };

        var keyboardShortcutsItem = new NativeMenuItem("Keyboard Shortcuts")
        {
            Gesture = new KeyGesture(Key.F1)
        };
        keyboardShortcutsItem.Click += (_, _) => _mainViewModel.IsHelpOpen = true;
        helpMenu.Menu.Add(keyboardShortcutsItem);

        var whatsNewItem = new NativeMenuItem("What's New")
        {
            Gesture = new KeyGesture(Key.F1, KeyModifiers.Meta)
        };
        whatsNewItem.Click += (_, _) => _recentFeaturesViewModel.OnOpened();
        helpMenu.Menu.Add(whatsNewItem);

        menu.Add(helpMenu);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _mainViewModel.Initialize();
        _statusOverlayService.Initialize(this);

        // Resolve system tray service for minimize-to-tray / close-to-tray behavior
        _systemTrayService = App.Current.Services.GetService<ISystemTrayService>();

        // Update system tray when config is saved via Settings UI
        _mainViewModel.ConfigReloaded += (_, _) =>
        {
            if (_systemTrayService != null)
            {
                var config = _configService.Load();
                _systemTrayService.IsEnabled = config.Settings.ShowInSystemTray;
            }
        };

        // Initialize toast overlay window (shown on demand when toasts appear)
        var toastWindow = new ToastWindow();
        toastWindow.Initialize(this, _toastService);

        // Subscribe to all existing terminal tabs for overlay aggregation
        foreach (var tab in _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>())
            tab.PropertyChanged += OnAnyTerminalTabPropertyChanged;
        _mainViewModel.Tabs.CollectionChanged += OnTabsCollectionChangedForOverlay;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _soundService?.SetAppFocused(true);
        _statusOverlayService.OnMainWindowActivated();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _soundService?.SetAppFocused(false);
        _statusOverlayService.OnMainWindowDeactivated();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var config = _configService.Load();

        // If tray is enabled and not explicitly exiting, minimize to tray instead of closing
        if (_systemTrayService?.IsEnabled == true && !_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Check if we need to confirm close
        if (config.Settings.ConfirmOnClose)
        {
            // Check if any terminals are still running
            var hasRunningTerminals = _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>()
                .Any(t => t.Pair.CustomTerminal.IsProcessRunning() || t.Pair.ShellTerminal.IsProcessRunning());

            if (!hasRunningTerminals)
            {
                hasRunningTerminals = _mainViewModel.Tabs.OfType<ProfileTerminalTabViewModel>()
                    .Any(t => t.Session.IsProcessRunning());
            }

            if (hasRunningTerminals)
            {
                if (!_dialogService.ShowConfirmation(
                    "There are still terminals running. Are you sure you want to close?",
                    "Confirm Close"))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        // Save window state
        config.WindowState = new WindowStateInfo
        {
            Left = Position.X,
            Top = Position.Y,
            Width = (int)Width,
            Height = (int)Height,
            IsMaximized = WindowState == WindowState.Maximized
        };
        _configService.Save(config);

        // Shutdown status overlay
        _statusOverlayService.Shutdown();

        // Shutdown view model
        _mainViewModel.Shutdown();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Update Claude Tasks panel workspace when selected tab changes
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            if (_claudeTasksPanelViewModel.IsOpen)
            {
                // Always update workspace path so it's correct when toggling to "Current Workspace"
                var workspacePath = (_mainViewModel.SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory;
                _claudeTasksPanelViewModel.SetWorkspace(workspacePath);

                // Only refresh if filtering by current workspace (global shows all anyway)
                if (!_claudeTasksPanelViewModel.ShowGlobalTasks)
                {
                    _claudeTasksPanelViewModel.OnOpened();
                }
            }

            // Subscribe to terminal activity changes on the new tab for status overlay
            SubscribeOverlayTab(_mainViewModel.SelectedTab as TerminalPairTabViewModel);
            UpdateStatusOverlay();

            // Rebind center panel data when switching to a tab that has one.
            // Singleton panel VMs only hold data for one tab at a time, so we
            // must reload when the user switches to a different tab.
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel newTab &&
                newTab.ActiveCenterPanel != null)
            {
                if (newTab.ActiveCenterPanel == _unifiedGitPanelViewModel)
                    _ = _unifiedGitPanelViewModel.OpenOnTabAsync(newTab, _unifiedGitPanelViewModel.ActiveTab);
                // Note: Other center panel types (branchComparison, searchFiles, prReview, etc.)
                // are not yet implemented as IPanelableViewModel in Avalonia. Add rebinding here
                // as they are migrated to inherit from BasePanelViewModel.
            }
        }
    }

    #region Status Overlay

    private void SubscribeOverlayTab(TerminalPairTabViewModel? tab)
    {
        if (_subscribedOverlayTab != null)
        {
            _subscribedOverlayTab.PropertyChanged -= OnOverlayTabPropertyChanged;
            _subscribedOverlayTab = null;
        }

        if (tab != null)
        {
            _subscribedOverlayTab = tab;
            tab.PropertyChanged += OnOverlayTabPropertyChanged;
        }
    }

    private void OnOverlayTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TerminalPairTabViewModel.IsAnyTerminalActive) or
            nameof(TerminalPairTabViewModel.IsWaitingForInput) or
            nameof(TerminalPairTabViewModel.HasUnreadActivity))
        {
            UpdateStatusOverlay();
        }
    }

    private void UpdateStatusOverlay()
    {
        if (_statusOverlayService.OverlayCount == 0) return;

        var terminalTabs = _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>().ToList();
        var aiName = _configService.Load().Settings.CustomCommandName;

        // Priority: any waiting > any active > any completed > all idle
        var waitingTab = terminalTabs.FirstOrDefault(t => t.IsWaitingForInput);
        if (waitingTab != null)
        {
            var project = System.IO.Path.GetFileName(waitingTab.Pair.WorkingDirectory);
            _statusOverlayService.UpdateState("waiting", $"{project} — waiting for input");
            return;
        }

        var activeTabs = terminalTabs.Where(t => t.IsAnyTerminalActive).ToList();
        if (activeTabs.Count > 0)
        {
            if (activeTabs.Count == 1)
            {
                var project = System.IO.Path.GetFileName(activeTabs[0].Pair.WorkingDirectory);
                _statusOverlayService.UpdateState("active", $"{project} — {aiName} working");
            }
            else
            {
                _statusOverlayService.UpdateState("active", $"{activeTabs.Count} workspaces active");
            }
            return;
        }

        var completedTab = terminalTabs.FirstOrDefault(t => t.HasUnreadActivity);
        if (completedTab != null)
        {
            var project = System.IO.Path.GetFileName(completedTab.Pair.WorkingDirectory);
            _statusOverlayService.UpdateState("completed", $"{project} — task completed");
            return;
        }

        // All idle - show selected tab name or generic
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel selectedTerminal)
        {
            var project = System.IO.Path.GetFileName(selectedTerminal.Pair.WorkingDirectory);
            _statusOverlayService.UpdateState("idle", $"{project} — idle");
        }
        else
        {
            _statusOverlayService.UpdateState("idle", "Idle");
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
            foreach (var tab in e.NewItems.OfType<TerminalPairTabViewModel>())
                tab.PropertyChanged += OnAnyTerminalTabPropertyChanged;
        if (e.OldItems != null)
            foreach (var tab in e.OldItems.OfType<TerminalPairTabViewModel>())
                tab.PropertyChanged -= OnAnyTerminalTabPropertyChanged;
        UpdateStatusOverlay();
    }

    #endregion

    #region Popup Event Handlers

    private async void OnCenterPanelRestoreRequested(object? sender, CenterPanelRestoreEventArgs e)
    {
        // When SkipDataLoad is true (non-selected tabs during startup), skip async data loading
        // to avoid race conditions with singleton panel ViewModels. Data loads on demand
        // when the user switches to the tab (via tab-switch rebinding in OnViewModelPropertyChanged).
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
                    e.Tab.ShowCenterPanel(_unifiedGitPanelViewModel);
                }
                else
                {
                    await _unifiedGitPanelViewModel.OpenOnTabAsync(e.Tab, gitTab);
                    e.Tab.ShowCenterPanel(_unifiedGitPanelViewModel);
                }
                break;
            // Note: Other center panel types (branchComparison, searchFiles, prReview, etc.)
            // are not yet implemented as IPanelableViewModel in Avalonia. Add cases here as
            // they are migrated to inherit from BasePanelViewModel.
        }
    }

    private async void OnGitChangesRequested(object? sender, EventArgs e)
    {
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
            await _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.Changes);
        }
    }

    private void OnDetectedLinksFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            var mode = e.OpenInEditMode ? FileViewerMode.Edit : FileViewerMode.Preview;
            _fileViewerViewModel.Open(e.FilePath, mode, e.Line > 0 ? e.Line : null);
        }
    }

    private void OnGitFilesFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Preview);
        }
    }

    private void OnGitFilesFileEditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Edit);
        }
    }

    private async void OnMergeConflictRequested(object? sender, EventArgs e)
    {
        var currentTab = _mainViewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null) return;

        await _mergeConflictViewModel.OpenAsync(currentTab);
    }

    private void OnSearchFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _searchAcrossFilesViewModel.CloseCommand.Execute(null);
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Preview, e.Line);
        }
    }

    private void OnSearchFileEditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _searchAcrossFilesViewModel.CloseCommand.Execute(null);
            // LineNumber is not in Core FileEditRequestedEventArgs - use null as default
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Edit, goToLine: null);
        }
    }

    private void OnFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.FilePath))
        {
            // Open file picker if no path provided
            _ = OpenFilePickerAsync(e.OpenInEditMode);
            return;
        }

        var mode = e.OpenInEditMode ? FileViewerMode.Edit : FileViewerMode.Preview;
        _fileViewerViewModel.Open(e.FilePath, mode, e.Line > 0 ? e.Line : null);
    }

    private void OnFilePopOutRequested(object? sender, FileViewerRequestedEventArgs e)
    {
        // Create a new FileViewerWindow for pop-out
        CreatePopOutWindow(e.FilePath, e.Mode == FileViewerMode.Edit);
    }

    private void OnFileHistoryRequested(object? sender, FileHistoryRequestedEventArgs e)
    {
        _ = _fileHistoryViewModel.OpenAsync(e.WorkingDirectory, e.FilePath);
    }

    private void OnFileBlameRequested(object? sender, FileBlameRequestedEventArgs e)
    {
        _ = _fileBlameViewModel.OpenAsync(e.WorkingDirectory, e.FilePath);
    }

    private void OnFileViewerDetachRequested(object? sender, EventArgs e)
    {
        // Pop out the current file from the popup viewer
        if (!string.IsNullOrEmpty(_fileViewerViewModel.FilePath))
        {
            var isEditMode = _fileViewerViewModel.IsEditModeSelected;
            CreatePopOutWindow(_fileViewerViewModel.FilePath, isEditMode);
            _fileViewerViewModel.Close();
        }
    }

    private void CreatePopOutWindow(string filePath, bool editMode)
    {
        // TODO: Create FileViewerWindow when implemented for Avalonia
        // For now, just open in default app
        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                // Silently fail
            }
        }
    }

    private void OnSetupRequested(object? sender, EventArgs e)
    {
        // Create and show SetupWindow
        var services = App.Current.Services;
        var setupViewModel = new SetupViewModel(
            services.GetService<IProcessService>());

        var clipboardService = services.GetRequiredService<IClipboardService>();
        var timerService = services.GetRequiredService<Services.ITimerService>();

        var setupWindow = new SetupWindow(setupViewModel, clipboardService, timerService, isStartupMode: false);
        setupWindow.Show(this);
    }

    private async void OnPrReviewRequested(object? sender, EventArgs e)
    {
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            await _prReviewViewModel.OpenAsync(terminalTab.WorkingDirectory);
        }
    }

    private async void OnDashboardPrReviewRequested(object? sender, PrReviewRequestedEventArgs e)
    {
        // Find the tab for this working directory (OpenProjectTab was already called by DashboardTabViewModel)
        var tab = _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>()
            .FirstOrDefault(t => string.Equals(t.WorkingDirectory, e.WorkingDirectory, StringComparison.OrdinalIgnoreCase))
            ?? _mainViewModel.SelectedTab as TerminalPairTabViewModel;

        if (tab == null) return;

        // Switch to the project tab so the PR review popup appears in the right context
        _mainViewModel.SelectedTab = tab;

        await _prReviewViewModel.OpenForPrAsync(e.WorkingDirectory, e.PullRequest);
    }

    private void OnOpenWorktreeRequested(object? sender, string worktreePath)
    {
        // Open or focus the worktree as a new tab
        _mainViewModel.OpenProjectTab(worktreePath);
    }

    private async void OnManageWorktreesRequested(object? sender, EventArgs e)
    {
        CloseAllPopups();
        await _manageWorktreesViewModel.OpenAsync();
    }

    private async void OnRunTerminalRequested(object? sender, RunTerminalRequestedEventArgs e)
    {
        var tab = e.Tab;

        if (e.IsStop)
        {
            // Send Ctrl+C to stop the running process
            if (tab.Pair.RunTerminal != null)
            {
                tab.Pair.RunTerminal.SendText("\x03", appendNewline: false); // Ctrl+C
                tab.OnRunStopped();
            }
            return;
        }

        // Initialize the run terminal if not already done
        if (tab.Pair.RunTerminal == null)
        {
            await tab.InitializeRunTerminalAsync();
        }

        // Send the run command to the terminal
        if (tab.Pair.RunTerminal != null)
        {
            var command = e.Configuration.Command;
            if (!string.IsNullOrWhiteSpace(command))
            {
                tab.Pair.RunTerminal.SendText(command, appendNewline: true);
                tab.OnRunStarted();
            }
        }
    }

    private async Task OpenFilePickerAsync(bool editMode)
    {
        try
        {
            var initialDir = (_mainViewModel.SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory;
            var filePath = await _filePickerService.PickFileAsync(
                title: "Select File",
                filters: null,
                initialDirectory: initialDir);

            if (!string.IsNullOrEmpty(filePath))
            {
                var mode = editMode ? FileViewerMode.Edit : FileViewerMode.Preview;
                _fileViewerViewModel.Open(filePath, mode);
            }
        }
        catch
        {
            // Silently fail if file picker fails
        }
    }

    #endregion

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Get the platform-appropriate modifier (Meta on macOS, Control otherwise)
        var primaryModifier = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? KeyModifiers.Meta
            : KeyModifiers.Control;

        // Handle Cmd/Ctrl+F1 for What's New / Recent Features
        if (e.Key == Key.F1 && e.KeyModifiers == primaryModifier)
        {
            if (_recentFeaturesViewModel.IsOpen)
                _recentFeaturesViewModel.CloseCommand.Execute(null);
            else
                _recentFeaturesViewModel.OnOpened();
            e.Handled = true;
            return;
        }

        // Handle F1 for help
        if (e.Key == Key.F1)
        {
            _mainViewModel.IsHelpOpen = !_mainViewModel.IsHelpOpen;
            e.Handled = true;
            return;
        }

        // Handle F5 for Run Start
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel tab && tab.CanRun)
            {
                tab.StartRunCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // Handle Shift+F5 for Run Stop
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.Shift)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel tab && tab.CanStop)
            {
                tab.StopRunCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // Handle F4 for Voice Commands toggle
        if (e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.None)
        {
            _mainViewModel.ToggleVoiceListening();
            e.Handled = true;
            return;
        }

        // Handle Escape - priority-based cascade (close one thing at a time)
        if (e.Key == Key.Escape)
        {
            // Let popup views handle their own Escape key
            if (_mainViewModel.IsCommandPaletteOpen || _mainViewModel.IsTabSwitcherOpen)
                return;

            // First priority: close active center panel (return to terminals)
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel escTerminalTab && escTerminalTab.ActiveCenterPanel != null)
            {
                escTerminalTab.CloseCenterPanel();
                e.Handled = true;
                return;
            }

            // Then close individual popups in priority order
            // Check all popup ViewModels that have IsOpen
            var openPopups = new (bool isOpen, Action close)[]
            {
                (_prReviewViewModel.IsOpen, () => _prReviewViewModel.IsOpen = false),
                (_mergeConflictViewModel.IsOpen, () => _mergeConflictViewModel.IsOpen = false),
                (_unifiedGitPanelViewModel.IsOpen, () => _unifiedGitPanelViewModel.CloseCommand.Execute(null)),
                (_searchAcrossFilesViewModel.IsOpen, () => _searchAcrossFilesViewModel.CloseCommand.Execute(null)),
                (_commitHistoryViewModel.IsOpen, () => _commitHistoryViewModel.IsOpen = false),
                (_gitStashViewModel.IsOpen, () => _gitStashViewModel.IsOpen = false),
                (_gitTagsViewModel.IsOpen, () => _gitTagsViewModel.IsOpen = false),
                (_branchComparisonViewModel.IsOpen, () => _branchComparisonViewModel.IsOpen = false),
                (_gitBranchViewModel.IsOpen, () => _gitBranchViewModel.IsOpen = false),
                (_gitFilesViewModel.IsOpen, () => _gitFilesViewModel.CloseCommand.Execute(null)),
                (_fileHistoryViewModel.IsOpen, () => _fileHistoryViewModel.IsOpen = false),
                (_fileBlameViewModel.IsOpen, () => _fileBlameViewModel.IsOpen = false),
                (_reflogViewModel.IsOpen, () => _reflogViewModel.IsOpen = false),
                (_manageWorktreesViewModel.IsOpen, () => _manageWorktreesViewModel.IsOpen = false),
                (_recentFeaturesViewModel.IsOpen, () => _recentFeaturesViewModel.CloseCommand.Execute(null)),
                (_taskPanelViewModel.IsOpen, () => _taskPanelViewModel.IsOpen = false),
                (_scratchPadViewModel.IsOpen, () => _scratchPadViewModel.CloseCommand.Execute(null)),
                (_detectedLinksViewModel.IsOpen, () => _detectedLinksViewModel.CloseCommand.Execute(null)),
                (_fileViewerViewModel.IsOpen, () => _fileViewerViewModel.CloseCommand.Execute(null)),
                (_claudeTasksPanelViewModel.IsOpen, () => _claudeTasksPanelViewModel.CloseCommand.Execute(null)),
                (_mainViewModel.IsHelpOpen, () => _mainViewModel.IsHelpOpen = false),
            };

            foreach (var (isOpen, close) in openPopups)
            {
                if (isOpen)
                {
                    close();
                    e.Handled = true;
                    return;
                }
            }

            // If nothing was open, don't consume the key event
            return;
        }

        // Handle Cmd/Ctrl+N for new project
        if (e.Key == Key.N && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.OpenNewProjectCommand.CanExecute(null))
                _mainViewModel.OpenNewProjectCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+W for close tab
        if (e.Key == Key.W && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab != null && _mainViewModel.CloseTabCommand.CanExecute(_mainViewModel.SelectedTab))
                _mainViewModel.CloseTabCommand.Execute(_mainViewModel.SelectedTab);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+, for settings
        if (e.Key == Key.OemComma && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.OpenSettingsCommand.CanExecute(null))
                _mainViewModel.OpenSettingsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+P for profiles
        if (e.Key == Key.P && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.OpenProfilesCommand.CanExecute(null))
                _mainViewModel.OpenProfilesCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+E for Open in Finder/Explorer
        if (e.Key == Key.E && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.OpenInExplorerCommand.CanExecute(null))
                _mainViewModel.OpenInExplorerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+` for terminal switching (may not work on all keyboard layouts)
        // Try multiple key codes for backtick: OemTilde, Oem3 (varies by keyboard layout)
        if ((e.Key == Key.OemTilde || e.Key == Key.Oem3) && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SwitchActiveTerminalCommand.CanExecute(null))
                _mainViewModel.SwitchActiveTerminalCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+P for command palette
        if (e.Key == Key.P && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.IsCommandPaletteOpen = !_mainViewModel.IsCommandPaletteOpen;
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+T for tab switcher
        if (e.Key == Key.T && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.IsTabSwitcherOpen = !_mainViewModel.IsTabSwitcherOpen;
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+B for Git Branches (opens unified panel on Branches tab)
        if (e.Key == Key.B && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.Branches);
            }
            e.Handled = true;
            return;
        }

        // Handle Alt+G for Git Changes (opens unified panel on Changes tab)
        if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Alt)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.Changes);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+H for Commit History (opens unified panel on History tab)
        if (e.Key == Key.H && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.History);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+S for Git Stash (opens unified panel on Stash tab)
        if (e.Key == Key.S && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(terminalTab, GitPanelTab.Stash);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+G for Git Reflog
        if (e.Key == Key.G && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _ = _reflogViewModel.OpenCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+R for PR Review
        if (e.Key == Key.R && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                _ = _prReviewViewModel.OpenAsync(terminalTab.WorkingDirectory);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+L for Layout Mode Toggle
        if (e.Key == Key.L && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.ToggleLayoutModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+I for Timeline Mode
        if (e.Key == Key.I && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.OpenTimelineCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+J for Spark Canvas
        if (e.Key == Key.J && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.OpenSparkCanvasCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+N for Scratch Pad
        if (e.Key == Key.N && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _scratchPadViewModel.Open();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+T for Task Panel
        if (e.Key == Key.T && e.KeyModifiers == primaryModifier)
        {
            _taskPanelViewModel.Open();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+K for Claude Tasks Panel
        if (e.Key == Key.K && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _claudeTasksPanelViewModel.Open();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+O for File Preview
        if (e.Key == Key.O && e.KeyModifiers == primaryModifier)
        {
            // Open file picker for preview
            _ = OpenFilePickerAsync(editMode: false);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+E for File Edit
        if (e.Key == Key.E && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            // Open file picker for edit
            _ = OpenFilePickerAsync(editMode: true);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+Y for Status Overlay toggle
        if (e.Key == Key.Y && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _statusOverlayService.Toggle();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+F for File Explorer toggle
        if (e.Key == Key.F && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.IsExplorerVisible = !terminalTab.IsExplorerVisible;
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+F for Search Across Files
        if (e.Key == Key.F && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                _searchAcrossFilesViewModel.OpenCommand.Execute(terminalTab);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+1-9 for tab jumping
        if (e.KeyModifiers == primaryModifier && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < _mainViewModel.Tabs.Count)
            {
                _mainViewModel.SelectedTab = _mainViewModel.Tabs[index];
            }
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Tab for next tab (kept for compatibility, may not work when terminal focused)
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Control)
        {
            if (_mainViewModel.CycleTabCommand.CanExecute(true))
                _mainViewModel.CycleTabCommand.Execute(true);
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Shift+Tab for previous tab
        if (e.Key == Key.Tab && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            if (_mainViewModel.CycleTabCommand.CanExecute(false))
                _mainViewModel.CycleTabCommand.Execute(false);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+H for Commit History (opens unified panel on History tab)
        if (e.Key == Key.H && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel histTab)
            {
                histTab.ShowCenterPanel(_unifiedGitPanelViewModel);
                _ = _unifiedGitPanelViewModel.OpenOnTabAsync(histTab, GitPanelTab.History);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+D for Git Pull
        if (e.Key == Key.D && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel pullTab)
                pullTab.GitPullCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+U for Git Push
        if (e.Key == Key.U && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel pushTab)
                pushTab.GitPushCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+M for Markdown Preview
        if (e.Key == Key.M && e.KeyModifiers == primaryModifier)
        {
            // TODO: wire when MarkdownPreview is connected to MainWindow (event not subscribed yet)
            _mainViewModel.RaiseMarkdownPreviewRequested();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+O for Repository Switcher
        if (e.Key == Key.O && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            // TODO: wire when RepositorySwitcherViewModel is injected into MainWindow
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+V for paste into terminal.
        // Text paste is always handled by us (SendText) since raw Ctrl+V sends 0x16 to the PTY.
        // Image paste differs by environment:
        //   Container: let Ctrl+V pass through -> Claude Code reads clipboard via our shims.
        //   Non-container: send Alt+V escape -> Claude Code reads clipboard directly.
        if (e.Key == Key.V && e.KeyModifiers == primaryModifier)
        {
            var session = (_mainViewModel.SelectedTab as TerminalPairTabViewModel)?.GetFocusedSession()
                ?? (_mainViewModel.SelectedTab as ProfileTerminalTabViewModel)?.Session;

            if (session != null)
            {
                var isContainerized = (_mainViewModel.SelectedTab as TerminalPairTabViewModel)?.IsContainerized ?? false;
                _ = PasteToTerminalAsync(session, isContainerized);
                e.Handled = true;
                return;
            }
        }

        // Handle Cmd/Ctrl+C for copy from terminal (only if there's a selection)
        if (e.Key == Key.C && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel copyTab)
            {
                var session = copyTab.GetFocusedSession();
                if (session != null)
                {
                    _ = session.CopySelectionToClipboardAsync();
                    // Don't consume event unconditionally - let Ctrl+C pass through for SIGINT if no selection
                }
            }
        }

        // Check Quick Command shortcuts
        if (TryExecuteQuickCommandShortcut(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }

        // Check profile launch shortcuts
        if (TryExecuteProfileShortcut(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }

        // Check Claude command shortcuts
        if (TryExecuteClaudeCommandShortcut(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }
    }

    #region Quick Command Shortcuts

    private bool TryExecuteQuickCommandShortcut(Key key, KeyModifiers modifiers)
    {
        foreach (var command in _mainViewModel.QuickCommands)
        {
            if (string.IsNullOrEmpty(command.Shortcut)) continue;

            if (TryParseShortcut(command.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                // On macOS, also accept Meta (Cmd) when the shortcut specifies Control (Ctrl)
                // This allows shortcuts defined as "Ctrl+Shift+X" to work with Cmd+Shift+X on macOS
                var platformModifiers = expectedModifiers;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && expectedModifiers.HasFlag(KeyModifiers.Control))
                {
                    platformModifiers = (expectedModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta;
                }

                if (key == expectedKey && (modifiers == expectedModifiers || modifiers == platformModifiers))
                {
                    _mainViewModel.ExecuteQuickCommandCommand.Execute(command);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryParseShortcut(string shortcut, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;

        var parts = shortcut.Split('+', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        // Parse modifiers and key
        foreach (var part in parts)
        {
            var upperPart = part.ToUpperInvariant();
            switch (upperPart)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "CMD":
                case "META":
                    modifiers |= KeyModifiers.Meta;
                    break;
                case "ALT":
                case "OPT":
                case "OPTION":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= KeyModifiers.Shift;
                    break;
                default:
                    // Try to parse as a Key
                    if (System.Enum.TryParse<Key>(part, ignoreCase: true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else if (part.Length == 1 && char.IsLetter(part[0]))
                    {
                        // Single letter key (A-Z)
                        key = (Key)System.Enum.Parse(typeof(Key), part.ToUpperInvariant());
                    }
                    else if (part.Length == 1 && char.IsDigit(part[0]))
                    {
                        // Number key (0-9) - use D0-D9 for top row
                        key = (Key)System.Enum.Parse(typeof(Key), "D" + part);
                    }
                    break;
            }
        }

        return key != Key.None && modifiers != KeyModifiers.None;
    }

    /// <summary>
    /// Formats a key combination into a shortcut string.
    /// </summary>
    public static string FormatShortcut(Key key, KeyModifiers modifiers)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Cmd");
        if (modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");

        // Convert key to display string
        var keyStr = key.ToString();
        if (keyStr.StartsWith("D") && keyStr.Length == 2 && char.IsDigit(keyStr[1]))
        {
            // D0-D9 -> 0-9
            keyStr = keyStr[1].ToString();
        }
        parts.Add(keyStr);

        return string.Join("+", parts);
    }

    #endregion

    #region Profile and Claude Command Shortcuts

    private bool TryExecuteProfileShortcut(Key key, KeyModifiers modifiers)
    {
        foreach (var profile in _mainViewModel.GetProfiles())
        {
            if (string.IsNullOrEmpty(profile.Shortcut)) continue;

            if (TryParseShortcut(profile.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                // On macOS, also accept Meta (Cmd) when the shortcut specifies Control (Ctrl)
                var platformModifiers = expectedModifiers;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && expectedModifiers.HasFlag(KeyModifiers.Control))
                {
                    platformModifiers = (expectedModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta;
                }

                if (key == expectedKey && (modifiers == expectedModifiers || modifiers == platformModifiers))
                {
                    _mainViewModel.OpenProfileTab(profile);
                    return true;
                }
            }
        }
        return false;
    }

    private bool TryExecuteClaudeCommandShortcut(Key key, KeyModifiers modifiers)
    {
        var claudeCommands = _mainViewModel.GetClaudeCommandsForCurrentProject();

        foreach (var command in claudeCommands)
        {
            if (string.IsNullOrEmpty(command.Shortcut)) continue;

            if (TryParseShortcut(command.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                // On macOS, also accept Meta (Cmd) when the shortcut specifies Control (Ctrl)
                var platformModifiers = expectedModifiers;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && expectedModifiers.HasFlag(KeyModifiers.Control))
                {
                    platformModifiers = (expectedModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta;
                }

                if (key == expectedKey && (modifiers == expectedModifiers || modifiers == platformModifiers))
                {
                    _mainViewModel.ExecuteClaudeCommand(command);
                    return true;
                }
            }
        }
        return false;
    }

    private async Task PasteToTerminalAsync(Domain.TerminalSession session, bool isContainerized = false)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            // Check for image data on the clipboard.
            // Avalonia may not fully support image detection on all platforms,
            // so we check data formats as a best-effort approach.
#pragma warning disable CS0618 // IClipboard APIs may be obsolete in newer Avalonia
            var formats = await clipboard.GetFormatsAsync();
#pragma warning restore CS0618
            var hasImage = formats != null && formats.Any(f =>
                f.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("png", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("bitmap", StringComparison.OrdinalIgnoreCase));

            if (hasImage)
            {
                if (isContainerized)
                {
                    // Container: let Ctrl+V reach Claude Code — it reads the image
                    // via our xclip shim that proxies to the host clipboard API.
                    return;
                }
                // Non-container: send Alt+V escape — Claude Code uses this to read clipboard
                session.SendText("\x1bv", appendNewline: false);
                return;
            }

            // Text paste: always handle ourselves (both container and non-container)
#pragma warning disable CS0618 // IClipboard.GetTextAsync is obsolete in newer Avalonia
            var text = await clipboard.GetTextAsync();
#pragma warning restore CS0618
            if (!string.IsNullOrEmpty(text))
            {
                session.SendText(text, appendNewline: false);
            }
        }
        catch
        {
            // Silently fail if clipboard access fails
        }
    }

    #endregion

    private void CloseAllPopups()
    {
        // Close MainViewModel popups
        _mainViewModel.IsHelpOpen = false;
        _mainViewModel.IsCommandPaletteOpen = false;
        _mainViewModel.IsTabSwitcherOpen = false;
        _mainViewModel.IsTabDropdownOpen = false;
        _mainViewModel.IsQuickTaskOpen = false;
        _mainViewModel.IsQuickNoteOpen = false;

        // Close ViewModel-managed popups
        _gitBranchViewModel.IsOpen = false;
        if (_gitFilesViewModel.CloseCommand.CanExecute(null))
            _gitFilesViewModel.CloseCommand.Execute(null);
        if (_commitHistoryViewModel.CloseCommand.CanExecute(null))
            _commitHistoryViewModel.CloseCommand.Execute(null);
        if (_gitStashViewModel.CloseCommand.CanExecute(null))
            _gitStashViewModel.CloseCommand.Execute(null);
        if (_gitTagsViewModel.CloseCommand.CanExecute(null))
            _gitTagsViewModel.CloseCommand.Execute(null);
        if (_scratchPadViewModel.CloseCommand.CanExecute(null))
            _scratchPadViewModel.CloseCommand.Execute(null);
        _fileViewerViewModel.Close();
        if (_detectedLinksViewModel.CloseCommand.CanExecute(null))
            _detectedLinksViewModel.CloseCommand.Execute(null);
        if (_taskPanelViewModel.CloseCommand.CanExecute(null))
            _taskPanelViewModel.CloseCommand.Execute(null);
        if (_claudeTasksPanelViewModel.CloseCommand.CanExecute(null))
            _claudeTasksPanelViewModel.CloseCommand.Execute(null);
        if (_searchAcrossFilesViewModel.CloseCommand.CanExecute(null))
            _searchAcrossFilesViewModel.CloseCommand.Execute(null);
        if (_fileHistoryViewModel.CloseCommand.CanExecute(null))
            _fileHistoryViewModel.CloseCommand.Execute(null);
        if (_fileBlameViewModel.CloseCommand.CanExecute(null))
            _fileBlameViewModel.CloseCommand.Execute(null);
        if (_reflogViewModel.CloseCommand.CanExecute(null))
            _reflogViewModel.CloseCommand.Execute(null);
        if (_manageWorktreesViewModel.CloseCommand.CanExecute(null))
            _manageWorktreesViewModel.CloseCommand.Execute(null);
        if (_prReviewViewModel.CloseCommand.CanExecute(null))
            _prReviewViewModel.CloseCommand.Execute(null);
        if (_recentFeaturesViewModel.CloseCommand.CanExecute(null))
            _recentFeaturesViewModel.CloseCommand.Execute(null);
        if (_branchComparisonViewModel.CloseCommand.CanExecute(null))
            _branchComparisonViewModel.CloseCommand.Execute(null);
        if (_mergeConflictViewModel.CloseCommand.CanExecute(null))
            _mergeConflictViewModel.CloseCommand.Execute(null);
    }

    public void BringToFront()
    {
        // On macOS, Show() is needed to deminiaturize (restore from dock)
        // Setting WindowState alone doesn't reliably unminimize
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Closes the window bypassing the minimize-to-tray behavior.
    /// Used by the system tray "Exit" menu item.
    /// </summary>
    public void ForceClose()
    {
        _isExiting = true;
        Close();
    }

    private void SidebarSplitter_DragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        // Update the main view model with the new sidebar width
        if (sender is GridSplitter splitter && splitter.Parent is Grid grid)
        {
            // Column 0 is the sidebar
            if (grid.ColumnDefinitions.Count >= 1)
            {
                var sidebarWidth = grid.ColumnDefinitions[0].ActualWidth;
                _mainViewModel.UpdateSidebarWidth(sidebarWidth);
            }
        }
    }

    private void OnAiPanelCommandRequested(object? sender, string action)
    {
        switch (action)
        {
            case "explain-blame":
                _fileBlameViewModel.ExplainBlameLineCommand.Execute(null);
                break;
            case "summarize-file-history":
                _fileHistoryViewModel.SummarizeHistoryCommand.Execute(null);
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
                var dashboard = _mainViewModel.Tabs.OfType<DashboardTabViewModel>().FirstOrDefault();
                dashboard?.AnalyzeCiFailureCommand.Execute(null);
                break;
            case "prioritize-prs":
                var dashboardForPr = _mainViewModel.Tabs.OfType<DashboardTabViewModel>().FirstOrDefault();
                dashboardForPr?.PrioritizePrsCommand.Execute(null);
                break;
            case "improve-markdown":
                _markdownPreviewViewModel.ImproveMarkdownCommand.Execute(null);
                break;
        }
    }
}