using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IProfileRegistry _profileRegistry;
    private readonly ISessionManager _sessionManager;
    private readonly ITerminalControlFactory _terminalFactory;
    private readonly IConfigurationService _configService;
    private readonly IStatisticsService _statisticsService;
    private readonly IGitStatusService _gitStatusService;

    private readonly ILinkDetectionService _linkDetectionService;
    private readonly IProjectDetectionService _projectDetectionService;
    private readonly IRunUrlDetectionService _runUrlDetectionService;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogService;

    private readonly IFilePreviewService _filePreviewService;
    private readonly IClaudeCommandService _claudeCommandService;
    private readonly IAiAssistantService _aiAssistantService;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private readonly ITimerService _timerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly ITimelineService _timelineService;
    private readonly IInputPromptDetectionService _inputPromptDetectionService;
    private readonly ITaskService? _taskService;
    private readonly IVoiceCommandService? _voiceCommandService;
    private readonly IEventAggregatorService? _eventAggregator;
    private readonly IApiServer? _apiServer;
    private readonly IWebhookDeliveryService? _webhookDeliveryService;

    private readonly IAppTimer _gitStatusTimer;
    private readonly IAppTimer _gitAutoFetchTimer;
    private readonly IAppTimer _activityTimer;
    private readonly IAppTimer _linkDetectionTimer;
    private readonly IAppTimer _runUrlDetectionTimer;

    // Focus time tracking for workspace auto-sort
    private DateTime? _tabFocusStartTime;
    private string? _focusedTabDirectory;

    /// <summary>
    /// The link detection service for scanning terminal output for clickable links.
    /// </summary>
    public ILinkDetectionService LinkDetectionService => _linkDetectionService;

    /// <summary>
    /// The run URL detection service for detecting localhost URLs from run output.
    /// </summary>
    public IRunUrlDetectionService RunUrlDetectionService => _runUrlDetectionService;

    /// <summary>
    /// The project detection service for auto-detecting project types.
    /// </summary>
    public IProjectDetectionService ProjectDetectionService => _projectDetectionService;

    /// <summary>
    /// The terminal control factory for creating terminal controls.
    /// </summary>
    public ITerminalControlFactory TerminalFactory => _terminalFactory;

    /// <summary>
    /// The session manager for tracking terminal sessions.
    /// </summary>
    public ISessionManager SessionManager => _sessionManager;

    /// <summary>
    /// The workspace sidebar view model for the sidebar layout mode.
    /// </summary>
    public WorkspaceSidebarViewModel? WorkspaceSidebar { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceSidebarVisible))]
    [NotifyPropertyChangedFor(nameof(IsTabStripVisible))]
    [NotifyPropertyChangedFor(nameof(SidebarColumnWidth))]
    [NotifyPropertyChangedFor(nameof(SidebarSplitterWidth))]
    private AppLayoutMode _layoutMode = AppLayoutMode.Tabs;

    /// <summary>
    /// Width of the sidebar column for binding.
    /// </summary>
    public double SidebarWidth => WorkspaceSidebar?.Width ?? 250;

    /// <summary>
    /// Whether the workspace sidebar should be visible.
    /// </summary>
    public bool IsWorkspaceSidebarVisible => LayoutMode == AppLayoutMode.WorkspaceSidebar && !(WorkspaceSidebar?.IsCollapsed ?? false);

    /// <summary>
    /// Whether the tab strip should be visible.
    /// </summary>
    public bool IsTabStripVisible => LayoutMode == AppLayoutMode.Tabs;

    /// <summary>
    /// Non-project tabs (Settings, Statistics, Dashboard, etc.) for display in sidebar mode.
    /// </summary>
    public IEnumerable<ITabViewModel> NonProjectTabs => Tabs.Where(t => t is not TerminalPairTabViewModel);

    /// <summary>
    /// Whether there are non-project tabs open.
    /// </summary>
    public bool HasNonProjectTabs => NonProjectTabs.Any();

    /// <summary>
    /// Width of the sidebar column for Grid binding.
    /// </summary>
    public System.Windows.GridLength SidebarColumnWidth =>
        IsWorkspaceSidebarVisible ? new System.Windows.GridLength(SidebarWidth) : new System.Windows.GridLength(0);

    /// <summary>
    /// Width of the sidebar splitter for Grid binding.
    /// </summary>
    public System.Windows.GridLength SidebarSplitterWidth =>
        IsWorkspaceSidebarVisible ? new System.Windows.GridLength(4) : new System.Windows.GridLength(0);

    [ObservableProperty]
    private ObservableCollection<ITabViewModel> _tabs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private ITabViewModel? _selectedTab;

    [ObservableProperty]
    private ObservableCollection<QuickCommand> _quickCommands = [];

    [ObservableProperty]
    private string _dropdownSearchText = "";

    private ObservableCollection<ITabViewModel> _filteredDropdownTabs = [];
    public ReadOnlyObservableCollection<ITabViewModel> FilteredDropdownTabs { get; }

    [ObservableProperty]
    private bool _isTabDropdownOpen;

    [ObservableProperty]
    private string _switcherSearchText = "";

    private ObservableCollection<ITabViewModel> _filteredSwitcherTabs = [];
    public ReadOnlyObservableCollection<ITabViewModel> FilteredSwitcherTabs { get; }

    [ObservableProperty]
    private bool _isTabSwitcherOpen;

    [ObservableProperty]
    private bool _isHelpOpen;

    /// <summary>
    /// Whether touch-friendly mode is enabled for larger touch targets and padding.
    /// </summary>
    [ObservableProperty]
    private bool _touchMode;

    /// <summary>
    /// Voice command floating bar ViewModel.
    /// </summary>
    public VoiceBarViewModel VoiceBar { get; }

    // Command Palette Properties
    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private string _paletteSearchText = "";

    private ObservableCollection<PaletteCommand> _allPaletteCommands = []; // Stores all commands
    private ObservableCollection<PaletteCommand> _filteredPaletteCommands = [];
    public ReadOnlyObservableCollection<PaletteCommand> FilteredPaletteCommands { get; }

    [ObservableProperty]
    private PaletteCommand? _selectedPaletteCommand;

    // Help
    public HelpViewModel HelpViewModel { get; }

    public event EventHandler? ConfigReloaded;
    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<RunTerminalRequestedEventArgs>? RunTerminalRequested;
    public event EventHandler<FileHistoryRequestedEventArgs>? FileHistoryRequested;
    public event EventHandler<FileBlameRequestedEventArgs>? FileBlameRequested;

    public string WindowTitle
    {
        get
        {
            if (SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                var gitBranch = terminalTab.GitStatus?.IsGitRepository == true
                    ? $" ({terminalTab.GitStatus.BranchName})"
                    : "";
                return $"{terminalTab.Title}{gitBranch} - TerminalHost";
            }

            // Handle non-project tabs by using their Title
            if (SelectedTab != null)
            {
                return $"{SelectedTab.Title} - TerminalHost";
            }

            return "TerminalHost";
        }
    }

    public MainViewModel(
        IProfileRegistry profileRegistry,
        ISessionManager sessionManager,
        ITerminalControlFactory terminalFactory,
        IConfigurationService configService,
        IStatisticsService statisticsService,
        IGitStatusService gitStatusService,
        ILinkDetectionService linkDetectionService,
        IProjectDetectionService projectDetectionService,
        IRunUrlDetectionService runUrlDetectionService,
        DetectedLinksViewModel detectedLinksViewModel,
        IFileSystem fileSystem,
        IDialogService dialogService,
        IFilePreviewService filePreviewService,
        IClaudeCommandService claudeCommandService,
        IAiAssistantService aiAssistantService,
        IProcessService processService,
        IToastService toastService,
        ITimerService timerService,
        IDispatcherService dispatcherService,
        IFolderPickerService folderPickerService,
        IViewModelFactory viewModelFactory,
        ITimelineService timelineService,
        IInputPromptDetectionService inputPromptDetectionService,
        ITaskService? taskService = null,
        IVoiceCommandService? voiceCommandService = null,
        IEventAggregatorService? eventAggregator = null,
        IApiServer? apiServer = null,
        IWebhookDeliveryService? webhookDeliveryService = null)
    {
        _profileRegistry = profileRegistry;
        _sessionManager = sessionManager;
        _terminalFactory = terminalFactory;
        _configService = configService;
        _statisticsService = statisticsService;
        _gitStatusService = gitStatusService;
        _linkDetectionService = linkDetectionService;
        _projectDetectionService = projectDetectionService;
        _runUrlDetectionService = runUrlDetectionService;
        _detectedLinksViewModel = detectedLinksViewModel;
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _filePreviewService = filePreviewService;
        _claudeCommandService = claudeCommandService;
        _aiAssistantService = aiAssistantService;
        _processService = processService;
        _toastService = toastService;
        _timerService = timerService;
        _dispatcherService = dispatcherService;
        _folderPickerService = folderPickerService;
        _viewModelFactory = viewModelFactory;
        _timelineService = timelineService;
        _inputPromptDetectionService = inputPromptDetectionService;
        _taskService = taskService;
        _voiceCommandService = voiceCommandService;
        _eventAggregator = eventAggregator;
        _apiServer = apiServer;
        _webhookDeliveryService = webhookDeliveryService;

        // Wire up API server state delegates
        if (_apiServer is ApiServer concreteServer)
        {
            concreteServer.SetRepoStateProvider(
                () => BuildRepoList(),
                (index) => BuildRepoDetail(index));
            concreteServer.SetWorkspaceStateProvider(
                () => BuildWorkspaceList());
        }

        // Subscribe to timeline events
        _timelineService.OpenProjectRequested += OnTimelineOpenProjectRequested;

        // Initialize workspace sidebar
        WorkspaceSidebar = _viewModelFactory.CreateWorkspaceSidebar();
        WorkspaceSidebar.OpenTabRequested += OnWorkspaceSidebarOpenTabRequested;
        WorkspaceSidebar.DuplicateTabRequested += OnWorkspaceSidebarDuplicateTabRequested;
        WorkspaceSidebar.CloseTabRequested += OnWorkspaceSidebarCloseTabRequested;
        WorkspaceSidebar.GitStatusRefreshed += OnWorkspaceSidebarGitStatusRefreshed;

        // Initialize voice command bar
        VoiceBar = new VoiceBarViewModel(timerService);
        VoiceBar.SendToAiRequested += OnVoiceSendToAi;
        VoiceBar.StartListeningRequested += (_, _) => _voiceCommandService?.StartListening();
        VoiceBar.StopListeningRequested += (_, _) => _voiceCommandService?.StopListening();
        if (_voiceCommandService is not null)
        {
            _voiceCommandService.CommandRecognized += (_, e) => VoiceBar.OnRecognitionResult(e.Result);
            _voiceCommandService.Error += (_, e) =>
            {
                _toastService.Show(e.Message, ToastType.Error);
                if (e.IsFatal) VoiceBar.Cancel();
            };
        }

        // Initialize help view model
        HelpViewModel = new HelpViewModel(this);

        // Initialize touch mode from config
        TouchMode = configService.Load().Settings.TouchMode;

        // Subscribe to Tabs collection changes for NonProjectTabs updates
        _tabs.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(NonProjectTabs));
            OnPropertyChanged(nameof(HasNonProjectTabs));

            // Publish API events for tab open/close
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<TerminalPairTabViewModel>())
                    PublishApiEvent("repo.opened", data: new { workingDirectory = item.Pair.WorkingDirectory, title = item.Title });
            }
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<TerminalPairTabViewModel>())
                    PublishApiEvent("repo.closed", data: new { workingDirectory = item.Pair.WorkingDirectory, title = item.Title });
            }
        };

        // Subscribe to Claude command changes (dispatch to UI thread since FileSystemWatcher raises events on thread pool)
        _claudeCommandService.CommandsChanged += (_, _) => _dispatcherService.BeginInvoke(FilterPaletteCommands);

        FilteredDropdownTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredDropdownTabs);
        UpdateFilteredDropdownTabs(); // Initial population

        FilteredSwitcherTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredSwitcherTabs);
        UpdateFilteredSwitcherTabs(); // Initial population

        FilteredPaletteCommands = new ReadOnlyObservableCollection<PaletteCommand>(_filteredPaletteCommands);
        InitializeCommandPalette(); // Initialize commands once
        InitializeVoiceGrammar();   // Build voice grammar from palette commands

        // Set up timer for periodic git status refresh (every 5 seconds)
        _gitStatusTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(5), async () => await RefreshSelectedTabGitStatusAsync());

        // Set up timer for git auto-fetch (configurable interval, default 60 seconds)
        var fetchInterval = Math.Max(30, configService.Load().Settings.GitAutoFetchIntervalSeconds);
        _gitAutoFetchTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(fetchInterval), async () => await AutoFetchAllAsync());

        // Set up timer for activity state refresh (every 1 second to detect idle transitions)
        _activityTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(1), RefreshActivityState);

        // Set up timer for link detection refresh (every 3 seconds)
        _linkDetectionTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(3), RefreshDetectedLinks);

        // Set up timer for run URL detection (every 2 seconds, only when running)
        _runUrlDetectionTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(2), RefreshRunUrlDetection);
    }

    partial void OnDropdownSearchTextChanged(string value)
    {
        UpdateFilteredDropdownTabs();
    }

    partial void OnSwitcherSearchTextChanged(string value)
    {
        UpdateFilteredSwitcherTabs();
    }

    partial void OnTabsChanged(ObservableCollection<ITabViewModel> value)
    {
        UpdateFilteredDropdownTabs();
        UpdateFilteredSwitcherTabs();
        OnPropertyChanged(nameof(NonProjectTabs));
        OnPropertyChanged(nameof(HasNonProjectTabs));

        // Subscribe to collection changes for non-project tabs updates
        value.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(NonProjectTabs));
            OnPropertyChanged(nameof(HasNonProjectTabs));
        };
    }

    partial void OnSelectedTabChanged(ITabViewModel? oldValue, ITabViewModel? newValue)
    {
        // Record focus time for the previous tab
        if (_tabFocusStartTime.HasValue && !string.IsNullOrEmpty(_focusedTabDirectory))
        {
            var elapsed = (int)(DateTime.Now - _tabFocusStartTime.Value).TotalSeconds;
            if (elapsed > 0)
            {
                _statisticsService.RecordFocusTime(_focusedTabDirectory, elapsed);
            }
        }

        // Start tracking focus time for the new tab
        if (newValue is TerminalPairTabViewModel newTerminalTab)
        {
            _tabFocusStartTime = DateTime.Now;
            _focusedTabDirectory = newTerminalTab.Pair.WorkingDirectory;

            // Publish API event for tab activation
            var tabIndex = Tabs.OfType<TerminalPairTabViewModel>().ToList().IndexOf(newTerminalTab);
            var previousIndex = oldValue is TerminalPairTabViewModel oldTerminal
                ? Tabs.OfType<TerminalPairTabViewModel>().ToList().IndexOf(oldTerminal) : -1;
            PublishApiEvent("repo.activated", tabIndex, new
            {
                workingDirectory = newTerminalTab.Pair.WorkingDirectory,
                title = newTerminalTab.Title,
                previousIndex
            });
        }
        else
        {
            _tabFocusStartTime = null;
            _focusedTabDirectory = null;
        }

        // If the selected tab changes, and the dropdown is open, close it.
        if (IsTabDropdownOpen && newValue != null)
        {
            IsTabDropdownOpen = false;
        }
        if (IsTabSwitcherOpen && newValue != null)
        {
            IsTabSwitcherOpen = false;
        }

        // Update IsSelected state on tabs
        if (oldValue != null)
        {
            oldValue.IsSelected = false;
        }
        if (newValue != null)
        {
            newValue.IsSelected = true;
            // Clear unread activity indicator when tab is selected/focused
            newValue.ClearUnreadActivity();

            // Update workspace sidebar highlighting
            if (newValue is TerminalPairTabViewModel terminalTab)
            {
                WorkspaceSidebar?.ClearUnreadActivity(terminalTab.Pair.WorkingDirectory);
                WorkspaceSidebar?.UpdateCurrentTab(terminalTab.Pair.WorkingDirectory);
            }
            else
            {
                // For non-project tabs, clear the current tab highlight
                WorkspaceSidebar?.UpdateCurrentTab(null);
            }
        }
        else
        {
            WorkspaceSidebar?.UpdateCurrentTab(null);
        }
    }

    partial void OnIsTabDropdownOpenChanged(bool value)
    {
        if (value)
        {
            DropdownSearchText = "";
            UpdateFilteredDropdownTabs();
        }
    }

    partial void OnIsTabSwitcherOpenChanged(bool value)
    {
        if (value)
        {
            SwitcherSearchText = "";
            UpdateFilteredSwitcherTabs();
        }
    }

    private void UpdateFilteredDropdownTabs()
    {
        _filteredDropdownTabs.Clear();
        if (string.IsNullOrEmpty(DropdownSearchText))
        {
            foreach (var tab in Tabs)
            {
                _filteredDropdownTabs.Add(tab);
            }
        }
        else
        {
            var searchText = DropdownSearchText.ToLower();
            foreach (var tab in Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText)))
            {
                _filteredDropdownTabs.Add(tab);
            }
        }
    }

    private void UpdateFilteredSwitcherTabs()
    {
        _filteredSwitcherTabs.Clear();
        if (string.IsNullOrEmpty(SwitcherSearchText))
        {
            foreach (var tab in Tabs)
            {
                _filteredSwitcherTabs.Add(tab);
            }
        }
        else
        {
            var searchText = SwitcherSearchText.ToLower();
            foreach (var tab in Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText)))
            {
                _filteredSwitcherTabs.Add(tab);
            }
        }
    }

    public void Initialize()
    {
        // Load quick commands from config
        LoadQuickCommands();

        // Load layout mode and initialize workspace sidebar
        _ = InitializeWorkspaceSidebarAsync();

        // Restore previously open folders
        RestoreOpenFolders();

        // Start git status refresh timer
        _gitStatusTimer.Start();

        // Start git auto-fetch timer (if enabled)
        if (_configService.Load().Settings.GitAutoFetch)
        {
            _gitAutoFetchTimer.Start();
        }

        // Start activity refresh timer
        _activityTimer.Start();

        // Start link detection timer
        _linkDetectionTimer.Start();

        // Start run URL detection timer
        _runUrlDetectionTimer.Start();
    }

    private void LoadQuickCommands()
    {
        var config = _configService.Load();
        QuickCommands = new ObservableCollection<QuickCommand>(config.QuickCommands);
    }

    private async Task RefreshSelectedTabGitStatusAsync()
    {
        if (SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        try
        {
            var previousBranch = terminalTab.GitStatus?.BranchName;
            var status = await _gitStatusService.GetGitStatusAsync(terminalTab.Pair.WorkingDirectory);
            terminalTab.GitStatus = status;
            // Update window title when git status changes
            OnPropertyChanged(nameof(WindowTitle));

            // Publish API events for git status changes
            var tabIndex = Tabs.OfType<TerminalPairTabViewModel>().ToList().IndexOf(terminalTab);
            if (tabIndex >= 0 && _eventAggregator != null)
            {
                PublishApiEvent("repo.git_status_changed", tabIndex, new
                {
                    branch = status.BranchName, isDirty = status.IsDirty,
                    ahead = status.AheadCount, behind = status.BehindCount
                });

                if (previousBranch != null && previousBranch != status.BranchName)
                {
                    PublishApiEvent("repo.branch_switched", tabIndex, new
                    {
                        previousBranch, newBranch = status.BranchName
                    });
                }
            }

            // Also refresh sidebar git status for the current workspace
            if (WorkspaceSidebar != null)
            {
                await WorkspaceSidebar.RefreshGitStatusAsync(terminalTab.Pair.WorkingDirectory);
            }
        }
        catch
        {
            // Silently ignore git status errors
        }
    }

    private async Task RefreshTabGitStatusAsync(TerminalPairTabViewModel tab)
    {
        try
        {
            var status = await _gitStatusService.GetGitStatusAsync(tab.Pair.WorkingDirectory);
            tab.GitStatus = status;
        }
        catch
        {
            // Silently ignore git status errors
        }
    }

    private void RefreshActivityState()
    {
        // Update activity state for all terminal tabs (to detect idle transitions)
        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.UpdateActivityState();
            tab.UpdateWaitingState(_inputPromptDetectionService);

            // Sync activity state to workspace sidebar
            WorkspaceSidebar?.UpdateActivity(
                tab.Pair.WorkingDirectory,
                tab.IsAnyTerminalActive,
                tab.HasUnreadActivity,
                tab.IsWaitingForInput);
        }

        // Also update profile terminal tabs
        foreach (var tab in Tabs.OfType<ProfileTerminalTabViewModel>())
        {
            tab.UpdateActivityState();
            tab.UpdateWaitingState(_inputPromptDetectionService);
        }
    }

    /// <summary>
    /// Automatically fetches from git remotes for all workspaces in the sidebar.
    /// This runs periodically to keep behind counts up to date.
    /// </summary>
    private async Task AutoFetchAllAsync()
    {
        // Fetch for all workspaces in the sidebar (not just open tabs)
        if (WorkspaceSidebar != null)
        {
            await WorkspaceSidebar.FetchAllAsync();
        }

        // Also refresh git status for any open tabs (keeps tab indicators in sync)
        var refreshTasks = Tabs.OfType<TerminalPairTabViewModel>()
            .Select(tab => RefreshTabGitStatusAsync(tab));
        await Task.WhenAll(refreshTasks);
    }

    private void RefreshDetectedLinks()
    {
        // Only refresh the selected tab to keep it lightweight
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.UpdateDetectedLinks(_linkDetectionService);
        }
    }

    private void RefreshRunUrlDetection()
    {
        // Only scan when there's a running project
        if (SelectedTab is not TerminalPairTabViewModel terminalTab)
            return;

        if (terminalTab.RunState != RunState.Running && terminalTab.RunState != RunState.Starting)
            return;

        if (terminalTab.Pair.RunTerminal == null)
            return;

        // Don't re-detect if we already have a URL
        if (!string.IsNullOrEmpty(terminalTab.DetectedRunUrl))
            return;

        // Get recent output from run terminal
        var output = terminalTab.Pair.RunTerminal.GetRecentOutput(5000);
        if (string.IsNullOrEmpty(output))
            return;

        // Get the URL pattern from the active configuration
        var urlPattern = terminalTab.ActiveRunConfiguration?.UrlPattern;

        // Detect URL
        var url = _runUrlDetectionService.DetectUrl(output, urlPattern);
        if (!string.IsNullOrEmpty(url))
        {
            terminalTab.DetectedRunUrl = url;
        }
    }

    // Deferred center panel restores during startup to avoid race conditions
    // when multiple tabs fire async restore for the same singleton panel ViewModel.
    private List<CenterPanelRestoreEventArgs>? _deferredCenterPanelRestores;

    private void RestoreOpenFolders()
    {
        var config = _configService.Load();
        var lastTabType = config.LastSelectedTabType ?? "Project";

        // Restore dashboard if it was open OR if it was the last active tab
        var shouldOpenDashboard = (config.Settings.Dashboard.ShowOnStartup && config.Settings.Dashboard.Enabled)
                                  || lastTabType == "Dashboard";
        if (shouldOpenDashboard)
        {
            _ = OpenDashboardAsync();
        }

        // Restore timeline if it was open OR if it was the last active tab
        var shouldOpenTimeline = (config.Settings.Timeline.ShowOnStartup && config.Settings.Timeline.Enabled)
                                 || lastTabType == "Timeline";
        if (shouldOpenTimeline)
        {
            OpenTimeline();
        }

        // Defer center panel restores until the correct SelectedTab is set.
        // Without this, multiple tabs fire async restores for singleton panel VMs
        // and the last one to complete wins — which may not be the selected tab.
        _deferredCenterPanelRestores = [];

        foreach (var folder in config.OpenFolders)
        {
            if (_fileSystem.DirectoryExists(folder))
            {
                OpenProjectTab(folder);
            }
        }

        // Capture and stop deferring
        var pendingRestores = _deferredCenterPanelRestores;
        _deferredCenterPanelRestores = null;

        // Restore the last selected tab based on type
        switch (lastTabType)
        {
            case "Dashboard":
                var dashboardTab = Tabs.OfType<DashboardTabViewModel>().FirstOrDefault();
                if (dashboardTab != null)
                {
                    SelectedTab = dashboardTab;
                }
                break;

            case "Timeline":
                var timelineTab = Tabs.OfType<TimelineTabViewModel>().FirstOrDefault();
                if (timelineTab != null)
                {
                    SelectedTab = timelineTab;
                }
                break;

            case "Project":
            default:
                if (!string.IsNullOrEmpty(config.LastSelectedFolder))
                {
                    var tabToSelect = Tabs.OfType<TerminalPairTabViewModel>()
                        .FirstOrDefault(t => t.Pair.WorkingDirectory.Equals(config.LastSelectedFolder, StringComparison.OrdinalIgnoreCase));
                    if (tabToSelect != null)
                    {
                        SelectedTab = tabToSelect;
                    }
                }
                break;
        }

        // Now fire deferred center panel restores.
        // Non-selected tabs only get ActiveCenterPanel set (no data load) to avoid
        // async races overwriting the selected tab's data in singleton panel VMs.
        // Data loads on demand when the user switches tabs (via OnViewModelPropertyChanged).
        foreach (var restore in pendingRestores.Where(r => r.Tab != SelectedTab))
        {
            CenterPanelRestoreRequested?.Invoke(this, new CenterPanelRestoreEventArgs
            {
                Tab = restore.Tab,
                PanelId = restore.PanelId,
                GitPanelActiveTab = restore.GitPanelActiveTab,
                SkipDataLoad = true
            });
        }
        var selectedRestore = pendingRestores.FirstOrDefault(r => r.Tab == SelectedTab);
        if (selectedRestore != null)
        {
            CenterPanelRestoreRequested?.Invoke(this, selectedRestore);
        }
    }

    private void SaveOpenFolders()
    {
        var config = _configService.Load();

        // Only save TerminalPairTabViewModel tabs (not Settings, Stats, Dashboard, etc.)
        config.OpenFolders = [.. Tabs.OfType<TerminalPairTabViewModel>().Select(t => t.Pair.WorkingDirectory)];

        // Save the currently selected tab type and folder
        switch (SelectedTab)
        {
            case TerminalPairTabViewModel selectedProjectTab:
                config.LastSelectedTabType = "Project";
                config.LastSelectedFolder = selectedProjectTab.Pair.WorkingDirectory;
                break;
            case DashboardTabViewModel:
                config.LastSelectedTabType = "Dashboard";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            case TimelineTabViewModel:
                config.LastSelectedTabType = "Timeline";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            case SettingsTabViewModel:
                config.LastSelectedTabType = "Settings";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            case StatisticsTabViewModel:
                config.LastSelectedTabType = "Statistics";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            default:
                config.LastSelectedTabType = "Project";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
        }

        _configService.Save(config);
    }

    private void SaveDirectorySettings(TerminalPairTabViewModel tab)
    {
        var config = _configService.Load();
        var normalizedPath = NormalizePath(tab.Pair.WorkingDirectory);

        // Get existing settings or create new
        if (!config.DirectorySettings.TryGetValue(normalizedPath, out var settings))
        {
            settings = new DirectorySettings();
        }

        // Update basic settings
        settings.LayoutMode = tab.LayoutMode;
        settings.SplitRatio = tab.SplitRatio;
        settings.ActiveTerminal = tab.ActiveTerminal.ToString();

        // Update run settings
        settings.IsRunTerminalVisible = tab.IsRunTerminalVisible;
        settings.RunSplitRatio = tab.RunSplitRatio;
        settings.ActiveRunConfigurationId = tab.ActiveRunConfiguration?.Id;
        settings.RunConfigurations = [.. tab.RunConfigurations];

        // Update explorer/panel settings
        settings.IsExplorerVisible = tab.IsExplorerVisible;
        settings.ExplorerSplitRatio = tab.ExplorerSplitRatio;
        settings.IsLeftPanelVisible = tab.IsLeftPanelVisible;
        settings.LeftPanelSplitRatio = tab.LeftPanelSplitRatio;

        // Update center panel state
        settings.ActiveCenterPanel = tab.ActiveCenterPanel?.PanelId;

        // Save git panel active tab if the git panel is the center panel
        if (tab.ActiveCenterPanel is UnifiedGitPanelViewModel gitPanel)
        {
            settings.GitPanelActiveTab = gitPanel.ActiveTab.ToString();
        }

        // Update right sidebar panel state
        settings.OpenRightPanels = tab.RightPanels.Select(p => p.PanelId).ToList();
        settings.ActiveRightPanel = tab.ActiveRightPanel?.PanelId;

        config.DirectorySettings[normalizedPath] = settings;
        _configService.Save(config);
    }

    private DirectorySettings? GetDirectorySettings(string workingDirectory)
    {
        var config = _configService.Load();
        var normalizedPath = NormalizePath(workingDirectory);

        return config.DirectorySettings.TryGetValue(normalizedPath, out var settings) ? settings : null;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
    }

    /// <summary>
    /// Updates the recent folders list when a folder is opened.
    /// </summary>
    private void UpdateRecentFolders(string path)
    {
        var config = _configService.Load();
        var recentPaths = config.Settings.Repositories.RecentPaths;
        var maxItems = config.Settings.Repositories.MaxRecentItems;

        // Normalize path for comparison
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Remove existing entry (case-insensitive) and add to front
        recentPaths.RemoveAll(p => p.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        recentPaths.Insert(0, normalizedPath);

        // Trim to max
        while (recentPaths.Count > maxItems)
        {
            recentPaths.RemoveAt(recentPaths.Count - 1);
        }

        _configService.Save(config);
    }

    [RelayCommand]
    private void OpenNewProject()
    {
        try
        {
            var selectedPath = _folderPickerService.PickFolder("Select Project Directory");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                OpenProjectTab(selectedPath);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Error opening project: {ex.Message}");
        }
    }

    public void OpenProjectTab(string workingDirectory, bool forceNew = false)
    {
        try
        {
            // Normalize the path for comparison
            workingDirectory = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!_fileSystem.DirectoryExists(workingDirectory)) // Use injected IFileSystem
            {
                _dialogService.ShowError($"Directory not found: {workingDirectory}"); // Use injected IDialogService
                return;
            }

            // Check if we already have a tab open for this directory (unless forceNew)
            if (!forceNew)
            {
                var existingTab = Tabs.OfType<TerminalPairTabViewModel>().FirstOrDefault(t =>
                    string.Equals(
                        Path.GetFullPath(t.Pair.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        workingDirectory,
                        StringComparison.OrdinalIgnoreCase));

                if (existingTab != null)
                {
                    // Focus the existing tab instead of creating a new one
                    SelectedTab = existingTab;
                    return;
                }
            }

            // Calculate duplicate index for display title
            var duplicateIndex = GetDuplicateTabIndex(workingDirectory);

            var settings = _profileRegistry.Settings;

            // Get the AI assistant for this directory
            var aiAssistant = _aiAssistantService.GetAssistantForDirectory(workingDirectory);
            var enabledAssistants = _aiAssistantService.GetEnabledAssistants();

            // Create profiles for custom command and shell
            var customProfile = new Profile
            {
                Id = "custom",
                Name = aiAssistant.Name,
                Command = aiAssistant.Command,
                WorkingDir = workingDirectory,
                Icon = aiAssistant.Icon
            };

            var shellProfile = new Profile
            {
                Id = "shell",
                Name = settings.ShellCommandName,
                Command = settings.ShellCommand,
                WorkingDir = workingDirectory,
                Icon = settings.ShellCommandIcon
            };

            // Create the terminal pair
            var pair = new TerminalPair(workingDirectory, customProfile, shellProfile, _statisticsService);

            // Create terminal controls for both
            var customControl = _terminalFactory.CreateTerminalControl(pair.CustomTerminal);
            var shellControl = _terminalFactory.CreateTerminalControl(pair.ShellTerminal);

            // Create view model with AI assistant info
            var tabViewModel = new TerminalPairTabViewModel(pair, aiAssistant, enabledAssistants, settings.ShellCommandIcon, _statisticsService, _gitStatusService, _toastService, duplicateIndex, _taskService);
            tabViewModel.AiAssistantSwitchRequested += OnAiAssistantSwitchRequested;
            tabViewModel.SetTerminalControls(customControl, shellControl);
            tabViewModel.CloseRequested += OnTabCloseRequested;
            tabViewModel.SettingsChanged += OnTabSettingsChanged;

            // Restore per-directory settings if available
            var dirSettings = GetDirectorySettings(workingDirectory);
            if (dirSettings != null)
            {
                tabViewModel.LayoutMode = dirSettings.LayoutMode;
                tabViewModel.SplitRatio = dirSettings.SplitRatio;
                if (Enum.TryParse<ActiveTerminal>(dirSettings.ActiveTerminal, out var activeTerminal))
                {
                    tabViewModel.ActiveTerminal = activeTerminal;
                    pair.ActiveTerminal = activeTerminal;
                }

                // Restore run settings
                tabViewModel.IsRunTerminalVisible = dirSettings.IsRunTerminalVisible;
                tabViewModel.RunSplitRatio = dirSettings.RunSplitRatio;
            }

            // Initialize run configurations (from settings or auto-detect)
            InitializeRunConfigurations(tabViewModel, workingDirectory, dirSettings);

            // Track sessions
            _sessionManager.TrackSession(pair.CustomTerminal);
            _sessionManager.TrackSession(pair.ShellTerminal);

            // Subscribe to link click events
            pair.CustomTerminal.LinkClicked += (s, text) => HandleLinkClick(text, workingDirectory);
            pair.ShellTerminal.LinkClicked += (s, text) => HandleLinkClick(text, workingDirectory);

            // Subscribe to run terminal events
            tabViewModel.RunStartRequested += OnRunStartRequested;
            tabViewModel.RunStopRequested += OnRunStopRequested;

            // Initialize file explorer and panel system
            var explorerViewModel = _viewModelFactory.CreateFileExplorer(workingDirectory);
            tabViewModel.InitializePanelSystem(explorerViewModel);

            // Restore explorer/panel settings
            if (dirSettings != null)
            {
                tabViewModel.IsExplorerVisible = dirSettings.IsExplorerVisible;
                tabViewModel.ExplorerSplitRatio = dirSettings.ExplorerSplitRatio;
                tabViewModel.IsLeftPanelVisible = dirSettings.IsLeftPanelVisible;
                tabViewModel.LeftPanelSplitRatio = dirSettings.LeftPanelSplitRatio;
            }

            // Wire up explorer events
            explorerViewModel.CdToShellRequested += (s, path) => tabViewModel.SendCdToShell(path);
            explorerViewModel.FileViewerRequested += OnExplorerFileViewerRequested;
            explorerViewModel.PopOutRequested += OnExplorerPopOutRequested;
            explorerViewModel.RenameRequested += OnExplorerRenameRequested;
            explorerViewModel.FileHistoryRequested += OnExplorerFileHistoryRequested;
            explorerViewModel.FileBlameRequested += OnExplorerFileBlameRequested;

            // Initialize explorer async (don't await - let it load in background)
            _ = explorerViewModel.InitializeAsync(workingDirectory);

            Tabs.Add(tabViewModel);
            SelectedTab = tabViewModel;

            // Track in recent folders
            UpdateRecentFolders(workingDirectory);

            // Fetch git status for the new tab
            _ = RefreshTabGitStatusAsync(tabViewModel);

            // Sync with workspace sidebar
            _ = WorkspaceSidebar?.SyncWithOpenTabAsync(workingDirectory);

            // Restore center panel state (fires event for MainWindow to handle)
            if (dirSettings?.ActiveCenterPanel != null)
            {
                var restoreArgs = new CenterPanelRestoreEventArgs
                {
                    Tab = tabViewModel,
                    PanelId = dirSettings.ActiveCenterPanel,
                    GitPanelActiveTab = dirSettings.GitPanelActiveTab
                };

                if (_deferredCenterPanelRestores != null)
                {
                    // During startup: defer until SelectedTab is finalized
                    _deferredCenterPanelRestores.Add(restoreArgs);
                }
                else
                {
                    CenterPanelRestoreRequested?.Invoke(this, restoreArgs);
                }
            }

            // Restore right sidebar panel state
            if (dirSettings?.OpenRightPanels?.Count > 0)
            {
                RightPanelRestoreRequested?.Invoke(this, new RightPanelRestoreEventArgs
                {
                    Tab = tabViewModel,
                    PanelIds = dirSettings.OpenRightPanels,
                    ActivePanelId = dirSettings.ActiveRightPanel
                });
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Error creating terminal: {ex.Message}"); // Use injected IDialogService
        }
    }

    /// <summary>
    /// Gets the next duplicate index for tabs with the same working directory.
    /// Returns 0 for the first tab (no suffix), 2 for the second, etc.
    /// </summary>
    private int GetDuplicateTabIndex(string workingDirectory)
    {
        var normalizedPath = Path.GetFullPath(workingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var existingTabs = Tabs.OfType<TerminalPairTabViewModel>()
            .Where(t => string.Equals(
                Path.GetFullPath(t.Pair.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (existingTabs.Count == 0)
            return 0; // First tab, no index needed

        // Find the highest existing index and add 1
        var maxIndex = existingTabs.Max(t => t.DuplicateIndex);
        return Math.Max(2, maxIndex + 1);
    }

    /// <summary>
    /// Duplicates the specified tab, creating a new tab for the same directory.
    /// </summary>
    [RelayCommand]
    private void DuplicateTab(ITabViewModel? tab)
    {
        if (tab is TerminalPairTabViewModel terminalTab)
        {
            OpenProjectTab(terminalTab.Pair.WorkingDirectory, forceNew: true);
        }
    }

    /// <summary>
    /// Moves the specified tab to the front of the tab list.
    /// </summary>
    [RelayCommand]
    private void MoveTabToFront(ITabViewModel? tab)
    {
        if (tab == null) return;
        var index = Tabs.IndexOf(tab);
        if (index > 0)
        {
            Tabs.Move(index, 0);
        }
    }

    /// <summary>
    /// Moves the specified tab to the end of the tab list.
    /// </summary>
    [RelayCommand]
    private void MoveTabToEnd(ITabViewModel? tab)
    {
        if (tab == null) return;
        var index = Tabs.IndexOf(tab);
        if (index >= 0 && index < Tabs.Count - 1)
        {
            Tabs.Move(index, Tabs.Count - 1);
        }
    }

    /// <summary>
    /// Closes all tabs except the specified one.
    /// </summary>
    [RelayCommand]
    private void CloseOtherTabs(ITabViewModel? tab)
    {
        if (tab == null) return;
        var tabsToClose = Tabs.Where(t => t != tab && t.IsCloseable).ToList();
        foreach (var t in tabsToClose)
        {
            CloseTabCommand.Execute(t);
        }
    }

    /// <summary>
    /// Closes all tabs to the right of the specified tab.
    /// </summary>
    [RelayCommand]
    private void CloseTabsToRight(ITabViewModel? tab)
    {
        if (tab == null) return;
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        var tabsToClose = Tabs.Skip(index + 1).Where(t => t.IsCloseable).ToList();
        foreach (var t in tabsToClose)
        {
            CloseTabCommand.Execute(t);
        }
    }

    /// <summary>
    /// Opens a new tab with a single terminal running the specified profile.
    /// </summary>
    /// <param name="profile">The profile to launch.</param>
    /// <param name="workingDirectory">Optional working directory. If null, uses the profile's WorkingDir.</param>
    public void OpenProfileTab(Profile profile, string? workingDirectory = null)
    {
        try
        {
            // Determine working directory
            var effectiveWorkingDir = workingDirectory;
            if (string.IsNullOrWhiteSpace(effectiveWorkingDir))
            {
                effectiveWorkingDir = profile.GetExpandedWorkingDir();
            }

            // If still empty, use user profile directory
            if (string.IsNullOrWhiteSpace(effectiveWorkingDir))
            {
                effectiveWorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            // Normalize path
            effectiveWorkingDir = Path.GetFullPath(effectiveWorkingDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!_fileSystem.DirectoryExists(effectiveWorkingDir)) // Use injected IFileSystem
            {
                _dialogService.ShowError($"Directory not found: {effectiveWorkingDir}"); // Use injected IDialogService
                return;
            }

            // Clone the profile with the working directory set
            var profileWithDir = new Profile
            {
                Id = profile.Id,
                Name = profile.Name,
                Command = profile.Command,
                WorkingDir = effectiveWorkingDir,
                Icon = profile.Icon,
                Shortcut = profile.Shortcut,
                AutoStart = profile.AutoStart
            };

            // Create view model
            var tabViewModel = new ProfileTerminalTabViewModel(profileWithDir, effectiveWorkingDir, _statisticsService);

            // Create terminal control
            var terminalControl = _terminalFactory.CreateTerminalControl(tabViewModel.Session);
            tabViewModel.SetTerminalControl(terminalControl);

            // Subscribe to events
            tabViewModel.CloseRequested += OnTabCloseRequested;

            // Track session
            _sessionManager.TrackSession(tabViewModel.Session);

            // Add tab and select it
            Tabs.Add(tabViewModel);
            SelectedTab = tabViewModel;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Error launching profile: {ex.Message}"); // Use injected IDialogService
        }
    }

    /// <summary>
    /// Opens a profile tab with a folder picker to select the working directory.
    /// </summary>
    /// <param name="profile">The profile to launch.</param>
    public void OpenProfileTabWithPicker(Profile profile)
    {
        try
        {
            // Get initial directory from profile's configured directory if it exists
            var initialDir = profile.GetExpandedWorkingDir();
            if (string.IsNullOrWhiteSpace(initialDir) || !_fileSystem.DirectoryExists(initialDir))
            {
                initialDir = null;
            }

            var selectedPath = _folderPickerService.PickFolder(
                $"Select Working Directory for {profile.Name}",
                initialDir);

            if (!string.IsNullOrEmpty(selectedPath))
            {
                OpenProfileTab(profile, selectedPath);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Error opening folder picker: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CloseTab(ITabViewModel? tab)
    {
        if (tab == null) return;

        if (tab is TerminalPairTabViewModel terminalTab)
        {
            var hasRunning = terminalTab.Pair.CustomTerminal.IsProcessRunning() || terminalTab.Pair.ShellTerminal.IsProcessRunning();

            if (hasRunning && _profileRegistry.Settings.ConfirmOnClose)
            {
                if (!_dialogService.ShowConfirmation( // Use injected IDialogService
                    $"Terminals in '{terminalTab.Title}' are still running. Close anyway?",
                    "Confirm Close"))
                    return;
            }

            terminalTab.CloseRequested -= OnTabCloseRequested;
            terminalTab.SettingsChanged -= OnTabSettingsChanged;
            terminalTab.RunStartRequested -= OnRunStartRequested;
            terminalTab.RunStopRequested -= OnRunStopRequested;
            _sessionManager.CloseSession(terminalTab.Pair.CustomTerminal);
            _sessionManager.CloseSession(terminalTab.Pair.ShellTerminal);
            if (terminalTab.Pair.RunTerminal != null)
            {
                _sessionManager.CloseSession(terminalTab.Pair.RunTerminal);
            }
            terminalTab.Pair.Dispose();
        }
        else if (tab is SettingsTabViewModel settingsTab)
        {
            settingsTab.CloseRequested -= OnTabCloseRequested;
            settingsTab.ConfigSaved -= OnConfigSaved;
        }
        else if (tab is ProfilesTabViewModel profilesTab)
        {
            profilesTab.CloseRequested -= OnTabCloseRequested;
            profilesTab.ProfileLaunchRequested -= OnProfileLaunchRequested;
        }
        else if (tab is StatisticsTabViewModel statsTab)
        {
            statsTab.CloseRequested -= OnTabCloseRequested;
        }
        else if (tab is TimelineTabViewModel timelineTab)
        {
            timelineTab.CloseRequested -= OnTabCloseRequested;
            timelineTab.Dispose();

            // Save that timeline is closed
            var config = _configService.Load();
            config.Settings.Timeline.ShowOnStartup = false;
            _configService.Save(config);
        }
        else if (tab is DashboardTabViewModel dashboardTab)
        {
            dashboardTab.CloseRequested -= OnTabCloseRequested;
            dashboardTab.PrReviewRequested -= OnDashboardPrReviewRequested;

            // Save that dashboard is closed
            var config = _configService.Load();
            config.Settings.Dashboard.ShowOnStartup = false;
            _configService.Save(config);
        }
        else if (tab is ProfileTerminalTabViewModel profileTab)
        {
            var hasRunning = profileTab.Session.IsProcessRunning();

            if (hasRunning && _profileRegistry.Settings.ConfirmOnClose)
            {
                if (!_dialogService.ShowConfirmation( // Use injected IDialogService
                    $"Terminal '{profileTab.Title}' is still running. Close anyway?",
                    "Confirm Close"))
                    return;
            }

            profileTab.CloseRequested -= OnTabCloseRequested;
            _sessionManager.CloseSession(profileTab.Session);
            profileTab.Session.Dispose();
        }

        Tabs.Remove(tab);

        if (SelectedTab == tab && Tabs.Count > 0)
        {
            SelectedTab = Tabs[^1];
        }
    }

    [RelayCommand]
    private void SwitchActiveTerminal()
    {
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.SwitchTerminalCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ExecuteQuickCommand(QuickCommand? command)
    {
        if (command == null || SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        // Switch to the target terminal
        if (command.Target == QuickCommandTarget.Custom)
        {
            terminalTab.ShowCustomTerminalCommand.Execute(null);
        }
        else
        {
            // If targeting Shell and currently in Custom-only mode, switch to split layout
            if (terminalTab.IsCustomFullMode)
            {
                terminalTab.SetVerticalSplitLayoutCommand.Execute(null);
            }
            terminalTab.ShowShellTerminalCommand.Execute(null);
        }

        var targetSession = command.Target == QuickCommandTarget.Custom
            ? terminalTab.Pair.CustomTerminal
            : terminalTab.Pair.ShellTerminal;

        targetSession.SendText(command.Text, command.AppendNewline, command.NewlineChar, command.UseUserInput);

        // Focus the terminal
        targetSession.Focus();
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is ITabViewModel tab)
        {
            CloseTab(tab);
        }
    }

    private void OnTabSettingsChanged(object? sender, EventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            SaveDirectorySettings(tab);
        }
    }

    private void OnAiAssistantSwitchRequested(object? sender, AiAssistantSwitchEventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            // Save the new AI selection
            _aiAssistantService.SetAssistantForDirectory(tab.WorkingDirectory, e.NewAssistant.Id);

            // Create new profile for the new AI assistant
            var newProfile = new Profile
            {
                Id = "custom",
                Name = e.NewAssistant.Name,
                Command = e.NewAssistant.Command,
                WorkingDir = tab.WorkingDirectory,
                Icon = e.NewAssistant.Icon
            };

            // Close old custom terminal session
            var oldSession = tab.Pair.CustomTerminal;
            _sessionManager.CloseSession(oldSession);

            // Create new session and control
            var newSession = new TerminalSession(newProfile, _statisticsService, "Custom");
            var newControl = _terminalFactory.CreateTerminalControl(newSession);
            _sessionManager.TrackSession(newSession);

            // Subscribe to link click events
            newSession.LinkClicked += (s, text) => HandleLinkClick(text, tab.WorkingDirectory);

            // Replace the terminal in the pair
            tab.Pair.ReplaceCustomTerminal(newSession);
            tab.SetCustomTerminalControl(newControl);
            tab.UpdateActiveAiAssistant(e.NewAssistant);
        }
    }

    private void OnRunStartRequested(object? sender, RunConfiguration configuration)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            RunTerminalRequested?.Invoke(this, new RunTerminalRequestedEventArgs
            {
                Tab = tab,
                Configuration = configuration,
                IsStop = false
            });
        }
    }

    private void OnRunStopRequested(object? sender, EventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab && tab.ActiveRunConfiguration != null)
        {
            RunTerminalRequested?.Invoke(this, new RunTerminalRequestedEventArgs
            {
                Tab = tab,
                Configuration = tab.ActiveRunConfiguration,
                IsStop = true
            });
        }
    }

    private void OnExplorerFileViewerRequested(object? sender, FileViewerRequestedEventArgs e)
    {
        // Fire event to open in the popup viewer
        if (e.Mode == FileViewerMode.Preview)
        {
            FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
            {
                FilePath = e.FilePath,
                Line = 0,
                Column = 0
            });
        }
        else
        {
            // For edit mode, we need a different event or we can reuse FileEditRequested from GitFilesViewModel
            // For now, use FilePreviewRequested and let the viewer switch to edit mode
            FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
            {
                FilePath = e.FilePath,
                Line = 0,
                Column = 0,
                OpenInEditMode = true
            });
        }
    }

    private void OnExplorerPopOutRequested(object? sender, FileViewerRequestedEventArgs e)
    {
        // Create a detached file viewer window
        var viewer = _viewModelFactory.CreateFileViewer(isDetached: true);
        viewer.Open(e.FilePath, e.Mode);

        // Create and show the window
        var window = new Views.FileViewerWindow
        {
            DataContext = viewer
        };
        window.Show();
    }

    private async void OnExplorerRenameRequested(object? sender, FileSystemNode node)
    {
        if (sender is not FileExplorerViewModel explorerVm)
            return;

        var newName = _dialogService.ShowInput(
            $"Enter new name for '{node.Name}':",
            "Rename",
            node.Name);

        if (!string.IsNullOrWhiteSpace(newName) && newName != node.Name)
        {
            await explorerVm.PerformRenameAsync(node, newName);
        }
    }

    private void OnExplorerFileHistoryRequested(object? sender, FileHistoryRequestedEventArgs e)
    {
        FileHistoryRequested?.Invoke(this, e);
    }

    private void OnExplorerFileBlameRequested(object? sender, FileBlameRequestedEventArgs e)
    {
        FileBlameRequested?.Invoke(this, e);
    }

    private void InitializeRunConfigurations(TerminalPairTabViewModel tab, string workingDirectory, DirectorySettings? dirSettings)
    {
        List<RunConfiguration> configs;

        if (dirSettings != null && dirSettings.RunConfigurations.Count > 0)
        {
            // Use saved configurations
            configs = dirSettings.RunConfigurations;
        }
        else
        {
            // Auto-detect project type and create configurations
            configs = _projectDetectionService.GetOrCreateConfigurations(
                workingDirectory,
                dirSettings ?? new DirectorySettings());
        }

        tab.InitializeRunConfigurations(configs, dirSettings?.ActiveRunConfigurationId);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        // Check if settings tab already exists
        var existingSettings = Tabs.OfType<SettingsTabViewModel>().FirstOrDefault();
        if (existingSettings != null)
        {
            SelectedTab = existingSettings;
            return;
        }

        // Create new settings tab
        var settingsTab = _viewModelFactory.CreateSettings();
        settingsTab.CloseRequested += OnTabCloseRequested;
        settingsTab.ConfigSaved += OnConfigSaved;
        Tabs.Add(settingsTab);
        SelectedTab = settingsTab;
    }

    [RelayCommand]
    private void OpenProfiles()
    {
        // Open Settings and navigate to Profiles section
        var existingSettings = Tabs.OfType<SettingsTabViewModel>().FirstOrDefault();
        if (existingSettings != null)
        {
            existingSettings.SelectedSection = SettingsSection.Profiles;
            SelectedTab = existingSettings;
            return;
        }

        // Create new settings tab with Profiles section selected
        var settingsTab = _viewModelFactory.CreateSettings();
        settingsTab.SelectedSection = SettingsSection.Profiles;
        settingsTab.CloseRequested += OnTabCloseRequested;
        settingsTab.ConfigSaved += OnConfigSaved;
        Tabs.Add(settingsTab);
        SelectedTab = settingsTab;
    }

    [RelayCommand]
    private async Task OpenDashboardAsync()
    {
        // Check if dashboard tab already exists
        var existingDashboard = Tabs.OfType<DashboardTabViewModel>().FirstOrDefault();
        if (existingDashboard != null)
        {
            SelectedTab = existingDashboard;
            return;
        }

        // Create new dashboard tab
        var dashboardTab = _viewModelFactory.CreateDashboard(this);
        dashboardTab.CloseRequested += OnTabCloseRequested;
        dashboardTab.PrReviewRequested += OnDashboardPrReviewRequested;
        Tabs.Add(dashboardTab);
        SelectedTab = dashboardTab;

        // Save that dashboard is open
        var config = _configService.Load();
        config.Settings.Dashboard.ShowOnStartup = true;
        _configService.Save(config);

        // Initialize the dashboard (fetches data)
        await dashboardTab.InitializeAsync();
    }

    /// <summary>
    /// Event raised when PR Review Mode should be opened from the Dashboard.
    /// </summary>
    public event EventHandler<PrReviewRequestedEventArgs>? DashboardPrReviewRequested;

    private void OnDashboardPrReviewRequested(object? sender, PrReviewRequestedEventArgs e)
    {
        DashboardPrReviewRequested?.Invoke(this, e);
    }

    private void OnProfileLaunchRequested(object? sender, ProfileLaunchEventArgs e)
    {
        if (e.PickFolder)
        {
            OpenProfileTabWithPicker(e.Profile);
        }
        else
        {
            OpenProfileTab(e.Profile);
        }
    }

    [RelayCommand]
    private void OpenStatistics()
    {
        try
        {
            // Check if statistics tab already exists
            var existingStats = Tabs.OfType<StatisticsTabViewModel>().FirstOrDefault();
            if (existingStats != null)
            {
                SelectedTab = existingStats;
                // Also refresh the stats when focusing the existing tab
                existingStats.LoadStatsCommand.Execute(null);
                return;
            }

            // Create new statistics tab
            var statsTab = _viewModelFactory.CreateStatistics();
            statsTab.CloseRequested += OnTabCloseRequested;
            Tabs.Add(statsTab);
            SelectedTab = statsTab;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"An error occurred while opening the statistics view:\n\n{ex.Message}"); // Use injected IDialogService
        }
    }

    [RelayCommand]
    private void OpenTimeline()
    {
        try
        {
            // Check if timeline tab already exists
            var existingTimeline = Tabs.OfType<TimelineTabViewModel>().FirstOrDefault();
            if (existingTimeline != null)
            {
                SelectedTab = existingTimeline;
                return;
            }

            // Create new timeline tab
            var timelineTab = _viewModelFactory.CreateTimeline();
            timelineTab.CloseRequested += OnTabCloseRequested;
            Tabs.Add(timelineTab);
            SelectedTab = timelineTab;

            // Save that timeline is open (for restore on startup)
            var config = _configService.Load();
            config.Settings.Timeline.ShowOnStartup = true;
            _configService.Save(config);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"An error occurred while opening Timeline Mode:\n\n{ex.Message}");
        }
    }

    private void OnConfigSaved(object? sender, EventArgs e)
    {
        // Reload quick commands when config is saved
        LoadQuickCommands();

        // Reload touch mode setting and adjust sidebar width
        var newTouchMode = _configService.Load().Settings.TouchMode;
        if (newTouchMode != TouchMode)
        {
            TouchMode = newTouchMode;
            // Adjust sidebar width for touch mode (narrower for more content space)
            if (WorkspaceSidebar != null)
            {
                WorkspaceSidebar.Width = newTouchMode ? 180 : 250;
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(SidebarColumnWidth));
            }
        }

        // Reload AI assistants and update all terminal tabs
        _aiAssistantService.Reload();
        var enabledAssistants = _aiAssistantService.GetEnabledAssistants();
        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.RefreshAvailableAiAssistants(enabledAssistants);
        }

        // Sync API server state with saved config
        var apiSettings = _configService.Load().Settings.Api;
        if (_apiServer != null)
        {
            if (apiSettings.Enabled && !_apiServer.IsRunning)
                _ = StartApiServerAsync();
            else if (!apiSettings.Enabled && _apiServer.IsRunning)
                _ = StopApiServerAsync();
        }

        // Notify that config has been reloaded (for system tray, etc.)
        ConfigReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Handles Ctrl+Click link detection from terminals.
    /// </summary>
    private void HandleLinkClick(string recentOutput, string workingDirectory)
    {
        if (string.IsNullOrEmpty(recentOutput)) return;

        // Try to find a link in the recent output
        // We scan the output looking for URL patterns, file paths, or custom patterns
        var lines = recentOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Start from the end (most recent) and work backwards
        foreach (var line in lines.Reverse())
        {
            var cleanLine = line.Trim();
            if (string.IsNullOrEmpty(cleanLine)) continue;

            // Try each "word" in the line
            var words = cleanLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                var link = _linkDetectionService.DetectLink(word, workingDirectory);
                if (link != null)
                {
                    HandleDetectedLink(link);
                    return;
                }
            }

            // Also try the whole line in case it's a file path with spaces
            var linkFromLine = _linkDetectionService.DetectLink(cleanLine, workingDirectory);
            if (linkFromLine != null)
            {
                HandleDetectedLink(linkFromLine);
                return;
            }
        }

    }

    private void HandleDetectedLink(string link)
    {
        // Check if it's a file path that we should show in preview
        if (LinkDetectionService.IsFilePath(link))
        {
            // Parse for line/column numbers
            var (path, line, column) = FilePreviewService.ParseFilePathWithPosition(link);

            // Fire event for MainWindow to show preview
            FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
            {
                FilePath = path,
                Line = line,
                Column = column
            });
        }
        else
        {
            // It's a URL or something else - open normally
            _linkDetectionService.OpenLink(link);
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        var folder = terminalTab.Pair.WorkingDirectory;
        if (_fileSystem.DirectoryExists(folder))
        {
            _processService.Start("explorer.exe", folder);
        }
    }

    [RelayCommand]
    private void CycleTab(bool forward)
    {
        if (Tabs.Count <= 1) return;

        var currentIndex = SelectedTab != null
            ? Tabs.IndexOf(SelectedTab)
            : 0;

        int newIndex;
        if (forward)
        {
            newIndex = (currentIndex + 1) % Tabs.Count;
        }
        else
        {
            newIndex = (currentIndex - 1 + Tabs.Count) % Tabs.Count;
        }

        SelectedTab = Tabs[newIndex];
    }

    public event EventHandler? ScratchPadRequested;
    public event EventHandler? GitChangesRequested;
    public event EventHandler? SetupRequested;
    public event EventHandler? PrReviewRequested;
    public event EventHandler? MarkdownPreviewRequested;
    public event EventHandler<GitPanelTab>? UnifiedGitPanelRequested;
    public event EventHandler<CenterPanelRestoreEventArgs>? CenterPanelRestoreRequested;
    public event EventHandler<RightPanelRestoreEventArgs>? RightPanelRestoreRequested;
    public event EventHandler? ReflogRequested;
    public event EventHandler? RepositorySwitcherRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? ClaudeTasksRequested;
    public event EventHandler? TestRunnerRequested;
    public event EventHandler? WhatsNewRequested;
    public event EventHandler<string>? AiPanelCommandRequested;

    /// <summary>
    /// Returns the static palette commands for the Recent Features page.
    /// </summary>
    internal IReadOnlyList<PaletteCommand> GetPaletteCommandsForFeatures() => _allPaletteCommands;

    [RelayCommand]
    private void OpenSetup()
    {
        SetupRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenScratchPad()
    {
        ScratchPadRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenGitChanges()
    {
        GitChangesRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenGitPanelFromToolbar()
    {
        UnifiedGitPanelRequested?.Invoke(this, GitPanelTab.Changes);
    }

    private void OpenUnifiedGitPanel(GitPanelTab tab)
    {
        UnifiedGitPanelRequested?.Invoke(this, tab);
    }

    [RelayCommand]
    private void OpenHelp()
    {
        IsHelpOpen = true;
    }

    [RelayCommand]
    private void OpenTabDropdown()
    {
        IsTabDropdownOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanOpenDetectedLinks))]
    private async Task OpenDetectedLinks()
    {
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            await _detectedLinksViewModel.OpenAsync(terminalTab);
        }
    }

    private bool CanOpenDetectedLinks() => SelectedTab is TerminalPairTabViewModel;

    [RelayCommand]
    private void CloseHelp()
    {
        IsHelpOpen = false;
    }

    partial void OnPaletteSearchTextChanged(string value)
    {
        FilterPaletteCommands();
    }

    partial void OnIsCommandPaletteOpenChanged(bool value)
    {
        if (value)
        {
            PaletteSearchText = "";
            FilterPaletteCommands();
            if (FilteredPaletteCommands.Any())
            {
                SelectedPaletteCommand = FilteredPaletteCommands.First();
            }
        }
    }

    private void InitializeCommandPalette()
    {
        _allPaletteCommands =
        [
            // Tab/Project commands
            new() {
                Id = "new-project",
                Name = "New Project",
                Description = "Open folder as new project",
                Shortcut = "Ctrl+N",
                Icon = "📁",
                Category = "Project",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenNewProjectCommand.Execute(null)
            },
            new() {
                Id = "close-tab",
                Name = "Close Tab",
                Description = "Close current tab",
                Shortcut = "Ctrl+W",
                Icon = "✕",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (SelectedTab != null) CloseTabCommand.Execute(SelectedTab); }
            },
            new() {
                Id = "tab-switcher",
                Name = "Switch Tab",
                Description = "Search and switch tabs",
                Shortcut = "Ctrl+Shift+T",
                Icon = "🔍",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { IsTabSwitcherOpen = true; SwitcherSearchText = ""; }
            },
            new() {
                Id = "duplicate-tab",
                Name = "Duplicate Tab",
                Description = "Open new tab for same directory",
                Icon = "📋",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) DuplicateTabCommand.Execute(tab); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // File commands
            new() {
                Id = "file-preview",
                Name = "Preview File",
                Description = "Open file preview",
                Shortcut = "Ctrl+O",
                Icon = "👁",
                Category = "File",
                IntroducedOn = new DateOnly(2025, 12, 19),
                Execute = () => FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = "", Line = 0, Column = 0}) // Needs to be improved
            },
            new() {
                Id = "file-edit",
                Name = "Edit File",
                Description = "Open file in editor",
                Shortcut = "Ctrl+Shift+E",
                Icon = "✏️",
                Category = "File",
                IntroducedOn = new DateOnly(2025, 12, 19),
                Execute = () => { /* Needs to be improved */ }
            },
            new() {
                Id = "open-explorer",
                Name = "Open in Explorer",
                Description = "Open folder in file explorer",
                Shortcut = "Ctrl+E",
                Icon = "📂",
                Category = "File",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenInExplorerCommand.Execute(null),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Terminal commands
            new() {
                Id = "switch-terminal",
                Name = "Switch Terminal",
                Description = "Toggle between custom and shell",
                Shortcut = "Ctrl+`",
                Icon = "⇄",
                Category = "Terminal",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => SwitchActiveTerminalCommand.Execute(null),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Settings
            new() {
                Id = "settings",
                Name = "Settings",
                Description = "Open settings editor",
                Shortcut = "Ctrl+,",
                Icon = "⚙️",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenSettingsCommand.Execute(null)
            },
            new() {
                Id = "profiles",
                Name = "Settings: Profiles",
                Description = "Open settings and manage terminal profiles",
                Shortcut = "Ctrl+P",
                Icon = "👤",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenProfilesCommand.Execute(null)
            },
            new() {
                Id = "setup",
                Name = "Setup",
                Description = "Check dependencies and setup",
                Icon = "🔧",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenSetupCommand.Execute(null)
            },

            // Help
            new() {
                Id = "help",
                Name = "Help",
                Description = "Show keyboard shortcuts",
                Shortcut = "F1",
                Icon = "❓",
                Category = "Help",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => IsHelpOpen = true
            },
            new() {
                Id = "open-crash-log-folder",
                Name = "Open Crash Log Folder",
                Description = "Open the folder with app crash reports",
                Icon = "🩺",
                Category = "Help",
                IntroducedOn = new DateOnly(2026, 2, 13),
                Execute = () =>
                {
                    var crashLogDirectory = GetCrashLogDirectoryPath();
                    Directory.CreateDirectory(crashLogDirectory);
                    _processService.OpenFolder(crashLogDirectory);
                }
            },

            // Scratch Pad
            new() {
                Id = "scratch-pad",
                Name = "Scratch Pad",
                Description = "Open notes panel",
                Shortcut = "Ctrl+Shift+N",
                Icon = "📝",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 14),
                Execute = () => OpenScratchPadCommand.Execute(null)
            },

            // Statistics
            new() {
                Id = "statistics",
                Name = "Statistics",
                Description = "View usage statistics",
                Icon = "📊",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => OpenStatisticsCommand.Execute(null)
            },

            // Timeline Mode
            new() {
                Id = "timeline",
                Name = "Timeline Mode",
                Description = "Visual timeline of AI-assisted development sessions",
                Shortcut = "Ctrl+Shift+I",
                Icon = "⏱️",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 26),
                Execute = () => OpenTimelineCommand.Execute(null)
            },

            // GitHub Dashboard
            new() {
                Id = "dashboard",
                Name = "Dashboard",
                Description = "View GitHub PRs, issues, and CI status",
                Shortcut = "Ctrl+Shift+H",
                Icon = "🏠",
                Category = "GitHub",
                IntroducedOn = new DateOnly(2025, 12, 18),
                Execute = () => OpenDashboardCommand.Execute(null)
            },
            new() {
                Id = "pr-review",
                Name = "PR Review Mode",
                Description = "Review the current branch's pull request",
                Shortcut = "Ctrl+Shift+R",
                Icon = "📝",
                Category = "GitHub",
                IntroducedOn = new DateOnly(2025, 12, 18),
                Execute = () => PrReviewRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Markdown
            new() {
                Id = "markdown-preview",
                Name = "Markdown Preview",
                Description = "Preview markdown files",
                Shortcut = "Ctrl+M",
                Icon = "📄",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 18),
                Execute = () => MarkdownPreviewRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Git
            new() {
                Id = "git-changes",
                Name = "Git Changes",
                Description = "View modified files and diffs",
                Shortcut = "Alt+G",
                Icon = "📋",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => OpenUnifiedGitPanel(GitPanelTab.Changes),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-commit",
                Name = "Git Commit",
                Description = "Stage files, write message, and commit from the Changes panel (Alt+G)",
                Icon = "💾",
                Category = "Git",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => OpenUnifiedGitPanel(GitPanelTab.Changes),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-branches",
                Name = "Git Branches",
                Description = "Switch, create, or delete branches",
                Shortcut = "Ctrl+B",
                Icon = "🌿",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => OpenUnifiedGitPanel(GitPanelTab.Branches),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-history",
                Name = "Git History",
                Description = "View commit history",
                Shortcut = "Ctrl+H",
                Icon = "📜",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => OpenUnifiedGitPanel(GitPanelTab.History),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-stash",
                Name = "Git Stash",
                Description = "Manage stashed changes",
                Shortcut = "Ctrl+Shift+S",
                Icon = "📦",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => OpenUnifiedGitPanel(GitPanelTab.Stash),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-compare",
                Name = "Git Compare Branches",
                Description = "Compare two branches",
                Shortcut = "Ctrl+Alt+B",
                Icon = "🔀",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => OpenUnifiedGitPanel(GitPanelTab.Comparison),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Run commands
            new() {
                Id = "run-start",
                Name = "Run: Start",
                Description = "Start the project",
                Shortcut = "F5",
                Icon = "▶",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab && tab.CanRun) tab.StartRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { CanRun: true }
            },
            new() {
                Id = "run-stop",
                Name = "Run: Stop",
                Description = "Stop the running project",
                Shortcut = "Shift+F5",
                Icon = "⏹",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab && tab.CanStop) tab.StopRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { CanStop: true }
            },
            new() {
                Id = "run-restart",
                Name = "Run: Restart",
                Description = "Restart the running project",
                Icon = "🔄",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.RestartRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { RunState: RunState.Running }
            },
            new() {
                Id = "run-toggle-terminal",
                Name = "Run: Toggle Terminal",
                Description = "Show/hide run terminal panel",
                Icon = "📺",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.ToggleRunTerminalCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "run-open-url",
                Name = "Run: Open URL",
                Description = "Open detected localhost URL in browser",
                Icon = "🌐",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab && !string.IsNullOrEmpty(tab.DetectedRunUrl)) RunUrlDetectionService.OpenInBrowser(tab.DetectedRunUrl); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { HasDetectedRunUrl: true }
            },

            // Layout commands
            new() {
                Id = "toggle-layout-mode",
                Name = "Toggle Layout Mode",
                Description = "Switch between Tabs and Workspace Sidebar layout",
                Shortcut = "Ctrl+L",
                Icon = "📐",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () => ToggleLayoutModeCommand.Execute(null)
            },
            new() {
                Id = "toggle-sidebar",
                Name = "Toggle Sidebar",
                Description = "Collapse/expand the workspace sidebar",
                Shortcut = "Ctrl+Shift+L",
                Icon = "📎",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () => ToggleSidebarCommand.Execute(null),
                CanExecute = () => LayoutMode == AppLayoutMode.WorkspaceSidebar
            },
            new() {
                Id = "switch-to-tabs",
                Name = "Switch to Tabs Layout",
                Description = "Use traditional tab bar layout",
                Icon = "🗂",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () => { LayoutMode = AppLayoutMode.Tabs; var config = _configService.Load(); config.Settings.LayoutMode = LayoutMode; _configService.Save(config); },
                CanExecute = () => LayoutMode != AppLayoutMode.Tabs
            },
            new() {
                Id = "switch-to-sidebar",
                Name = "Switch to Sidebar Layout",
                Description = "Use workspace sidebar layout",
                Icon = "📂",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () => { LayoutMode = AppLayoutMode.WorkspaceSidebar; var config = _configService.Load(); config.Settings.LayoutMode = LayoutMode; _configService.Save(config); },
                CanExecute = () => LayoutMode != AppLayoutMode.WorkspaceSidebar
            },

            // Git operations
            new() {
                Id = "git-pull",
                Name = "Git Pull",
                NameProvider = () => {
                    var behind = (SelectedTab as TerminalPairTabViewModel)?.GitStatus?.BehindCount ?? 0;
                    return behind > 0 ? $"Git Pull (↓{behind})" : "Git Pull";
                },
                Description = "Pull with auto-stash and rebase",
                Shortcut = "Ctrl+Shift+D",
                Icon = "⬇",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.GitPullCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-push",
                Name = "Git Push",
                NameProvider = () => {
                    var ahead = (SelectedTab as TerminalPairTabViewModel)?.GitStatus?.AheadCount ?? 0;
                    return ahead > 0 ? $"Git Push (↑{ahead})" : "Git Push";
                },
                Description = "Push to remote",
                Shortcut = "Ctrl+Shift+U",
                Icon = "⬆",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.GitPushCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-reflog",
                Name = "Git Reflog",
                Description = "View reference log",
                Shortcut = "Ctrl+Shift+G",
                Icon = "📋",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => ReflogRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-repository-switcher",
                Name = "Switch Repository",
                Description = "Open repository switcher",
                Shortcut = "Ctrl+Shift+O",
                Icon = "🔄",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => RepositorySwitcherRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Panel/Tool toggles
            new() {
                Id = "file-explorer",
                Name = "File Explorer",
                Description = "Toggle file explorer panel",
                Shortcut = "Ctrl+Shift+F",
                Icon = "📁",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 22),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.ToggleExplorerCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "file-search",
                Name = "Search in Files",
                Description = "Search across files",
                Shortcut = "Ctrl+F3",
                Icon = "🔍",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 2, 7),
                Execute = () => SearchRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "claude-tasks",
                Name = "Claude Tasks",
                Description = "View Claude Code task activity",
                Shortcut = "Ctrl+Shift+K",
                Icon = "🤖",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 1, 27),
                Execute = () => ClaudeTasksRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "test-runner",
                Name = "Run Tests",
                Description = "Run project tests",
                Shortcut = "F6",
                Icon = "🧪",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 2, 5),
                Execute = () => TestRunnerRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Terminal layout modes
            new() {
                Id = "layout-custom-full",
                Name = "Layout: Custom Full",
                Description = "Show only custom terminal",
                Icon = "🖥",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.SetCustomFullLayoutCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "layout-horizontal-split",
                Name = "Layout: Horizontal Split",
                Description = "Side-by-side terminals",
                Icon = "⬜",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.SetHorizontalSplitLayoutCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "layout-vertical-split",
                Name = "Layout: Vertical Split",
                Description = "Top-bottom terminals",
                Icon = "⬛",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.SetVerticalSplitLayoutCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Settings toggles
            new() {
                Id = "toggle-sounds",
                Name = "Toggle Sounds",
                NameProvider = () => _configService.Load().Settings.Sounds.Enabled ? "Disable Sounds" : "Enable Sounds",
                Description = "Sound notifications",
                Icon = "🔊",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => {
                    var config = _configService.Load();
                    config.Settings.Sounds.Enabled = !config.Settings.Sounds.Enabled;
                    _configService.Save(config);
                    _toastService.Show(config.Settings.Sounds.Enabled ? "Sounds enabled" : "Sounds disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-touch-mode",
                Name = "Toggle Touch Mode",
                NameProvider = () => _configService.Load().Settings.TouchMode ? "Disable Touch Mode" : "Enable Touch Mode",
                Description = "Touch-friendly UI with larger targets",
                Icon = "👆",
                Category = "Settings",
                IntroducedOn = new DateOnly(2026, 1, 12),
                Execute = () => {
                    var config = _configService.Load();
                    config.Settings.TouchMode = !config.Settings.TouchMode;
                    _configService.Save(config);
                    _toastService.Show(config.Settings.TouchMode ? "Touch Mode enabled" : "Touch Mode disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-system-tray",
                Name = "Toggle System Tray",
                NameProvider = () => _configService.Load().Settings.ShowInSystemTray ? "Disable System Tray" : "Enable System Tray",
                Description = "System tray icon",
                Icon = "🔽",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => {
                    var config = _configService.Load();
                    config.Settings.ShowInSystemTray = !config.Settings.ShowInSystemTray;
                    _configService.Save(config);
                    _toastService.Show(config.Settings.ShowInSystemTray ? "System tray enabled" : "System tray disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-confirm-close",
                Name = "Toggle Confirm on Close",
                NameProvider = () => _configService.Load().Settings.ConfirmOnClose ? "Disable Confirm on Close" : "Enable Confirm on Close",
                Description = "Confirm before closing tabs",
                Icon = "⚠",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => {
                    var config = _configService.Load();
                    config.Settings.ConfirmOnClose = !config.Settings.ConfirmOnClose;
                    _configService.Save(config);
                    _toastService.Show(config.Settings.ConfirmOnClose ? "Close confirmation enabled" : "Close confirmation disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-git-auto-fetch",
                Name = "Toggle Git Auto-Fetch",
                NameProvider = () => _configService.Load().Settings.GitAutoFetch ? "Disable Git Auto-Fetch" : "Enable Git Auto-Fetch",
                Description = "Automatic fetch from remotes",
                Icon = "🔄",
                Category = "Settings",
                IntroducedOn = new DateOnly(2026, 1, 7),
                Execute = () => {
                    var config = _configService.Load();
                    config.Settings.GitAutoFetch = !config.Settings.GitAutoFetch;
                    _configService.Save(config);
                    _toastService.Show(config.Settings.GitAutoFetch ? "Git auto-fetch enabled" : "Git auto-fetch disabled", ToastType.Info);
                }
            },

            // What's New
            new() {
                Id = "whats-new",
                Name = "What's New",
                Description = "View recently added features",
                Shortcut = "Ctrl+F1",
                Icon = "✨",
                Category = "Help",
                IntroducedOn = new DateOnly(2026, 2, 10),
                Execute = () => WhatsNewRequested?.Invoke(this, EventArgs.Empty)
            },

            // AI Workflow Commands
            new() {
                Id = "ai-explain-blame",
                Name = "Explain blame line (AI) ✨",
                Description = "AI explains why a blame line was changed",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "explain-blame"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-summarize-file-history",
                Name = "Summarize file history (AI) ✨",
                Description = "AI summarizes a file's commit history",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "summarize-file-history"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-explain-commit",
                Name = "Explain commit (AI) ✨",
                Description = "AI explains what a commit does and why",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "explain-commit"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-explain-reflog",
                Name = "Explain recent git operations (AI) ✨",
                Description = "AI explains recent reflog entries",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "explain-reflog"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-generate-stash-name",
                Name = "Generate stash name (AI) ✨",
                Description = "AI generates a descriptive stash name",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "generate-stash-name"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-assess-merge-risk",
                Name = "Assess merge risk (AI) ✨",
                Description = "AI assesses risk of merging compared branches",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "assess-merge-risk"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-suggest-version",
                Name = "Suggest next version (AI) ✨",
                Description = "AI suggests next semantic version based on tags and commits",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "suggest-version"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-analyze-ci-failure",
                Name = "Analyze CI failure (AI) ✨",
                Description = "AI analyzes a failed CI check",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "analyze-ci-failure")
            },
            new() {
                Id = "ai-prioritize-prs",
                Name = "Prioritize PRs for review (AI) ✨",
                Description = "AI prioritizes open PRs by review urgency",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "prioritize-prs")
            },
            new() {
                Id = "ai-improve-markdown",
                Name = "Improve markdown (AI) ✨",
                Description = "AI suggests improvements to open markdown file",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "improve-markdown")
            },

            // Voice Commands
            new() {
                Id = "toggle-voice",
                Name = "Toggle Voice Commands",
                NameProvider = () => VoiceBar.IsVisible ? "Stop Voice Listening" : "Start Voice Listening",
                Description = "Control your terminal with voice (F4)",
                Shortcut = "F4",
                Icon = "🎙",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => ToggleVoiceListening()
            },
            new() {
                Id = "toggle-voice-enabled",
                Name = "Toggle Voice Commands Enabled",
                NameProvider = () => _configService.Load().Settings.Voice.Enabled ? "Disable Voice Commands" : "Enable Voice Commands",
                Description = "Enable or disable voice command feature",
                Icon = "🎙",
                Category = "Settings",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => {
                    var config = _configService.Load();
                    config.Settings.Voice.Enabled = !config.Settings.Voice.Enabled;
                    _configService.Save(config);
                    _toastService.Show(config.Settings.Voice.Enabled ? "Voice commands enabled" : "Voice commands disabled", ToastType.Info);
                }
            },

            // API commands
            new() {
                Id = "api-start",
                Name = "API: Start Server",
                Description = "Start the REST API server",
                Icon = "🌐",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _ = StartApiServerAsync(),
                CanExecute = () => _apiServer != null && !_apiServer.IsRunning
            },
            new() {
                Id = "api-stop",
                Name = "API: Stop Server",
                Description = "Stop the REST API server",
                Icon = "🌐",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _ = StopApiServerAsync(),
                CanExecute = () => _apiServer?.IsRunning == true
            },
            new() {
                Id = "api-copy-url",
                Name = "API: Copy Base URL",
                Description = "Copy the API base URL to clipboard",
                Icon = "📋",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => CopyApiUrl(),
                CanExecute = () => _apiServer?.IsRunning == true
            },
            new() {
                Id = "api-open-browser",
                Name = "API: Open in Browser",
                Description = "Open /api/status in default browser",
                Icon = "🔗",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => OpenApiInBrowser(),
                CanExecute = () => _apiServer?.IsRunning == true
            },
            new() {
                Id = "api-test-webhooks",
                Name = "API: Test Webhooks",
                Description = "Send a test event to all enabled webhooks",
                Icon = "🧪",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _ = TestWebhooksAsync()
            },
            new() {
                Id = "api-stats",
                Name = "API: Show Delivery Stats",
                Description = "Show webhook delivery statistics",
                Icon = "📊",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => ShowWebhookStats()
            }
        ];
    }

    /// <summary>
    /// Build voice command grammar from palette commands and quick commands.
    /// Curated aliases map common speech phrases to command IDs.
    /// </summary>
    private void InitializeVoiceGrammar()
    {
        if (_voiceCommandService is null) return;

        var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["new-project"] = ["open project", "new project"],
            ["close-tab"] = ["close tab"],
            ["switch-terminal"] = ["switch terminal", "toggle terminal"],
            ["settings"] = ["settings", "open settings"],
            ["command-palette"] = ["command palette", "commands"],
            ["git-changes"] = ["git changes", "git status", "show changes"],
            ["git-branches"] = ["branches", "switch branch"],
            ["git-history"] = ["commit history", "git log"],
            ["git-stash"] = ["git stash", "stash"],
            ["file-explorer"] = ["file explorer", "files"],
            ["scratch-pad"] = ["scratch pad", "notes"],
            ["help"] = ["help", "what can I say"],
            ["dashboard"] = ["dashboard"],
            ["pr-review"] = ["review PR", "PR review"],
            ["run-start"] = ["run", "start project"],
            ["run-stop"] = ["stop", "stop project"],
            ["timeline"] = ["timeline"],
            ["file-search"] = ["search", "find in files"],
            ["markdown-preview"] = ["markdown preview"],
            ["whats-new"] = ["what's new", "recent features"],
            ["toggle-voice"] = ["voice commands", "toggle voice", "stop listening"]
        };

        var entries = new List<VoiceCommandEntry>();

        foreach (var cmd in _allPaletteCommands)
        {
            aliases.TryGetValue(cmd.Id, out var cmdAliases);
            entries.Add(new VoiceCommandEntry
            {
                CommandId = cmd.Id,
                DisplayName = cmd.Name,
                Shortcut = cmd.Shortcut,
                PrimaryPhrase = cmd.Name.ToLowerInvariant(),
                Aliases = cmdAliases ?? [],
                Execute = cmd.Execute,
                Category = cmd.Category
            });
        }

        // Add quick commands
        var config = _configService.Load();
        foreach (var qc in config.QuickCommands)
        {
            entries.Add(new VoiceCommandEntry
            {
                CommandId = $"qc-{qc.Id}",
                DisplayName = qc.Label,
                Shortcut = qc.Shortcut,
                PrimaryPhrase = qc.Label.ToLowerInvariant(),
                Aliases = [],
                Execute = () => ExecuteQuickCommandCommand.Execute(qc),
                Category = "Quick Command"
            });
        }

        _voiceCommandService.UpdateGrammar(entries);
    }

    private void FilterPaletteCommands()
    {
        _filteredPaletteCommands.Clear();
        var searchText = PaletteSearchText?.ToLower() ?? "";
        var allCommands = new List<PaletteCommand>();

        // Get static commands
        var filtered = _allPaletteCommands
            .Where(c => c.CanExecute == null || c.CanExecute()) // Evaluate CanExecute on the spot
            .Where(c =>
                string.IsNullOrEmpty(searchText) ||
                c.Name.ToLower().Contains(searchText) ||
                (c.Description?.ToLower().Contains(searchText) ?? false) ||
                c.Category.ToLower().Contains(searchText))
            .ToList();

        allCommands.AddRange(filtered);

        // Add dynamic profile launch commands
        foreach (var profile in _profileRegistry.Profiles)
        {
            var profileName = $"Launch: {profile.Name}";
            var matchesSearch = string.IsNullOrEmpty(searchText) ||
                               profileName.ToLower().Contains(searchText) ||
                               "profile".Contains(searchText) ||
                               "launch".Contains(searchText);

            if (matchesSearch)
            {
                var capturedProfile = profile; // Capture for closure
                allCommands.Add(new PaletteCommand
                {
                    Id = $"launch-profile-{profile.Id}",
                    Name = profileName,
                    Description = profile.Command,
                    Shortcut = profile.Shortcut ?? "",
                    Icon = profile.Icon ?? "▶",
                    Category = "Profile",
                    Execute = () => OpenProfileTab(capturedProfile)
                });
            }
        }

        // Add Claude commands (from ~/.claude/commands/, .claude/commands/, and plugins)
        var currentWorkingDir = (SelectedTab as TerminalPairTabViewModel)?.Pair.WorkingDirectory;
        var claudeCommands = _claudeCommandService.GetAllCommands(currentWorkingDir);

        foreach (var cmd in claudeCommands)
        {
            var commandName = $"Claude: /{cmd.FullName}";
            var matchesSearch = string.IsNullOrEmpty(searchText) ||
                               commandName.ToLower().Contains(searchText) ||
                               (cmd.Description?.ToLower().Contains(searchText) ?? false) ||
                               (cmd.PluginName?.ToLower().Contains(searchText) ?? false) ||
                               "claude".Contains(searchText) ||
                               "plugin".Contains(searchText);

            if (matchesSearch)
            {
                var capturedCmd = cmd; // Capture for closure
                var category = cmd.Source switch
                {
                    ClaudeCommandSource.Global => "Claude (Global)",
                    ClaudeCommandSource.Project => "Claude (Project)",
                    ClaudeCommandSource.Plugin => $"Claude (Plugin: {cmd.PluginName})",
                    _ => "Claude"
                };

                allCommands.Add(new PaletteCommand
                {
                    Id = $"claude-cmd-{cmd.Id}",
                    Name = commandName,
                    Description = cmd.Description ?? cmd.FilePath,
                    Shortcut = cmd.Shortcut ?? "",
                    Icon = "🤖",
                    Category = category,
                    Execute = () => ExecuteClaudeCommand(capturedCmd)
                });
            }
        }

        // Sort by MRU (most recently used first), then alphabetically
        var mruList = _configService.Load().CommandPaletteMru;
        var sortedCommands = allCommands
            .OrderBy(c =>
            {
                var mruIndex = mruList.IndexOf(c.Id);
                return mruIndex >= 0 ? mruIndex : int.MaxValue;
            })
            .ThenBy(c => c.Name)
            .ToList();

        foreach (var command in sortedCommands)
        {
            _filteredPaletteCommands.Add(command);
        }

        if (FilteredPaletteCommands.Any())
        {
            SelectedPaletteCommand = FilteredPaletteCommands.First();
        }
        else
        {
            SelectedPaletteCommand = null;
        }
    }

    [RelayCommand]
    private void ExecuteSelectedPaletteCommand()
    {
        if (SelectedPaletteCommand != null)
        {
            // Track MRU before closing
            UpdateCommandMru(SelectedPaletteCommand.Id);

            IsCommandPaletteOpen = false;
            SelectedPaletteCommand.Execute();
        }
    }

    private void UpdateCommandMru(string commandId)
    {
        var config = _configService.Load();

        // Remove if already exists (will be re-added at front)
        config.CommandPaletteMru.Remove(commandId);

        // Add to front
        config.CommandPaletteMru.Insert(0, commandId);

        // Limit to 30 most recent
        if (config.CommandPaletteMru.Count > 30)
        {
            config.CommandPaletteMru.RemoveRange(30, config.CommandPaletteMru.Count - 30);
        }

        _configService.Save(config);
    }

    private static string GetCrashLogDirectoryPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TerminalHost", "logs");
    }

    /// <summary>
    /// Executes a Claude command by sending the slash command to the Custom terminal.
    /// </summary>
    public void ExecuteClaudeCommand(ClaudeCommand command)
    {
        if (SelectedTab is not TerminalPairTabViewModel tab)
            return;

        // Switch to Custom terminal
        tab.ShowCustomTerminalCommand.Execute(null);

        // Send the slash command to Claude Code (use FullName for plugin commands)
        tab.Pair.CustomTerminal.SendText(
            $"/{command.FullName}",
            appendNewline: true,
            newlineChar: "\r",
            useUserInput: true  // Important for Claude Code to properly receive the command
        );

        // Focus the terminal
        tab.Pair.CustomTerminal.Focus();
    }

    /// <summary>
    /// Gets all Claude commands for the current project (used by MainWindow for keyboard shortcuts).
    /// </summary>
    public IReadOnlyList<ClaudeCommand> GetClaudeCommandsForCurrentProject()
    {
        var currentWorkingDir = (SelectedTab as TerminalPairTabViewModel)?.Pair.WorkingDirectory;
        return _claudeCommandService.GetAllCommands(currentWorkingDir);
    }

    /// <summary>
    /// Handles the OpenTabRequested event from the workspace sidebar.
    /// </summary>
    private void OnWorkspaceSidebarOpenTabRequested(object? sender, string path)
    {
        OpenProjectTab(path);

        // Focus the terminal after tab selection
        // Use Dispatcher to ensure UI has updated before focusing
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () =>
            {
                if (SelectedTab is TerminalPairTabViewModel terminalTab)
                {
                    terminalTab.FocusActiveTerminal();
                }
            });
    }

    /// <summary>
    /// Handles the OpenProjectRequested event from the timeline service.
    /// </summary>
    private void OnTimelineOpenProjectRequested(object? sender, (string WorktreePath, string? InitialPrompt) args)
    {
        // Open the project tab for the intent's worktree
        OpenProjectTab(args.WorktreePath);

        // Focus the terminal after tab selection
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () =>
            {
                if (SelectedTab is TerminalPairTabViewModel terminalTab)
                {
                    terminalTab.FocusActiveTerminal();
                    // TODO: If there's an initial prompt, we could send it to the terminal after focus
                }
            });
    }

    /// <summary>
    /// Handles the DuplicateTabRequested event from the workspace sidebar.
    /// </summary>
    private void OnWorkspaceSidebarDuplicateTabRequested(object? sender, string path)
    {
        OpenProjectTab(path, forceNew: true);

        // Focus the terminal after tab selection
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () =>
            {
                if (SelectedTab is TerminalPairTabViewModel terminalTab)
                {
                    terminalTab.FocusActiveTerminal();
                }
            });
    }

    /// <summary>
    /// Handles the CloseTabRequested event from the workspace sidebar.
    /// </summary>
    private void OnWorkspaceSidebarCloseTabRequested(object? sender, string path)
    {
        var tab = Tabs.OfType<TerminalPairTabViewModel>()
            .FirstOrDefault(t => string.Equals(t.Pair.WorkingDirectory, path, StringComparison.OrdinalIgnoreCase));

        if (tab != null)
        {
            CloseTabCommand.Execute(tab);
        }
    }

    /// <summary>
    /// Handles the GitStatusRefreshed event from the workspace sidebar.
    /// Updates the corresponding tab's git status to keep sidebar and tab in sync.
    /// </summary>
    private async void OnWorkspaceSidebarGitStatusRefreshed(object? sender, string path)
    {
        var tab = Tabs.OfType<TerminalPairTabViewModel>()
            .FirstOrDefault(t => string.Equals(t.Pair.WorkingDirectory, path, StringComparison.OrdinalIgnoreCase));

        if (tab != null)
        {
            try
            {
                var status = await _gitStatusService.GetGitStatusAsync(path);
                tab.GitStatus = status;
            }
            catch
            {
                // Silently ignore git status errors
            }
        }
    }

    /// <summary>
    /// Toggle voice listening on/off (F4 shortcut).
    /// </summary>
    public void ToggleVoiceListening()
    {
        var settings = _configService.Load().Settings.Voice;
        if (!settings.Enabled)
        {
            _toastService.Show("Voice commands are disabled. Enable them in Settings → Voice.", ToastType.Info);
            return;
        }
        if (_voiceCommandService is null || !_voiceCommandService.IsAvailable)
        {
            _toastService.Show("Voice commands are not available on this system.", ToastType.Warning);
            return;
        }

        if (VoiceBar.IsVisible)
            VoiceBar.Cancel();
        else
            VoiceBar.StartListening();
    }

    /// <summary>
    /// Handles the SendToAiRequested event from the voice bar.
    /// Sends unmatched voice transcript to the active custom terminal.
    /// </summary>
    private void OnVoiceSendToAi(object? sender, string text)
    {
        if (SelectedTab is not TerminalPairTabViewModel tab) return;

        tab.Pair.CustomTerminal.SendText(text, appendNewline: false, newlineChar: "\r", useUserInput: true);
        tab.Pair.CustomTerminal.Focus();
    }

    /// <summary>
    /// Toggles between Tabs and WorkspaceSidebar layout modes.
    /// </summary>
    [RelayCommand]
    public void ToggleLayoutMode()
    {
        LayoutMode = LayoutMode == AppLayoutMode.Tabs
            ? AppLayoutMode.WorkspaceSidebar
            : AppLayoutMode.Tabs;

        // Save the setting
        var config = _configService.Load();
        config.Settings.LayoutMode = LayoutMode;
        _configService.Save(config);
    }

    /// <summary>
    /// Toggles the workspace sidebar visibility (collapse/expand).
    /// </summary>
    [RelayCommand]
    public void ToggleSidebar()
    {
        if (WorkspaceSidebar != null)
        {
            WorkspaceSidebar.IsCollapsed = !WorkspaceSidebar.IsCollapsed;
            OnPropertyChanged(nameof(IsWorkspaceSidebarVisible));
            OnPropertyChanged(nameof(SidebarColumnWidth));
            OnPropertyChanged(nameof(SidebarSplitterWidth));
        }
    }

    /// <summary>
    /// Initializes the workspace sidebar with saved workspaces.
    /// Call this during application startup.
    /// </summary>
    public async Task InitializeWorkspaceSidebarAsync()
    {
        var config = _configService.Load();
        LayoutMode = config.Settings.LayoutMode;

        if (WorkspaceSidebar != null)
        {
            await WorkspaceSidebar.LoadAsync();
        }
    }

    public void Shutdown()
    {
        // Stop timers
        _gitStatusTimer.Stop();
        _gitAutoFetchTimer.Stop();
        _activityTimer.Stop();
        _linkDetectionTimer.Stop();
        _runUrlDetectionTimer.Stop();

        // Save final focus time for the currently active tab
        if (_tabFocusStartTime.HasValue && !string.IsNullOrEmpty(_focusedTabDirectory))
        {
            var elapsed = (int)(DateTime.Now - _tabFocusStartTime.Value).TotalSeconds;
            if (elapsed > 0)
            {
                _statisticsService.RecordFocusTime(_focusedTabDirectory, elapsed);
            }
        }

        // Save open folders before closing
        SaveOpenFolders();

        _sessionManager.CloseAllSessions();
        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.Pair.Dispose();
        }
    }

    #region API Server Helpers

    private async Task StartApiServerAsync()
    {
        if (_apiServer == null) return;
        try
        {
            await _apiServer.StartAsync();
            _toastService.Show($"API server started at {_apiServer.BaseUrl}", ToastType.Success);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to start API server: {ex.Message}", ToastType.Error);
        }
    }

    private async Task StopApiServerAsync()
    {
        if (_apiServer == null) return;
        try
        {
            await _apiServer.StopAsync();
            _toastService.Show("API server stopped", ToastType.Info);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to stop API server: {ex.Message}", ToastType.Error);
        }
    }

    private void CopyApiUrl()
    {
        if (_apiServer?.BaseUrl != null)
        {
            try
            {
                System.Windows.Clipboard.SetText(_apiServer.BaseUrl);
                _toastService.Show($"Copied: {_apiServer.BaseUrl}", ToastType.Success);
            }
            catch { }
        }
    }

    private void OpenApiInBrowser()
    {
        if (_apiServer?.BaseUrl != null)
        {
            _processService.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"{_apiServer.BaseUrl}/api/status",
                UseShellExecute = true
            });
        }
    }

    private async Task TestWebhooksAsync()
    {
        if (_webhookDeliveryService == null) return;
        try
        {
            await _webhookDeliveryService.TestWebhooksAsync();
            _toastService.Show("Test events sent to all enabled webhooks", ToastType.Success);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Webhook test failed: {ex.Message}", ToastType.Error);
        }
    }

    private void ShowWebhookStats()
    {
        if (_webhookDeliveryService == null) return;

        var stats = _webhookDeliveryService.GetStats();
        var msg = $"Delivered: {stats.TotalDelivered} | Failed: {stats.TotalFailed} | Pending retries: {stats.PendingRetries}";
        _toastService.Show(msg, ToastType.Info);
    }

    private List<ApiRepoInfo> BuildRepoList()
    {
        var repos = new List<ApiRepoInfo>();
        var terminalTabs = Tabs.OfType<TerminalPairTabViewModel>().ToList();

        for (var i = 0; i < terminalTabs.Count; i++)
        {
            var tab = terminalTabs[i];
            repos.Add(new ApiRepoInfo
            {
                Index = i,
                Title = tab.Title,
                WorkingDirectory = tab.Pair.WorkingDirectory,
                IsActive = tab == SelectedTab,
                Layout = tab.LayoutMode.ToString(),
                SplitRatio = tab.SplitRatio,
                ActiveTerminal = tab.ActiveTerminal.ToString(),
                Git = tab.GitStatus != null ? new ApiGitInfo
                {
                    Branch = tab.GitStatus.BranchName,
                    IsDirty = tab.GitStatus.IsDirty,
                    Ahead = tab.GitStatus.AheadCount,
                    Behind = tab.GitStatus.BehindCount,
                    StashCount = tab.GitStatus.StashCount
                } : null,
                Terminals = new ApiTerminalsInfo
                {
                    Custom = new ApiTerminalInfo
                    {
                        Title = tab.CustomTerminalTitle ?? "",
                        IsActive = tab.ActiveTerminal == ActiveTerminal.Custom,
                        IsBusy = tab.Pair.CustomTerminal.IsActive,
                        LastActivityAt = tab.Pair.CustomTerminal.LastOutputTime?.ToUniversalTime(),
                    },
                    Shell = new ApiTerminalInfo
                    {
                        Title = tab.ShellTerminalTitle ?? "",
                        IsActive = tab.ActiveTerminal == ActiveTerminal.Shell,
                        IsBusy = tab.Pair.ShellTerminal.IsActive,
                        LastActivityAt = tab.Pair.ShellTerminal.LastOutputTime?.ToUniversalTime(),
                    },
                    Run = tab.Pair.RunTerminal != null ? new ApiTerminalInfo
                    {
                        Title = "Run",
                        IsActive = tab.ActiveTerminal == ActiveTerminal.Run,
                        IsBusy = tab.Pair.RunTerminal.IsActive,
                        LastActivityAt = tab.Pair.RunTerminal.LastOutputTime?.ToUniversalTime(),
                    } : null
                },
                ActivityIndicator = new ApiActivityIndicator
                {
                    State = tab.IsAnyTerminalActive ? "busy"
                        : tab.IsWaitingForInput ? "waiting"
                        : tab.HasUnreadActivity ? "done"
                        : "idle",
                    HasUnreadActivity = tab.HasUnreadActivity,
                    IsWaitingForInput = tab.IsWaitingForInput,
                }
            });
        }

        return repos;
    }

    private ApiRepoDetailInfo? BuildRepoDetail(int index)
    {
        var terminalTabs = Tabs.OfType<TerminalPairTabViewModel>().ToList();
        if (index < 0 || index >= terminalTabs.Count) return null;

        var tab = terminalTabs[index];
        var basic = BuildRepoList()[index];

        return new ApiRepoDetailInfo
        {
            Index = basic.Index,
            Title = basic.Title,
            WorkingDirectory = basic.WorkingDirectory,
            IsActive = basic.IsActive,
            Layout = basic.Layout,
            SplitRatio = basic.SplitRatio,
            ActiveTerminal = basic.ActiveTerminal,
            Git = basic.Git,
            Terminals = basic.Terminals,
            AiAssistant = tab.ActiveAiAssistant != null ? new ApiAiAssistantInfo
            {
                Id = tab.ActiveAiAssistant.Id,
                Name = tab.ActiveAiAssistant.Name,
                Icon = tab.ActiveAiAssistant.DisplayLabel
            } : null
        };
    }

    private List<ApiWorkspaceInfo> BuildWorkspaceList()
    {
        var config = _configService.Load();
        var openRepos = BuildRepoList();

        return config.Workspaces.Select(w =>
        {
            var normalizedPath = w.Path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
            var matchingRepo = openRepos.FirstOrDefault(r =>
                r.WorkingDirectory.Replace('\\', '/').TrimEnd('/').ToLowerInvariant() == normalizedPath);

            return new ApiWorkspaceInfo
            {
                Id = w.Id,
                Name = w.Name,
                Path = w.Path,
                PathId = ApiServer.NormalizePathId(w.Path),
                Section = w.Section,
                IsPinned = w.IsPinned,
                Order = w.Order,
                CustomIcon = w.CustomIcon,
                IsOpen = matchingRepo != null,
                RepoIndex = matchingRepo?.Index,
                ActivityIndicator = matchingRepo?.ActivityIndicator,
                Terminals = matchingRepo?.Terminals,
            };
        }).ToList();
    }

    /// <summary>
    /// Publishes an API event if the event aggregator is available.
    /// </summary>
    private void PublishApiEvent(string type, int? repoIndex = null, object? data = null)
    {
        _eventAggregator?.Publish(new ApiEvent
        {
            Type = type,
            RepoIndex = repoIndex,
            Data = data
        });
    }

    #endregion
}

public class RunTerminalRequestedEventArgs : EventArgs
{
    public required TerminalPairTabViewModel Tab { get; init; }
    public required RunConfiguration Configuration { get; init; }
    public bool IsStop { get; init; }
}

public class CenterPanelRestoreEventArgs : EventArgs
{
    public required TerminalPairTabViewModel Tab { get; init; }
    public required string PanelId { get; init; }
    public string? GitPanelActiveTab { get; init; }

    /// <summary>
    /// When true, only associate the panel with the tab (set ActiveCenterPanel)
    /// without loading data. Used for non-selected tabs during startup to avoid
    /// race conditions with singleton panel ViewModels.
    /// </summary>
    public bool SkipDataLoad { get; init; }
}

public class RightPanelRestoreEventArgs : EventArgs
{
    public required TerminalPairTabViewModel Tab { get; init; }
    public required List<string> PanelIds { get; init; }
    public string? ActivePanelId { get; init; }
}
