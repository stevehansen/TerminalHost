using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
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
        ITaskService? taskService = null)
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

        // Subscribe to timeline events
        _timelineService.OpenProjectRequested += OnTimelineOpenProjectRequested;

        // Initialize workspace sidebar
        WorkspaceSidebar = _viewModelFactory.CreateWorkspaceSidebar();
        WorkspaceSidebar.OpenTabRequested += OnWorkspaceSidebarOpenTabRequested;
        WorkspaceSidebar.DuplicateTabRequested += OnWorkspaceSidebarDuplicateTabRequested;
        WorkspaceSidebar.CloseTabRequested += OnWorkspaceSidebarCloseTabRequested;
        WorkspaceSidebar.GitStatusRefreshed += OnWorkspaceSidebarGitStatusRefreshed;

        // Initialize help view model
        HelpViewModel = new HelpViewModel(this);

        // Initialize touch mode from config
        TouchMode = configService.Load().Settings.TouchMode;

        // Subscribe to Tabs collection changes for NonProjectTabs updates
        _tabs.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(NonProjectTabs));
            OnPropertyChanged(nameof(HasNonProjectTabs));
        };

        // Subscribe to Claude command changes (dispatch to UI thread since FileSystemWatcher raises events on thread pool)
        _claudeCommandService.CommandsChanged += (_, _) => _dispatcherService.BeginInvoke(FilterPaletteCommands);

        FilteredDropdownTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredDropdownTabs);
        UpdateFilteredDropdownTabs(); // Initial population

        FilteredSwitcherTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredSwitcherTabs);
        UpdateFilteredSwitcherTabs(); // Initial population

        FilteredPaletteCommands = new ReadOnlyObservableCollection<PaletteCommand>(_filteredPaletteCommands);
        InitializeCommandPalette(); // Initialize commands once

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
            var status = await _gitStatusService.GetGitStatusAsync(terminalTab.Pair.WorkingDirectory);
            terminalTab.GitStatus = status;
            // Update window title when git status changes
            OnPropertyChanged(nameof(WindowTitle));

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

        foreach (var folder in config.OpenFolders)
        {
            if (_fileSystem.DirectoryExists(folder))
            {
                OpenProjectTab(folder);
            }
        }

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
            var tabViewModel = new TerminalPairTabViewModel(pair, aiAssistant, enabledAssistants, settings.ShellCommandIcon, _statisticsService, duplicateIndex, _taskService);
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
                Execute = () => OpenNewProjectCommand.Execute(null)
            },
            new() {
                Id = "close-tab",
                Name = "Close Tab",
                Description = "Close current tab",
                Shortcut = "Ctrl+W",
                Icon = "✕",
                Category = "Tab",
                Execute = () => { if (SelectedTab != null) CloseTabCommand.Execute(SelectedTab); }
            },
            new() {
                Id = "tab-switcher",
                Name = "Switch Tab",
                Description = "Search and switch tabs",
                Shortcut = "Ctrl+Shift+T",
                Icon = "🔍",
                Category = "Tab",
                Execute = () => { IsTabSwitcherOpen = true; SwitcherSearchText = ""; }
            },
            new() {
                Id = "duplicate-tab",
                Name = "Duplicate Tab",
                Description = "Open new tab for same directory",
                Icon = "📋",
                Category = "Tab",
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
                Execute = () => FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = "", Line = 0, Column = 0}) // Needs to be improved
            },
            new() {
                Id = "file-edit",
                Name = "Edit File",
                Description = "Open file in editor",
                Shortcut = "Ctrl+Shift+E",
                Icon = "✏️",
                Category = "File",
                Execute = () => { /* Needs to be improved */ }
            },
            new() {
                Id = "open-explorer",
                Name = "Open in Explorer",
                Description = "Open folder in file explorer",
                Shortcut = "Ctrl+E",
                Icon = "📂",
                Category = "File",
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
                Execute = () => OpenSettingsCommand.Execute(null)
            },
            new() {
                Id = "profiles",
                Name = "Settings: Profiles",
                Description = "Open settings and manage terminal profiles",
                Shortcut = "Ctrl+P",
                Icon = "👤",
                Category = "Settings",
                Execute = () => OpenProfilesCommand.Execute(null)
            },
            new() {
                Id = "setup",
                Name = "Setup",
                Description = "Check dependencies and setup",
                Icon = "🔧",
                Category = "Settings",
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
                Execute = () => IsHelpOpen = true
            },

            // Scratch Pad
            new() {
                Id = "scratch-pad",
                Name = "Scratch Pad",
                Description = "Open notes panel",
                Shortcut = "Ctrl+Shift+N",
                Icon = "📝",
                Category = "Tools",
                Execute = () => OpenScratchPadCommand.Execute(null)
            },

            // Statistics
            new() {
                Id = "statistics",
                Name = "Statistics",
                Description = "View usage statistics",
                Icon = "📊",
                Category = "Tools",
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
                Execute = () => OpenDashboardCommand.Execute(null)
            },
            new() {
                Id = "pr-review",
                Name = "PR Review Mode",
                Description = "Review the current branch's pull request",
                Shortcut = "Ctrl+Shift+R",
                Icon = "📝",
                Category = "GitHub",
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
                Execute = () => MarkdownPreviewRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Git
            new() {
                Id = "git-changes",
                Name = "Git Changes",
                Description = "View modified files and diffs",
                Shortcut = "Ctrl+G",
                Icon = "📋",
                Category = "Git",
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
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab && tab.CanStop) tab.StopRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { CanStop: true }
            },
            new() {
                Id = "run-restart",
                Name = "Run: Restart",
                Description = "Restart the running project",
                Icon = "🔄",
                Category = "Run",
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.RestartRunCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel { RunState: RunState.Running }
            },
            new() {
                Id = "run-toggle-terminal",
                Name = "Run: Toggle Terminal",
                Description = "Show/hide run terminal panel",
                Icon = "📺",
                Category = "Run",
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) tab.ToggleRunTerminalCommand.Execute(null); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "run-open-url",
                Name = "Run: Open URL",
                Description = "Open detected localhost URL in browser",
                Icon = "🌐",
                Category = "Run",
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
                Execute = () => ToggleLayoutModeCommand.Execute(null)
            },
            new() {
                Id = "toggle-sidebar",
                Name = "Toggle Sidebar",
                Description = "Collapse/expand the workspace sidebar",
                Shortcut = "Ctrl+Shift+L",
                Icon = "📎",
                Category = "Layout",
                Execute = () => ToggleSidebarCommand.Execute(null),
                CanExecute = () => LayoutMode == AppLayoutMode.WorkspaceSidebar
            },
            new() {
                Id = "switch-to-tabs",
                Name = "Switch to Tabs Layout",
                Description = "Use traditional tab bar layout",
                Icon = "🗂",
                Category = "Layout",
                Execute = () => { LayoutMode = AppLayoutMode.Tabs; var config = _configService.Load(); config.Settings.LayoutMode = LayoutMode; _configService.Save(config); },
                CanExecute = () => LayoutMode != AppLayoutMode.Tabs
            },
            new() {
                Id = "switch-to-sidebar",
                Name = "Switch to Sidebar Layout",
                Description = "Use workspace sidebar layout",
                Icon = "📂",
                Category = "Layout",
                Execute = () => { LayoutMode = AppLayoutMode.WorkspaceSidebar; var config = _configService.Load(); config.Settings.LayoutMode = LayoutMode; _configService.Save(config); },
                CanExecute = () => LayoutMode != AppLayoutMode.WorkspaceSidebar
            }
        ];
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
}

public class RunTerminalRequestedEventArgs : EventArgs
{
    public required TerminalPairTabViewModel Tab { get; init; }
    public required RunConfiguration Configuration { get; init; }
    public bool IsStop { get; init; }
}
