using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;
using TerminalHost.Core.Workspace;
using TerminalHost.Domain;
using TerminalHost.Services;
using ITimerService = TerminalHost.Services.ITimerService;

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
    private readonly IFileExplorerService _fileExplorerService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IFileEditService _fileEditService;
    private readonly IClaudeCommandService _claudeCommandService;
    private readonly ITaskService _taskService;
    private readonly IAiAssistantService _aiAssistantService;
    private readonly IGitHubService _gitHubService;
    private readonly IMarkdownService _markdownService;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private readonly IClipboardService _clipboardService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFilePickerService _filePickerService;
    private readonly ITimerService _timerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ITimelineService _timelineService;
    private readonly IClaudeTaskDetectionService? _claudeTaskDetectionService;
    private readonly ITaskAggregator? _taskAggregator;
    private readonly IApiServer? _apiServer;
    private readonly ISessionActivityService? _sessionActivityService;
    private readonly IEventAggregatorService? _eventAggregator;
    private readonly IWebhookDeliveryService? _webhookDeliveryService;
    private readonly IAiExecutionService? _aiExecutionService;
    private readonly IInputPromptDetectionService _inputPromptDetectionService;
    private readonly ISoundService? _soundService;
    private readonly StatusOverlayService? _statusOverlayService;
    private readonly IContainerService? _containerService;
    private readonly IVoiceCommandService? _voiceCommandService;
    private readonly Core.Interfaces.ITimerService _coreTimerService;
    private readonly TabRouter _router;

    private readonly IPlatformTimer _gitStatusTimer;
    private readonly IPlatformTimer _gitAutoFetchTimer;
    private readonly IPlatformTimer _activityTimer;
    private readonly IPlatformTimer _linkDetectionTimer;
    private readonly IPlatformTimer _runUrlDetectionTimer;

    // Cached git tracking mode to avoid config loads on every timer tick
    private GitTrackingMode _gitTrackingMode;

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
    /// Voice command floating bar ViewModel.
    /// </summary>
    public VoiceBarViewModel VoiceBar { get; private set; } = null!;

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
    /// Built-in keyboard shortcuts for the Help view, with platform-appropriate display text.
    /// Sourced from ShortcutConflictService (single source of truth).
    /// </summary>
    public List<HelpDisplaySection> HelpShortcutSections { get; } = BuildHelpSections();

    /// <summary>
    /// Quick command shortcuts for the Help view.
    /// </summary>
    public List<HelpDisplaySection> HelpQuickCommandSections { get; } =
    [
        new("Default Quick Commands (configurable in Settings)",
        [
            new(FormatShortcut("Ctrl+Shift+C"), "Send 'commit' to Claude Code"),
            new(FormatShortcut("Ctrl+Shift+D"), "Run 'git pull --rebase' in Shell"),
            new(FormatShortcut("Ctrl+Shift+U"), "Run 'git push' in Shell"),
        ]),
    ];

    /// <summary>
    /// Command line examples for the Help view.
    /// </summary>
    public List<HelpCommandLineExample> HelpCommandLineExamples { get; } =
    [
        new("host", "Open/focus app"),
        new("host .", "Open project from current directory"),
        new("host ~/MyProject", "Open specific project"),
        new("host -w ~/Path", "Using named argument"),
        new("host -multi", "Allow multiple instances"),
        new("host -data path", "Override config path"),
    ];

    /// <summary>
    /// Config path for the Help view.
    /// </summary>
    public string HelpConfigPath => OperatingSystem.IsMacOS()
        ? "~/Library/Application Support/TerminalHost/config.json"
        : "~/.config/TerminalHost/config.json";

    private static List<HelpDisplaySection> BuildHelpSections()
    {
        var isMac = OperatingSystem.IsMacOS();
        return ShortcutConflictService.GetSectionsForPlatform(isMac)
            .Select(s => new HelpDisplaySection(
                s.Name,
                s.GetItemsForPlatform(isMac)
                    .Select(i => new HelpDisplayItem(i.GetDisplayShortcut(isMac), i.Description))
                    .ToList()))
            .Where(s => s.Items.Count > 0)
            .ToList();
    }

    private static string FormatShortcut(string shortcut)
    {
        if (OperatingSystem.IsMacOS())
        {
            return shortcut.Replace("Ctrl+", "⌘");
        }
        return shortcut;
    }

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

    private ICommandPalette _palette = null!;
    private ObservableCollection<PaletteCommand> _filteredPaletteCommands = [];
    public ReadOnlyObservableCollection<PaletteCommand> FilteredPaletteCommands { get; }

    [ObservableProperty]
    private PaletteCommand? _selectedPaletteCommand;

    // Task Panel
    public TaskPanelViewModel? TaskPanelViewModel { get; set; }

    // Claude Tasks Panel
    public ClaudeTasksPanelViewModel? ClaudeTasksPanelViewModel { get; set; }

    public SessionsTreePanelViewModel? SessionsTreePanelViewModel { get; set; }

    public MemoryBrowserViewModel? MemoryBrowserViewModel { get; set; }

    public DebugLogViewModel? DebugLogViewModel { get; set; }

    // Quick Capture
    [ObservableProperty]
    private bool _isQuickTaskOpen;

    [ObservableProperty]
    private string _quickTaskTitle = string.Empty;

    [ObservableProperty]
    private bool _isQuickNoteOpen;

    [ObservableProperty]
    private string _quickNoteText = string.Empty;

    // Layout Mode (Tabs vs Sidebar)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarMode))]
    [NotifyPropertyChangedFor(nameof(IsTabsMode))]
    [NotifyPropertyChangedFor(nameof(SidebarColumnWidth))]
    [NotifyPropertyChangedFor(nameof(SidebarSplitterWidth))]
    [NotifyPropertyChangedFor(nameof(TabStripRowHeight))]
    private AppLayoutMode _layoutMode = AppLayoutMode.Tabs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarColumnWidth))]
    private double _sidebarWidth = 250;

    /// <summary>
    /// The workspace sidebar view model.
    /// </summary>
    public WorkspaceSidebarViewModel? SidebarViewModel { get; set; }

    /// <summary>
    /// Whether the application is in sidebar layout mode.
    /// </summary>
    public bool IsSidebarMode => LayoutMode == AppLayoutMode.WorkspaceSidebar;

    /// <summary>
    /// Whether the application is in tabs layout mode.
    /// </summary>
    public bool IsTabsMode => LayoutMode == AppLayoutMode.Tabs;

    /// <summary>
    /// Width of the sidebar column (0 when hidden).
    /// </summary>
    public Avalonia.Controls.GridLength SidebarColumnWidth => IsSidebarMode
        ? new Avalonia.Controls.GridLength(SidebarWidth, Avalonia.Controls.GridUnitType.Pixel)
        : new Avalonia.Controls.GridLength(0, Avalonia.Controls.GridUnitType.Pixel);

    /// <summary>
    /// Width of the sidebar splitter (0 when hidden).
    /// </summary>
    public Avalonia.Controls.GridLength SidebarSplitterWidth => IsSidebarMode
        ? new Avalonia.Controls.GridLength(4, Avalonia.Controls.GridUnitType.Pixel)
        : new Avalonia.Controls.GridLength(0, Avalonia.Controls.GridUnitType.Pixel);

    /// <summary>
    /// Height of the tab strip row (Auto when visible, 0 when hidden).
    /// </summary>
    public Avalonia.Controls.GridLength TabStripRowHeight => IsTabsMode
        ? new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Auto)
        : new Avalonia.Controls.GridLength(0, Avalonia.Controls.GridUnitType.Pixel);

    public event EventHandler? ConfigReloaded;
    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileViewerRequestedEventArgs>? FilePopOutRequested;
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
            else if (SelectedTab is SettingsTabViewModel)
            {
                return "Settings - TerminalHost";
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
        IFileExplorerService fileExplorerService,
        IFilePreviewService filePreviewService,
        IFileEditService fileEditService,
        IClaudeCommandService claudeCommandService,
        ITaskService taskService,
        IAiAssistantService aiAssistantService,
        IGitHubService gitHubService,
        IMarkdownService markdownService,
        IProcessService processService,
        IToastService toastService,
        IClipboardService clipboardService,
        IFolderPickerService folderPickerService,
        IFilePickerService filePickerService,
        ITimerService timerService,
        IDispatcherService dispatcherService,
        ITimelineService timelineService,
        IInputPromptDetectionService inputPromptDetectionService,
        IClaudeTaskDetectionService? claudeTaskDetectionService = null,
        ITaskAggregator? taskAggregator = null,
        IApiServer? apiServer = null,
        ISessionActivityService? sessionActivityService = null,
        IEventAggregatorService? eventAggregator = null,
        IWebhookDeliveryService? webhookDeliveryService = null,
        IAiExecutionService? aiExecutionService = null,
        ISoundService? soundService = null,
        StatusOverlayService? statusOverlayService = null,
        IContainerService? containerService = null,
        IVoiceCommandService? voiceCommandService = null,
        Core.Interfaces.ITimerService? coreTimerService = null)
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
        _fileExplorerService = fileExplorerService;
        _filePreviewService = filePreviewService;
        _fileEditService = fileEditService;
        _claudeCommandService = claudeCommandService;
        _taskService = taskService;
        _aiAssistantService = aiAssistantService;
        _gitHubService = gitHubService;
        _markdownService = markdownService;
        _processService = processService;
        _toastService = toastService;
        _clipboardService = clipboardService;
        _folderPickerService = folderPickerService;
        _filePickerService = filePickerService;
        _timerService = timerService;
        _dispatcherService = dispatcherService;
        _timelineService = timelineService;
        _inputPromptDetectionService = inputPromptDetectionService;
        _claudeTaskDetectionService = claudeTaskDetectionService;
        _taskAggregator = taskAggregator;
        _apiServer = apiServer;
        _sessionActivityService = sessionActivityService;
        _eventAggregator = eventAggregator;
        _webhookDeliveryService = webhookDeliveryService;
        _aiExecutionService = aiExecutionService;
        _soundService = soundService;
        _statusOverlayService = statusOverlayService;
        _containerService = containerService;
        _voiceCommandService = voiceCommandService;
        _coreTimerService = coreTimerService ?? throw new ArgumentNullException(nameof(coreTimerService));

        // Initialize voice command bar
        VoiceBar = new VoiceBarViewModel(_coreTimerService);
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

        // Wire up API server state delegates
        if (_apiServer is ApiServer concreteServer)
        {
            concreteServer.SetRepoStateProvider(
                () => BuildRepoList(),
                (index) => BuildRepoDetail(index));
            concreteServer.SetWorkspaceStateProvider(
                () => BuildWorkspaceList());
        }

        // Subscribe to focus mode changes
        _taskService.FocusModeChanged += (_, _) => UpdateTabFocusModeVisibility();
        _taskService.CurrentTaskChanged += (_, _) => UpdateTabFocusModeVisibility();

        // Subscribe to Claude command changes (dispatch to UI thread since FileSystemWatcher raises events on thread pool)
        _claudeCommandService.CommandsChanged += (_, _) => _dispatcherService.BeginInvoke(FilterPaletteCommands);

        _router = new TabRouter(_tabs, tab => SelectedTab = tab);
        _router.Register<SettingsTabViewModel>(
            factory: () => new SettingsTabViewModel(_configService, _dialogService, _toastService, _processService, _clipboardService, _containerService,
                App.Current.Services.GetService<TerminalHost.Core.Interfaces.IEidetService>()),
            onCreated: tab =>
            {
                tab.CloseRequested += OnTabCloseRequested;
                tab.ConfigSaved += OnConfigSaved;
            });
        _router.Register<DashboardTabViewModel>(
            factory: () => new DashboardTabViewModel(_gitHubService, _configService, this, _dialogService, _fileSystem, _processService, _toastService, _timerService, _folderPickerService, _aiExecutionService!, _clipboardService),
            onCreated: tab =>
            {
                tab.CloseRequested += OnTabCloseRequested;
                tab.PrReviewRequested += OnDashboardPrReviewRequested;
            });
        _router.Register<StatisticsTabViewModel>(
            factory: () => new StatisticsTabViewModel(_statisticsService),
            onCreated: tab => tab.CloseRequested += OnTabCloseRequested);
        _router.Register<TimelineTabViewModel>(
            factory: () => new TimelineTabViewModel(_timelineService, _dialogService, _folderPickerService, _timerService),
            onCreated: tab => tab.CloseRequested += OnTabCloseRequested);

        // Initialize touch mode from config
        TouchMode = configService.Load().Settings.TouchMode;

        FilteredDropdownTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredDropdownTabs);
        UpdateFilteredDropdownTabs(); // Initial population

        // Keep sidebar sorted tabs in sync with any tab add/remove/move
        Tabs.CollectionChanged += (_, _) => SidebarViewModel?.RefreshSortedTabs();

        FilteredSwitcherTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredSwitcherTabs);
        UpdateFilteredSwitcherTabs(); // Initial population

        FilteredPaletteCommands = new ReadOnlyObservableCollection<PaletteCommand>(_filteredPaletteCommands);
        var commandContext = new CommandContext(
            activeTab: () => SelectedTab,
            serviceLocator: t => App.Current?.Services?.GetService(t));
        _palette = new CommandPalette(
            providers: new ICommandProvider[] { new MainViewModelStaticCommandProvider(this) },
            context: commandContext);
        InitializeVoiceGrammar();   // Build voice grammar from palette commands

        // Set up timer for periodic git status refresh (every 5 seconds)
        _gitStatusTimer = _timerService.CreateTimer(
            TimeSpan.FromSeconds(5),
            async () => await RefreshSelectedTabGitStatusAsync());

        // Set up timer for git auto-fetch (configurable interval, default 60 seconds)
        var fetchInterval = Math.Max(30, _configService.Load().Settings.GitAutoFetchIntervalSeconds);
        _gitAutoFetchTimer = _timerService.CreateTimer(
            TimeSpan.FromSeconds(fetchInterval),
            async () => await AutoFetchAllAsync());

        // Set up timer for activity state refresh (every 1 second to detect idle transitions)
        _activityTimer = _timerService.CreateTimer(
            TimeSpan.FromSeconds(1),
            RefreshActivityState);

        // Set up timer for link detection refresh (every 3 seconds)
        _linkDetectionTimer = _timerService.CreateTimer(
            TimeSpan.FromSeconds(3),
            RefreshDetectedLinks);

        // Set up timer for run URL detection (every 2 seconds, only when running)
        _runUrlDetectionTimer = _timerService.CreateTimer(
            TimeSpan.FromSeconds(2),
            RefreshRunUrlDetection);
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

            // Lazy initialization: create terminal controls when tab is first selected
            if (!newValue.IsTerminalInitialized)
            {
                _ = InitializeTabTerminalsAsync(newValue);
            }

            // Refresh worktrees in sidebar when tab changes
            if (IsSidebarMode)
            {
                _ = SidebarViewModel?.RefreshWorktreesAsync();
            }

            // Update Claude Tasks panel workspace when tab changes
            if (newValue is TerminalPairTabViewModel terminalTab && ClaudeTasksPanelViewModel != null)
            {
                ClaudeTasksPanelViewModel.SetWorkspace(terminalTab.WorkingDirectory);
                // Refresh if panel is open and filtering by workspace
                if (ClaudeTasksPanelViewModel.IsOpen && !ClaudeTasksPanelViewModel.ShowGlobalTasks)
                {
                    ClaudeTasksPanelViewModel.RefreshTasks();
                }
            }

            // Lazy-init file explorer for tabs that were deferred during startup restore
            if (newValue is TerminalPairTabViewModel newTerminalTab && newTerminalTab.DeferredExplorerInit != null)
            {
                var init = newTerminalTab.DeferredExplorerInit;
                newTerminalTab.DeferredExplorerInit = null;
                _ = init();
            }

            // Start tracking focus time for the new tab
            if (newValue is TerminalPairTabViewModel focusTrackTab)
            {
                _tabFocusStartTime = DateTime.Now;
                _focusedTabDirectory = focusTrackTab.Pair.WorkingDirectory;
            }
            else
            {
                _tabFocusStartTime = null;
                _focusedTabDirectory = null;
            }

            // Focus the custom (AI) terminal when switching to a workspace tab
            if (newValue is TerminalPairTabViewModel focusTab && focusTab.IsTerminalInitialized)
            {
                // BeginInvoke so the view has time to render before we focus
                _dispatcherService.BeginInvoke(() => focusTab.Pair.CustomTerminal.Focus());
            }

            // Publish API event for tab activation
            if (newValue is TerminalPairTabViewModel activatedTab)
            {
                var tabIndex = Tabs.OfType<TerminalPairTabViewModel>().ToList().IndexOf(activatedTab);
                var previousIndex = oldValue is TerminalPairTabViewModel oldTerminal
                    ? Tabs.OfType<TerminalPairTabViewModel>().ToList().IndexOf(oldTerminal) : -1;
                PublishApiEvent("repo.activated", tabIndex, new
                {
                    workingDirectory = activatedTab.WorkingDirectory,
                    title = activatedTab.Title,
                    previousIndex
                });
            }
        }
    }

    /// <summary>
    /// Initializes terminal controls for a tab. Called on first selection (lazy initialization).
    /// </summary>
    private async Task InitializeTabTerminalsAsync(ITabViewModel tab)
    {
        await tab.InitializeTerminalsAsync();

        // Track sessions after initialization (for terminal tabs)
        if (tab is TerminalPairTabViewModel terminalTab)
        {
            _sessionManager.TrackSession(terminalTab.Pair.CustomTerminal);
            _sessionManager.TrackSession(terminalTab.Pair.ShellTerminal);

            // Track run terminal if it was initialized (due to IsRunTerminalVisible being restored)
            if (terminalTab.Pair.RunTerminal != null)
            {
                _sessionManager.TrackSession(terminalTab.Pair.RunTerminal);
            }
        }
        else if (tab is ProfileTerminalTabViewModel profileTab)
        {
            _sessionManager.TrackSession(profileTab.Session);
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

    /// <summary>
    /// Updates visibility of all tabs based on focus mode state.
    /// Called when focus mode is toggled or current task changes.
    /// </summary>
    private void UpdateTabFocusModeVisibility()
    {
        var isFocusModeEnabled = _taskService.IsFocusModeEnabled;
        var currentTaskProjects = _taskService.GetProjectsForCurrentTask();

        foreach (var tab in Tabs)
        {
            tab.UpdateFocusModeVisibility(isFocusModeEnabled, currentTaskProjects);
        }

        // If the selected tab is now hidden, try to select a visible one
        if (SelectedTab != null && !SelectedTab.IsVisibleInFocusMode)
        {
            var visibleTab = Tabs.FirstOrDefault(t => t.IsVisibleInFocusMode);
            if (visibleTab != null)
            {
                SelectedTab = visibleTab;
            }
        }
    }

    public void Initialize()
    {
        // Load quick commands from config
        LoadQuickCommands();

        // Load layout mode from config
        LoadLayoutSettings();

        // Initialize sidebar view model
        SidebarViewModel?.Initialize();

        // Restore previously open folders
        RestoreOpenFolders();

        // Cache git tracking mode
        _gitTrackingMode = _configService.Load().Settings.GitTrackingMode;

        // Start git status refresh timer (unless tracking is fully disabled)
        if (_gitTrackingMode != GitTrackingMode.Disabled)
        {
            _gitStatusTimer.Start();
        }

        // Start git auto-fetch timer (only in All mode + if enabled)
        if (_gitTrackingMode == GitTrackingMode.All && _configService.Load().Settings.GitAutoFetch)
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

    private void LoadLayoutSettings()
    {
        var config = _configService.Load();
        LayoutMode = config.Settings.LayoutMode;
        SidebarWidth = config.Settings.SidebarWidth;
    }

    private void LoadQuickCommands()
    {
        var config = _configService.Load();
        QuickCommands = new ObservableCollection<QuickCommand>(config.QuickCommands);
    }

    private async Task RefreshSelectedTabGitStatusAsync()
    {
        if (_gitTrackingMode == GitTrackingMode.Disabled) return;
        if (SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        try
        {
            var previousBranch = terminalTab.GitStatus?.BranchName;
            var workDir = terminalTab.Pair.WorkingDirectory;
            var status = await Task.Run(() => _gitStatusService.GetGitStatusAsync(workDir));
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
            SidebarViewModel?.UpdateContainerState(tab.Pair.WorkingDirectory, tab.IsContainerized);
        }

        // Also update profile terminal tabs
        foreach (var tab in Tabs.OfType<ProfileTerminalTabViewModel>())
        {
            tab.UpdateActivityState();
        }
    }

    /// <summary>
    /// Automatically fetches from git remotes for all open projects.
    /// This runs periodically to keep behind counts up to date.
    /// Runs in batches of 5 to avoid flooding the UI thread.
    /// </summary>
    private async Task AutoFetchAllAsync()
    {
        // Respect git tracking mode — skip entirely if not in All mode
        if (_gitTrackingMode != GitTrackingMode.All) return;

        const int batchSize = 5;
        var tabs = Tabs.OfType<TerminalPairTabViewModel>().ToList();

        for (var i = 0; i < tabs.Count; i += batchSize)
        {
            var batch = tabs.Skip(i).Take(batchSize).ToList();
            await Task.WhenAll(batch.Select(async tab =>
            {
                try
                {
                    await Task.Run(async () =>
                    {
                        await _gitStatusService.FetchAllAsync(tab.Pair.WorkingDirectory);
                    });
                }
                catch
                {
                    // Silently ignore fetch errors (network issues, etc.)
                }
            }));

            // Refresh git status for the batch after fetch completes
            foreach (var tab in batch)
            {
                try
                {
                    var status = await Task.Run(() => _gitStatusService.GetGitStatusAsync(tab.Pair.WorkingDirectory));
                    tab.GitStatus = status;
                }
                catch { }
            }
        }
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

        // Restore dashboard if it was open
        if (config.Settings.Dashboard.ShowOnStartup && config.Settings.Dashboard.Enabled)
        {
            _ = OpenDashboardAsync();
        }

        // Defer center panel restores until the correct SelectedTab is set.
        // Without this, multiple tabs fire async restores for singleton panel VMs
        // and the last one to complete wins — which may not be the selected tab.
        _deferredCenterPanelRestores = [];

        foreach (var folder in config.OpenFolders)
        {
            if (_fileSystem.DirectoryExists(folder))
            {
                // Don't select tabs during restore - lazy initialization will happen when user clicks
                OpenProjectTab(folder, selectTab: false);
            }
        }

        // Capture and stop deferring
        var pendingRestores = _deferredCenterPanelRestores;
        _deferredCenterPanelRestores = null;

        // Restore the last selected tab (this is the only one that will be initialized on startup)
        if (!string.IsNullOrEmpty(config.LastSelectedFolder))
        {
            var tabToSelect = Tabs.OfType<TerminalPairTabViewModel>()
                .FirstOrDefault(t => t.Pair.WorkingDirectory.Equals(config.LastSelectedFolder, StringComparison.OrdinalIgnoreCase));
            if (tabToSelect != null)
            {
                SelectedTab = tabToSelect;
            }
        }
        else if (Tabs.Count > 0)
        {
            // If no last selected folder, select the first tab
            SelectedTab = Tabs[0];
        }

        // Now fire deferred center panel restores.
        // Non-selected tabs only get ActiveCenterPanel set (no data load) to avoid
        // async races overwriting the selected tab's data in singleton panel VMs.
        // Data loads on demand when the user switches tabs (via tab-switch rebinding).
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

        // Save the currently selected tab (if it's a project tab)
        if (SelectedTab is TerminalPairTabViewModel selectedProjectTab)
        {
            config.LastSelectedFolder = selectedProjectTab.Pair.WorkingDirectory;
        }
        else
        {
            // Keep the previous selection or clear it
            config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
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

        // Update explorer settings
        settings.IsExplorerVisible = tab.IsExplorerVisible;
        settings.ExplorerSplitRatio = tab.ExplorerSplitRatio;

        // Update center panel state
        settings.ActiveCenterPanel = tab.ActiveCenterPanel?.PanelId;

        // Save git panel active tab if the git panel is the center panel
        if (tab.ActiveCenterPanel is UnifiedGitPanelViewModel gitPanel)
        {
            settings.GitPanelActiveTab = gitPanel.ActiveTab.ToString();
        }

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
            var path = _folderPickerService.PickFolder("Select Project Directory");
            if (!string.IsNullOrEmpty(path))
            {
                OpenProjectTab(path);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Error opening project: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleLayoutMode()
    {
        LayoutMode = LayoutMode == AppLayoutMode.Tabs
            ? AppLayoutMode.WorkspaceSidebar
            : AppLayoutMode.Tabs;

        // Save preference
        var config = _configService.Load();
        config.Settings.LayoutMode = LayoutMode;
        config.Settings.SidebarWidth = SidebarWidth;
        _configService.Save(config);

        // Refresh worktrees when switching to sidebar mode
        if (IsSidebarMode)
        {
            _ = SidebarViewModel?.RefreshWorktreesAsync();
        }
    }

    /// <summary>
    /// Updates the sidebar width when the splitter is dragged.
    /// </summary>
    public void UpdateSidebarWidth(double width)
    {
        SidebarWidth = Math.Max(150, Math.Min(width, 500)); // Clamp between 150-500px

        // Save preference
        var config = _configService.Load();
        config.Settings.SidebarWidth = SidebarWidth;
        _configService.Save(config);
    }

    public async void OpenProjectTab(string workingDirectory, bool selectTab = true, bool forceNew = false)
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

            if (!forceNew)
            {
                // Check if we already have a tab open for this directory
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

            var settings = _profileRegistry.Settings;

            // Calculate duplicate index for display title
            var duplicateIndex = GetDuplicateTabIndex(workingDirectory);

            // Get the AI assistant for this directory
            var aiAssistant = _aiAssistantService.GetAssistantForDirectory(workingDirectory);
            var enabledAssistants = _aiAssistantService.GetEnabledAssistants();

            // Create profiles for custom command and shell
            // The custom terminal runs the shell first, then starts the AI CLI as a startup command.
            // This allows the user to exit and restart the AI CLI without losing the terminal.
            var customProfile = new Profile
            {
                Id = "custom",
                Name = aiAssistant.Name,
                Command = settings.ShellCommand,  // Start with the shell
                StartupCommand = aiAssistant.Command,  // Then launch the AI CLI
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

            // Set up container if enabled for this workspace
            string? containerName = null;
            if (_containerService != null && _containerService.IsEnabledForDirectory(workingDirectory))
            {
                try
                {
                    containerName = _containerService.GetContainerName(workingDirectory);
                    customProfile.ContainerName = containerName;
                    shellProfile.ContainerName = containerName;

                    // Ensure container is running before terminals try to docker exec.
                    // Call the lightweight EnsureContainerRunningAsync (no dialogs) on a
                    // background thread to avoid UI deadlock, then run dialog-based checks async.
                    Task.Run(() => _containerService.EnsureContainerRunningAsync(workingDirectory)).GetAwaiter().GetResult();

                    // Fire-and-forget: staleness checks and dialog prompts (non-blocking)
                    _ = EnsureContainerForWorkspaceAsync(workingDirectory);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Container setup failed: {ex.Message}");
                    _toastService.Show($"Container setup failed: {ex.Message}", ToastType.Warning);
                    // Fall back to non-containerized
                    customProfile.ContainerName = null;
                    shellProfile.ContainerName = null;
                }
            }

            // Create the terminal pair
            var pair = new TerminalPair(workingDirectory, customProfile, shellProfile, _statisticsService, _clipboardService);

            // Create view model with AI assistant info (terminals created lazily on first selection)
            var tabViewModel = new TerminalPairTabViewModel(pair, aiAssistant, enabledAssistants, settings.ShellCommandIcon, _statisticsService, _terminalFactory, _claudeTaskDetectionService, _timelineService, _taskService, _taskAggregator, _dispatcherService, _gitStatusService, _toastService, duplicateIndex);
            tabViewModel.IsContainerized = containerName != null;
            SidebarViewModel?.UpdateContainerState(workingDirectory, containerName != null);
            tabViewModel.AiAssistantSwitchRequested += OnAiAssistantSwitchRequested;
            tabViewModel.ShellProfileSwitchRequested += OnShellProfileSwitchRequested;
            tabViewModel.CloseRequested += OnTabCloseRequested;
            tabViewModel.SettingsChanged += OnTabSettingsChanged;
            tabViewModel.TaskPanelRequested += (s, e) => OpenTaskPanel();
            tabViewModel.ClaudeTasksPanelRequested += (s, e) => OpenClaudeTasksPanel();
            tabViewModel.SessionsTreePanel = SessionsTreePanelViewModel;

            // Initialize available shell profiles
            tabViewModel.RefreshAvailableShellProfiles(_profileRegistry.Profiles);

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

            // Note: Sessions are tracked in InitializeTabTerminalsAsync when terminals are created

            // Subscribe to link click events
            pair.CustomTerminal.LinkClicked += (s, text) => HandleLinkClick(text, workingDirectory);
            pair.ShellTerminal.LinkClicked += (s, text) => HandleLinkClick(text, workingDirectory);

            // Subscribe to run terminal events
            tabViewModel.RunStartRequested += OnRunStartRequested;
            tabViewModel.RunStopRequested += OnRunStopRequested;

            // Initialize file explorer
            var explorerViewModel = new FileExplorerViewModel(_fileExplorerService, _gitStatusService, _dialogService, _fileSystem, _processService, _dispatcherService, _clipboardService, _toastService);
            tabViewModel.ExplorerViewModel = explorerViewModel;

            // Restore explorer settings
            if (dirSettings != null)
            {
                tabViewModel.IsExplorerVisible = dirSettings.IsExplorerVisible;
                tabViewModel.ExplorerSplitRatio = dirSettings.ExplorerSplitRatio;
            }

            // Wire up explorer events
            explorerViewModel.CdToShellRequested += (s, path) => tabViewModel.SendCdToShell(path);
            explorerViewModel.FileViewerRequested += OnExplorerFileViewerRequested;
            explorerViewModel.PopOutRequested += OnExplorerPopOutRequested;
            explorerViewModel.RenameRequested += OnExplorerRenameRequested;
            explorerViewModel.FileHistoryRequested += OnExplorerFileHistoryRequested;
            explorerViewModel.FileBlameRequested += OnExplorerFileBlameRequested;

            // Initialize explorer async — during restore, defer to avoid flooding
            // the dispatcher with concurrent directory scans + git status checks.
            // Non-selected tabs will be initialized lazily when first selected.
            if (selectTab)
            {
                _ = explorerViewModel.InitializeAsync(workingDirectory);
            }
            else
            {
                tabViewModel.DeferredExplorerInit = () => explorerViewModel.InitializeAsync(workingDirectory);
            }

            Tabs.Add(tabViewModel);

            // Publish API event for repo opened
            PublishApiEvent("repo.opened", data: new { workingDirectory, title = tabViewModel.Title });

            // Only select the tab if requested (false during startup restore for lazy init)
            if (selectTab)
            {
                SelectedTab = tabViewModel;
            }

            // Track in recent folders
            UpdateRecentFolders(workingDirectory);

            // Track workspace for sidebar
            if (SidebarViewModel != null)
            {
                _ = SidebarViewModel.TrackWorkspaceOpenedAsync(workingDirectory);
            }

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

            // Fetch git status for the new tab
            _ = RefreshTabGitStatusAsync(tabViewModel);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Error creating terminal: {ex.Message}"); // Use injected IDialogService
        }
    }

    /// <summary>
    /// Opens a new tab with a single terminal running the specified profile.
    /// </summary>
    /// <param name="profile">The profile to launch.</param>
    /// <param name="workingDirectory">Optional working directory. If null, uses the profile's WorkingDir.</param>
    public async void OpenProfileTab(Profile profile, string? workingDirectory = null)
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

            // Create view model (terminal created lazily on first selection)
            var tabViewModel = new ProfileTerminalTabViewModel(profileWithDir, effectiveWorkingDir, _statisticsService, _clipboardService, _terminalFactory);

            // Subscribe to events
            tabViewModel.CloseRequested += OnTabCloseRequested;

            // Note: Session is tracked in InitializeTabTerminalsAsync when terminal is created

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
            // Set initial directory to profile's configured directory if it exists
            var initialDir = profile.GetExpandedWorkingDir();
            if (string.IsNullOrWhiteSpace(initialDir) || !_fileSystem.DirectoryExists(initialDir))
            {
                initialDir = null;
            }

            var path = _folderPickerService.PickFolder(
                $"Select Working Directory for {profile.Name}",
                initialDir);

            if (!string.IsNullOrEmpty(path))
            {
                OpenProfileTab(profile, path);
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

        // Publish API event for repo closed
        if (tab is TerminalPairTabViewModel closedTerminalTab)
        {
            PublishApiEvent("repo.closed", data: new { workingDirectory = closedTerminalTab.WorkingDirectory, title = closedTerminalTab.Title });
        }

        Tabs.Remove(tab);

        if (SelectedTab == tab && Tabs.Count > 0)
        {
            SelectedTab = Tabs[^1];
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

    private async void OnAiAssistantSwitchRequested(object? sender, AiAssistantSwitchEventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            // Save the new AI selection
            _aiAssistantService.SetAssistantForDirectory(tab.WorkingDirectory, e.NewAssistant.Id);

            // Create new profile for the new AI assistant
            // Uses shell with startup command so user can exit and restart the AI CLI
            var settings = _profileRegistry.Settings;
            var newProfile = new Profile
            {
                Id = "custom",
                Name = e.NewAssistant.Name,
                Command = settings.ShellCommand,  // Start with the shell
                StartupCommand = e.NewAssistant.Command,  // Then launch the AI CLI
                WorkingDir = tab.WorkingDirectory,
                Icon = e.NewAssistant.Icon
            };

            // Close old custom terminal session
            var oldSession = tab.Pair.CustomTerminal;
            _sessionManager.CloseSession(oldSession);

            // Create new session and control
            var newSession = new TerminalSession(newProfile, _statisticsService, _clipboardService, "Custom");
            var newControl = await _terminalFactory.CreateTerminalControlAsync(newSession);
            _sessionManager.TrackSession(newSession);

            // Subscribe to link click events
            newSession.LinkClicked += (s, text) => HandleLinkClick(text, tab.WorkingDirectory);

            // Replace the terminal in the pair
            tab.Pair.ReplaceCustomTerminal(newSession);
            tab.SetCustomTerminalControl(newControl);
            tab.UpdateActiveAiAssistant(e.NewAssistant);
        }
    }

    private async void OnShellProfileSwitchRequested(object? sender, Profile newProfile)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            // Create the shell profile with the selected profile's command
            var shellProfile = new Profile
            {
                Id = "shell",
                Name = newProfile.Name,
                Command = newProfile.Command,
                WorkingDir = tab.WorkingDirectory,
                Icon = newProfile.Icon
            };

            // Close old shell terminal session
            var oldSession = tab.Pair.ShellTerminal;
            _sessionManager.CloseSession(oldSession);

            // Create new session and control
            var newSession = new TerminalSession(shellProfile, _statisticsService, _clipboardService, "Shell");
            var newControl = await _terminalFactory.CreateTerminalControlAsync(newSession);
            _sessionManager.TrackSession(newSession);

            // Subscribe to link click events
            newSession.LinkClicked += (s, text) => HandleLinkClick(text, tab.WorkingDirectory);

            // Replace the terminal in the pair
            tab.Pair.ReplaceShellTerminal(newSession);
            tab.SetShellTerminalControl(newControl);
            tab.UpdateActiveShellProfile(newProfile);
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
        // Forward the pop-out request to MainWindow which will create the actual window
        // (ViewModels should not create windows directly - that's the View's responsibility)
        FilePopOutRequested?.Invoke(this, e);
    }

    private void OnExplorerFileHistoryRequested(object? sender, string filePath)
    {
        // Get the working directory from the current tab
        var workingDirectory = (SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory ?? "";
        FileHistoryRequested?.Invoke(this, new FileHistoryRequestedEventArgs
        {
            WorkingDirectory = workingDirectory,
            FilePath = filePath
        });
    }

    private void OnExplorerFileBlameRequested(object? sender, string filePath)
    {
        // Get the working directory from the current tab
        var workingDirectory = (SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory ?? "";
        FileBlameRequested?.Invoke(this, new FileBlameRequestedEventArgs
        {
            WorkingDirectory = workingDirectory,
            FilePath = filePath
        });
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

    private void InitializeRunConfigurations(TerminalPairTabViewModel tab, string workingDirectory, DirectorySettings? dirSettings)
    {
        List<RunConfiguration> configs;
        string? activeConfigId;

        if (dirSettings != null && dirSettings.RunConfigurations.Count > 0)
        {
            // Use saved configurations
            configs = dirSettings.RunConfigurations;
            activeConfigId = dirSettings.ActiveRunConfigurationId;
        }
        else
        {
            // Auto-detect project type and create configurations
            // Use existing dirSettings or create new one - GetOrCreateConfigurations will set ActiveRunConfigurationId
            var settings = dirSettings ?? new DirectorySettings();
            configs = _projectDetectionService.GetOrCreateConfigurations(workingDirectory, settings);
            activeConfigId = settings.ActiveRunConfigurationId;
        }

        tab.InitializeRunConfigurations(configs, activeConfigId);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _router.OpenSingleton<SettingsTabViewModel>();
    }

    [RelayCommand]
    private void OpenProfiles()
    {
        _router.OpenSingleton<SettingsTabViewModel>(tab => tab.SelectedSection = SettingsSection.Profiles);
    }

    [RelayCommand]
    private async Task OpenDashboardAsync()
    {
        var firstOpen = !_router.IsOpen<DashboardTabViewModel>();
        var dashboardTab = _router.OpenSingleton<DashboardTabViewModel>();
        if (!firstOpen) return;

        var config = _configService.Load();
        config.Settings.Dashboard.ShowOnStartup = true;
        _configService.Save(config);

        await dashboardTab.InitializeAsync();
    }

    [RelayCommand]
    private void OpenSparkCanvas()
    {
        if (SelectedTab is TerminalPairTabViewModel tab)
        {
            var vm = new SparkCanvasViewModel(
                activityService: _sessionActivityService,
                apiServer: _apiServer,
                timelineService: _timelineService,
                configService: _configService);

            // Auto-select the session matching this tab's working directory
            var tabDir = tab.WorkingDirectory;
            var matchingSession = _timelineService.GetLiveSessions()
                .FirstOrDefault(s => s.IsActive && string.Equals(s.WorkingDirectory, tabDir, StringComparison.OrdinalIgnoreCase))
                ?? _timelineService.GetLiveSessions()
                    .FirstOrDefault(s => string.Equals(s.WorkingDirectory, tabDir, StringComparison.OrdinalIgnoreCase));
            if (matchingSession != null)
                vm.OpenSession(matchingSession.ClaudeSessionId);

            tab.ShowCenterPanel(vm);
        }
    }

    private void OpenSparkCanvasAndLoadJsonl()
    {
        if (SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            _toastService.Show("Select a project tab first", ToastType.Warning);
            return;
        }

        var vm = new SparkCanvasViewModel(
            activityService: _sessionActivityService,
            apiServer: _apiServer,
            timelineService: _timelineService,
            configService: _configService);
        terminalTab.ShowCenterPanel(vm);

        // Trigger the file open dialog via the ViewModel's event
        vm.OpenJsonlFileCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenTimeline()
    {
        _router.OpenSingleton<TimelineTabViewModel>();
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
            var firstOpen = !_router.IsOpen<StatisticsTabViewModel>();
            var statsTab = _router.OpenSingleton<StatisticsTabViewModel>();
            if (!firstOpen)
            {
                statsTab.LoadStatsCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"An error occurred while opening the statistics view:\n\n{ex.Message}");
        }
    }

    private void OnConfigSaved(object? sender, EventArgs e)
    {
        // Reload settings from profile registry
        _profileRegistry.Reload();

        // Reload quick commands when config is saved
        LoadQuickCommands();

        // Reload touch mode setting and adjust sidebar width
        var newTouchMode = _configService.Load().Settings.TouchMode;
        if (newTouchMode != TouchMode)
        {
            TouchMode = newTouchMode;
            // Adjust sidebar width for touch mode (narrower for more content space)
            SidebarWidth = newTouchMode ? 180 : 250;
            OnPropertyChanged(nameof(SidebarColumnWidth));
        }

        // Reload AI assistants and update all terminal tabs
        _aiAssistantService.Reload();
        var enabledAssistants = _aiAssistantService.GetEnabledAssistants();
        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.RefreshAvailableAiAssistants(enabledAssistants);
        }

        // Refresh run configurations for all terminal tabs
        RefreshAllRunConfigurations();

        // Apply status overlay settings to existing overlays
        _statusOverlayService?.ApplySettings();

        // Refresh sidebar sort mode
        var config = _configService.Load();
        if (SidebarViewModel != null)
        {
            SidebarViewModel.SortMode = config.Settings.WorkspaceSortMode;
        }

        // Refresh git tracking mode
        var newTrackingMode = config.Settings.GitTrackingMode;
        if (newTrackingMode != _gitTrackingMode)
        {
            _gitTrackingMode = newTrackingMode;

            if (_gitTrackingMode == GitTrackingMode.Disabled)
            {
                _gitStatusTimer.Stop();
                _gitAutoFetchTimer.Stop();
            }
            else
            {
                _gitStatusTimer.Start();
                if (_gitTrackingMode == GitTrackingMode.All && config.Settings.GitAutoFetch)
                    _gitAutoFetchTimer.Start();
                else
                    _gitAutoFetchTimer.Stop();
            }
        }

        // Refresh sound settings
        if (_soundService is TerminalHost.Posix.Services.PosixSoundServiceBase soundService)
        {
            soundService.RefreshCachedSettings(config.Settings.Sounds);
        }

        // Apply Eidet memory settings live (connect/disconnect/reconnect with new URL)
        var eidet = App.Current.Services.GetService<TerminalHost.Core.Interfaces.IEidetService>();
        if (eidet != null)
            _ = Task.Run(() => eidet.OnSettingsChangedAsync());

        // Notify that config has been reloaded (for system tray, etc.)
        ConfigReloaded?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshAllRunConfigurations()
    {
        var config = _configService.Load();

        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            var workingDirectory = tab.WorkingDirectory;
            var dirKey = workingDirectory.ToLowerInvariant();

            // Get directory settings for this tab
            DirectorySettings? dirSettings = null;
            if (config.DirectorySettings.TryGetValue(dirKey, out var settings))
            {
                dirSettings = settings;
            }

            // Reinitialize run configurations
            InitializeRunConfigurations(tab, workingDirectory, dirSettings);
        }
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
            _processService.OpenFolder(folder);
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

    [RelayCommand]
    private void CycleTabForward() => CycleTab(true);

    [RelayCommand]
    private void CycleTabBackward() => CycleTab(false);

    /// <summary>
    /// Returns the static palette commands for the Recent Features page.
    /// </summary>
    internal IReadOnlyList<PaletteCommand> GetPaletteCommandsForFeatures() => _palette.Commands;

    public event EventHandler? ScratchPadRequested;
    public event EventHandler? GitChangesRequested;
    public event EventHandler? SetupRequested;
    public event EventHandler? TaskPanelRequested;
#pragma warning disable CS0067 // Event not yet wired on macOS
    public event EventHandler? ClaudeTasksPanelRequested;
#pragma warning restore CS0067
    public event EventHandler? PrReviewRequested;
    public event EventHandler? MarkdownPreviewRequested;
    public event EventHandler<CenterPanelRestoreEventArgs>? CenterPanelRestoreRequested;
    public event EventHandler<string>? AiPanelCommandRequested;

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
    private void OpenTaskPanel()
    {
        TaskPanelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenClaudeTasksPanel()
    {
        // Get the current workspace path from the selected tab
        var workspacePath = (SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory;

        // Set the workspace on the Claude Tasks panel before opening
        ClaudeTasksPanelViewModel?.Open(workspacePath);
    }

    [RelayCommand]
    private void OpenSessionsTree()
    {
        if (SelectedTab is TerminalPairTabViewModel terminalTab && terminalTab.WorkspaceTasksPanel is { } wsTasks)
        {
            wsTasks.IsVisible = true;
            terminalTab.RightPanelSelectedIndex = 1;
        }
        SessionsTreePanelViewModel?.Open();
    }

    [RelayCommand]
    private async Task OpenMemoryBrowser()
    {
        if (MemoryBrowserViewModel is null) return;
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
            await MemoryBrowserViewModel.OpenAsync(terminalTab);
    }

    [RelayCommand]
    private void OpenDebugLog()
    {
        DebugLogViewModel?.Open();
    }

    [RelayCommand]
    private void OpenQuickTask()
    {
        QuickTaskTitle = string.Empty;
        IsQuickTaskOpen = true;
    }

    [RelayCommand]
    private void CreateQuickTask()
    {
        if (string.IsNullOrWhiteSpace(QuickTaskTitle)) return;

        var task = _taskService.CreateTask(QuickTaskTitle.Trim());

        // Associate with current project if available
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            _taskService.AddProjectToTask(task.Id, terminalTab.Pair.WorkingDirectory);
        }

        QuickTaskTitle = string.Empty;
        IsQuickTaskOpen = false;
    }

    [RelayCommand]
    private void CreateAndStartQuickTask()
    {
        if (string.IsNullOrWhiteSpace(QuickTaskTitle)) return;

        var task = _taskService.CreateTask(QuickTaskTitle.Trim());

        // Associate with current project if available
        if (SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            _taskService.AddProjectToTask(task.Id, terminalTab.Pair.WorkingDirectory);
        }

        _taskService.StartTask(task.Id);

        QuickTaskTitle = string.Empty;
        IsQuickTaskOpen = false;
    }

    [RelayCommand]
    private void OpenQuickNote()
    {
        QuickNoteText = string.Empty;
        IsQuickNoteOpen = true;
    }

    [RelayCommand]
    private void CreateQuickNote()
    {
        if (string.IsNullOrWhiteSpace(QuickNoteText)) return;

        var projectPath = SelectedTab is TerminalPairTabViewModel terminalTab
            ? terminalTab.Pair.WorkingDirectory
            : null;

        _taskService.CreateNote(QuickNoteText.Trim(), projectPath);

        QuickNoteText = string.Empty;
        IsQuickNoteOpen = false;
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

    internal IReadOnlyList<PaletteCommand> BuildStaticPaletteCommands()
    {
        return
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
                Id = "duplicate-tab",
                Name = "Duplicate Tab",
                Description = "Open new tab for same directory",
                Icon = "📋",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (SelectedTab is TerminalPairTabViewModel tab) DuplicateTabCommand.Execute(tab); },
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "move-tab-to-front",
                Name = "Move Tab to Front",
                Description = "Move current tab to the beginning",
                Icon = "⏮",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (SelectedTab != null) MoveTabToFrontCommand.Execute(SelectedTab); },
                CanExecute = () => SelectedTab != null && Tabs.IndexOf(SelectedTab) > 0
            },
            new() {
                Id = "move-tab-to-end",
                Name = "Move Tab to End",
                Description = "Move current tab to the end",
                Icon = "⏭",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (SelectedTab != null) MoveTabToEndCommand.Execute(SelectedTab); },
                CanExecute = () => SelectedTab != null && Tabs.IndexOf(SelectedTab) < Tabs.Count - 1
            },
            new() {
                Id = "close-other-tabs",
                Name = "Close Other Tabs",
                Description = "Close all tabs except current",
                Icon = "🗑",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (SelectedTab != null) CloseOtherTabsCommand.Execute(SelectedTab); },
                CanExecute = () => SelectedTab != null && Tabs.Count > 1
            },
            new() {
                Id = "close-tabs-to-right",
                Name = "Close Tabs to Right",
                Description = "Close all tabs to the right of current",
                Icon = "➡️",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (SelectedTab != null) CloseTabsToRightCommand.Execute(SelectedTab); },
                CanExecute = () => SelectedTab != null && Tabs.IndexOf(SelectedTab) < Tabs.Count - 1
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
                Name = "Open in Finder",
                Description = "Open folder in Finder",
                Shortcut = "⌘E",
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
                Execute = () => { } // TODO: Not yet implemented in Avalonia
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
                Execute = () => { } // TODO: Not yet implemented in Avalonia
            },

            // Task Panel
            new() {
                Id = "task-panel",
                Name = "Tasks",
                Description = "Open task management panel",
                Shortcut = "Ctrl+T",
                Icon = "📋",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenTaskPanelCommand.Execute(null)
            },
            new() {
                Id = "claude-tasks-panel",
                Name = "Claude Tasks",
                Description = "Monitor Claude Code task activity",
                Shortcut = "Ctrl+Shift+K",
                Icon = "🤖",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 1, 27),
                Execute = () => OpenClaudeTasksPanelCommand.Execute(null)
            },
            new() {
                Id = "sessions-tree",
                Name = "Sessions",
                Description = "View active Claude Code sessions and subagents",
                Icon = "🧠",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 13),
                Execute = () => OpenSessionsTreeCommand.Execute(null)
            },
            new() {
                Id = "memory-browser",
                Name = "Memory Browser",
                Description = "Browse Eidet long-term memory for this repo",
                Icon = "🧠",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 13),
                Execute = () => OpenMemoryBrowserCommand.Execute(null)
            },
            new() {
                Id = "debug-log",
                Name = "Debug Log",
                Description = "Show diagnostic messages from MCP, Memory, and other subsystems",
                Icon = "🐛",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 13),
                Execute = () => OpenDebugLogCommand.Execute(null)
            },
            new() {
                Id = "quick-task",
                Name = "Quick Task",
                Description = "Quickly add a new task",
                Shortcut = "Ctrl+Shift+Q",
                Icon = "+",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenQuickTaskCommand.Execute(null)
            },
            new() {
                Id = "quick-note",
                Name = "Quick Note",
                Description = "Capture a quick note",
                Shortcut = "Ctrl+Shift+M",
                Icon = "📝",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => OpenQuickNoteCommand.Execute(null)
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
                Shortcut = "Cmd+Shift+I",
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
                Execute = () => GitChangesRequested?.Invoke(this, EventArgs.Empty),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-commit",
                Name = "Git Commit",
                Description = "Stage files, write message, and commit from the Changes panel (Alt+G)",
                Icon = "💾",
                Category = "Git",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => GitChangesRequested?.Invoke(this, EventArgs.Empty),
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
                Execute = () => { /* Needs to be improved */ },
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },

            // Git operations
            new() {
                Id = "git-pull",
                Name = "Git Pull",
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "timeline-hook-debug",
                Name = "Timeline: Hook Debug Log",
                Description = "Show incoming hook events from API and named pipe (troubleshoot container/session tracking)",
                Icon = "🔍",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 27),
                Execute = () =>
                {
                    if (_apiServer == null)
                    {
                        _toastService.Show("API server not available", ToastType.Error);
                        return;
                    }
                    var dialog = new Views.Dialogs.HookDebugDialog(_apiServer);
                    dialog.Show();
                }
            },
            new() {
                Id = "spark-canvas",
                Name = "Spark: Open Canvas",
                Description = "Open real-time force-directed AI session visualization",
                Shortcut = "Ctrl+Shift+J",
                Icon = "✨",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 27),
                Execute = () => OpenSparkCanvasCommand.Execute(null),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "spark-canvas-window",
                Name = "Spark: Open Canvas (Window)",
                Description = "Open Spark Canvas in a standalone window",
                Icon = "✨",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 27),
                Execute = () =>
                {
                    var vm = new SparkCanvasViewModel(
                        activityService: _sessionActivityService,
                        apiServer: _apiServer,
                        timelineService: _timelineService,
                        configService: _configService);
                    var window = new Views.SparkCanvasWindow(vm);
                    window.Show();
                }
            },
            new() {
                Id = "spark-load-jsonl",
                Name = "Spark: Load JSONL File",
                Description = "Open a .jsonl transcript file in Spark Canvas for visualization",
                Icon = "\u2728",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 30),
                Execute = () => OpenSparkCanvasAndLoadJsonl()
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Description = "System tray icon",
                Icon = "🔽",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => {
                    var config = _configService.Load();
                    config.Settings.ShowInSystemTray = !config.Settings.ShowInSystemTray;
                    _configService.Save(config);
                    // Update system tray service visibility
                    var trayService = App.Current.Services.GetService<ISystemTrayService>();
                    if (trayService != null)
                        trayService.IsEnabled = config.Settings.ShowInSystemTray;
                    _toastService.Show(config.Settings.ShowInSystemTray ? "System tray enabled" : "System tray disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-confirm-close",
                Name = "Toggle Confirm on Close",
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

            // API commands
            new() {
                Id = "api-start",
                Name = "API: Start Server",
                Description = "Start the REST API server",
                Icon = "🌐",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 24),
                Execute = () => _ = StartApiServerAsync(),
                CanExecute = () => _apiServer != null && !_apiServer.IsRunning
            },
            new() {
                Id = "api-stop",
                Name = "API: Stop Server",
                Description = "Stop the REST API server",
                Icon = "🌐",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 24),
                Execute = () => _ = StopApiServerAsync(),
                CanExecute = () => _apiServer != null && _apiServer.IsRunning
            },

            // Status Overlay commands
            new() {
                Id = "toggle-status-overlay",
                Name = "Toggle Status Overlay",
                Description = "Show or hide the floating status overlay",
                Shortcut = "Cmd+Shift+Y",
                Icon = "🔔",
                Category = "Application",
                IntroducedOn = new DateOnly(2026, 2, 26),
                Execute = () => _statusOverlayService?.Toggle()
            },
            new() {
                Id = "new-status-overlay",
                Name = "New Status Overlay",
                Description = "Create an additional floating status overlay instance",
                Icon = "🔔",
                Category = "Application",
                IntroducedOn = new DateOnly(2026, 2, 26),
                Execute = () => _statusOverlayService?.CreateOverlay()
            },
            new() {
                Id = "close-all-status-overlays",
                Name = "Close All Status Overlays",
                Description = "Close all floating status overlay windows",
                Icon = "🔔",
                Category = "Application",
                IntroducedOn = new DateOnly(2026, 2, 26),
                Execute = () => _statusOverlayService?.CloseAll(),
                CanExecute = () => _statusOverlayService?.OverlayCount > 0
            },

            // AI Workflow Commands
            new() {
                Id = "ai-explain-blame",
                Name = "Explain blame line (AI)",
                Description = "AI explains why a blame line was changed",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "explain-blame"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-summarize-file-history",
                Name = "Summarize file history (AI)",
                Description = "AI summarizes a file's commit history",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "summarize-file-history"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-explain-commit",
                Name = "Explain commit (AI)",
                Description = "AI explains what a commit does and why",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "explain-commit"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-explain-reflog",
                Name = "Explain recent git operations (AI)",
                Description = "AI explains recent reflog entries",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "explain-reflog"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-generate-stash-name",
                Name = "Generate stash name (AI)",
                Description = "AI generates a descriptive stash name",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "generate-stash-name"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-assess-merge-risk",
                Name = "Assess merge risk (AI)",
                Description = "AI assesses risk of merging compared branches",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "assess-merge-risk"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-suggest-version",
                Name = "Suggest next version (AI)",
                Description = "AI suggests next semantic version based on tags and commits",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "suggest-version"),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-analyze-ci-failure",
                Name = "Analyze CI failure (AI)",
                Description = "AI analyzes a failed CI check",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "analyze-ci-failure")
            },
            new() {
                Id = "ai-prioritize-prs",
                Name = "Prioritize PRs for review (AI)",
                Description = "AI prioritizes open PRs by review urgency",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "prioritize-prs")
            },
            new() {
                Id = "ai-improve-markdown",
                Name = "Improve markdown (AI)",
                Description = "AI suggests improvements to open markdown file",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => AiPanelCommandRequested?.Invoke(this, "improve-markdown")
            },

            // Container commands
            new() {
                Id = "container-toggle",
                Name = "Container: Toggle for Current Workspace",
                Description = "Enable or disable Docker container isolation for the active workspace",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => ToggleContainerForCurrentWorkspace(),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-rebuild-image",
                Name = "Container: Rebuild Image",
                Description = "Rebuild the Docker workspace image from Dockerfile",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _ = RebuildContainerImageAsync()
            },
            new() {
                Id = "container-stop",
                Name = "Container: Stop Current",
                Description = "Stop the Docker container for the active workspace",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _ = StopCurrentContainerAsync(),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-remove",
                Name = "Container: Remove Current",
                Description = "Remove the Docker container for the active workspace",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _ = RemoveCurrentContainerAsync(),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-recreate",
                Name = "Container: Recreate Current",
                Description = "Remove and recreate the container (applies settings changes)",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _ = RecreateCurrentContainerAsync(),
                CanExecute = () => SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-list",
                Name = "Container: List All",
                Description = "Show all TerminalHost Docker containers",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _ = ListContainersAsync()
            },
            new() {
                Id = "container-clean",
                Name = "Container: Clean Stopped",
                Description = "Remove all stopped Docker containers",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _ = CleanStoppedContainersAsync()
            },
            new() {
                Id = "container-check-docker",
                Name = "Container: Check Docker Status",
                Description = "Verify Docker Desktop is available and running",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _ = CheckDockerStatusAsync()
            },

            // Channel commands
            new() {
                Id = "channel-send-message",
                Name = "Channel: Send Message to Claude",
                Description = "Send a text message to the Claude Code session via the channel",
                Icon = "📨",
                Category = "Channel",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => SendChannelMessage(),
                CanExecute = () => _apiServer?.IsRunning == true
            },
            new() {
                Id = "channel-toggle",
                Name = "Channel: Toggle Integration",
                Description = "Enable or disable Claude Code channel integration",
                Icon = "🔌",
                Category = "Channel",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => ToggleChannelIntegration()
            }
        ];
    }

    private void FilterPaletteCommands()
    {
        _filteredPaletteCommands.Clear();
        var searchText = PaletteSearchText?.ToLower() ?? "";
        var allCommands = new List<PaletteCommand>();

        // Static commands (from providers, gated and filtered)
        allCommands.AddRange(_palette.Filter(searchText));

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

        // Add Claude commands (from ~/.claude/commands/ and .claude/commands/)
        var currentWorkingDir = (SelectedTab as TerminalPairTabViewModel)?.Pair.WorkingDirectory;
        var claudeCommands = _claudeCommandService.GetAllCommands(currentWorkingDir);

        foreach (var cmd in claudeCommands)
        {
            var commandName = $"Claude: /{cmd.Name}";
            var matchesSearch = string.IsNullOrEmpty(searchText) ||
                               commandName.ToLower().Contains(searchText) ||
                               (cmd.Description?.ToLower().Contains(searchText) ?? false) ||
                               "claude".Contains(searchText);

            if (matchesSearch)
            {
                var capturedCmd = cmd; // Capture for closure
                allCommands.Add(new PaletteCommand
                {
                    Id = $"claude-cmd-{cmd.Id}",
                    Name = commandName,
                    Description = cmd.Description ?? cmd.FilePath,
                    Shortcut = cmd.Shortcut ?? "",
                    Icon = "🤖",
                    Category = cmd.Source == ClaudeCommandSource.Global ? "Claude (Global)" : "Claude (Project)",
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

        // Send the slash command to Claude Code
        tab.Pair.CustomTerminal.SendText(
            $"/{command.Name}",
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
    /// Gets all profiles (used by MainWindow for keyboard shortcuts).
    /// </summary>
    public IReadOnlyList<Profile> GetProfiles() => _profileRegistry.Profiles;

    /// <summary>
    /// Raises the MarkdownPreviewRequested event (used by MainWindow for Cmd/Ctrl+M shortcut).
    /// </summary>
    public void RaiseMarkdownPreviewRequested() => MarkdownPreviewRequested?.Invoke(this, EventArgs.Empty);

    #region REST API State Providers

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
                WorkingDirectory = tab.WorkingDirectory,
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
                        IsBusy = tab.IsCustomTerminalActive,
                    },
                    Shell = new ApiTerminalInfo
                    {
                        Title = tab.ShellTerminalTitle ?? "",
                        IsActive = tab.ActiveTerminal == ActiveTerminal.Shell,
                        IsBusy = tab.IsShellTerminalActive,
                    },
                    Run = tab.IsRunTerminalVisible ? new ApiTerminalInfo
                    {
                        Title = "Run",
                        IsActive = tab.ActiveTerminal == ActiveTerminal.Run,
                        IsBusy = tab.IsRunTerminalActive,
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

    private async Task StartApiServerAsync()
    {
        if (_apiServer == null) return;
        try
        {
            await _apiServer.StartAsync();
            _toastService.Show("API server started", ToastType.Success);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to start API server: {ex.Message}", ToastType.Error);
        }
    }

    private async Task StopApiServerAsync()
    {
        if (_apiServer == null) return;
        await _apiServer.StopAsync();
        _toastService.Show("API server stopped", ToastType.Info);
    }

    #endregion

    // ── Container command helpers ───────────────────────────────────────

    private async Task EnsureContainerForWorkspaceAsync(string workspaceDir)
    {
        try
        {
            // Step 1: Check Docker availability
            if (!await _containerService!.IsDockerAvailableAsync())
            {
                _toastService.Show(
                    "Docker Desktop is not running. Start Docker Desktop or disable containers for this workspace (Container: Toggle in command palette).",
                    ToastType.Warning);
                return;
            }

            // Step 2: First-time image build
            if (!await _containerService.IsImageBuiltAsync())
            {
                var choice = _dialogService.ShowCustomButtons(
                    "The container workspace image needs to be built before first use. " +
                    "This is a one-time operation that takes approximately 5-10 minutes.\n\n" +
                    "Build the image now?",
                    "Container Setup",
                    "Build Now", "Skip", "Cancel");

                if (choice == 0)
                {
                    using var toast = _toastService.ShowProgress("Building container image...");
                    var success = await _containerService.BuildImageAsync(line =>
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            toast.Update(line.Length > 80 ? line[..80] + "..." : line);
                    });

                    if (!success)
                    {
                        toast.Fail("Image build failed. Check Docker Desktop logs.");
                        return;
                    }
                    toast.Complete("Container image built successfully");
                }
                else
                {
                    return;
                }
            }

            // Step 3: Check if Dockerfile has been updated (new TerminalHost version)
            var dockerfileStatus = _containerService.CheckDockerfileStatus();
            if (dockerfileStatus == DockerfileStatus.Stale)
            {
                var choice = _dialogService.ShowCustomButtons(
                    "The container Dockerfile has been updated in this version of TerminalHost " +
                    "(new tools or fixes available). Rebuilding the image is recommended.\n\n" +
                    "You can also rebuild later via command palette (Container: Rebuild Image).",
                    "Dockerfile Updated",
                    "Rebuild Now", "Skip");

                if (choice == 0)
                {
                    _containerService.UpdateDockerfileToLatest();
                    using var toast = _toastService.ShowProgress("Rebuilding container image...");
                    var success = await _containerService.BuildImageAsync(line =>
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            toast.Update(line.Length > 80 ? line[..80] + "..." : line);
                    });

                    if (success)
                        toast.Complete("Image rebuilt successfully");
                    else
                        toast.Fail("Image rebuild failed");
                }
            }

            // Step 4: Ensure container running
            var result = await _containerService.EnsureContainerRunningAsync(workspaceDir);

            // Step 5: Staleness warnings (non-blocking)
            if (result.IsConfigStale)
            {
                _toastService.Show(
                    "Container settings have changed. Use 'Container: Recreate Current' from the command palette to apply.",
                    ToastType.Warning);
            }
            else if (result.IsImageStale && dockerfileStatus != DockerfileStatus.Stale)
            {
                _toastService.Show(
                    "This container was built from an older image. Rebuild and recreate for latest tools.",
                    ToastType.Info);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start container for {workspaceDir}: {ex.Message}");
            _toastService.Show($"Docker container failed to start: {ex.Message}", ToastType.Error);
        }
    }

    private void ToggleContainerForCurrentWorkspace()
    {
        if (SelectedTab is not TerminalPairTabViewModel tab) return;
        var dir = tab.Pair.WorkingDirectory;
        var config = _configService.Load();
        var normalizedPath = NormalizePath(dir);

        if (!config.DirectorySettings.TryGetValue(normalizedPath, out var dirSettings))
        {
            dirSettings = new DirectorySettings();
            config.DirectorySettings[normalizedPath] = dirSettings;
        }

        // Toggle: if currently enabled (explicitly or via global), disable; otherwise enable
        var currentlyEnabled = _containerService?.IsEnabledForDirectory(dir) ?? false;
        dirSettings.ContainerEnabled = !currentlyEnabled;
        _configService.Save(config);

        var nowEnabled = dirSettings.ContainerEnabled.Value;
        var state = nowEnabled ? "enabled" : "disabled";
        _toastService.Show($"Container {state} for {Path.GetFileName(dir)}. Reloading tab...", ToastType.Info);

        // Stop the container when toggling off
        if (!nowEnabled && _containerService != null)
        {
            _ = Task.Run(async () =>
            {
                try { await _containerService.StopContainerAsync(dir); } catch { }
            });
        }

        // Auto-reload the tab so container settings take effect immediately
        ReloadTerminalTab(tab);
    }

    private void ReloadTerminalTab(TerminalPairTabViewModel tab)
    {
        var dir = tab.Pair.WorkingDirectory;

        // Force-close without confirmation (we're reloading, not closing)
        tab.CloseRequested -= OnTabCloseRequested;
        tab.SettingsChanged -= OnTabSettingsChanged;
        tab.RunStartRequested -= OnRunStartRequested;
        tab.RunStopRequested -= OnRunStopRequested;
        _sessionManager.CloseSession(tab.Pair.CustomTerminal);
        _sessionManager.CloseSession(tab.Pair.ShellTerminal);
        if (tab.Pair.RunTerminal != null)
            _sessionManager.CloseSession(tab.Pair.RunTerminal);
        tab.Pair.Dispose();
        Tabs.Remove(tab);

        // Reopen with fresh container state
        OpenProjectTab(dir);
    }

    private async Task RebuildContainerImageAsync()
    {
        if (_containerService == null) return;

        // Update the Dockerfile to the latest embedded version if stale
        var status = _containerService.CheckDockerfileStatus();
        if (status == DockerfileStatus.Stale)
            _containerService.UpdateDockerfileToLatest();

        using var toast = _toastService.ShowProgress("Building Docker image...");
        try
        {
            var success = await _containerService.BuildImageAsync(line =>
            {
                // Show last meaningful build step in the progress toast
                if (!string.IsNullOrWhiteSpace(line))
                    toast.Update(line.Length > 80 ? line[..80] + "..." : line);
            });

            if (success)
                toast.Complete("Docker image built successfully");
            else
                toast.Fail("Docker image build failed — check Docker Desktop logs");
        }
        catch (Exception ex)
        {
            toast.Fail($"Image build failed: {ex.Message}");
        }
    }

    private async Task RecreateCurrentContainerAsync()
    {
        if (_containerService == null || SelectedTab is not TerminalPairTabViewModel tab) return;
        var dir = tab.Pair.WorkingDirectory;

        using var toast = _toastService.ShowProgress("Recreating container...");
        try
        {
            toast.Update("Removing old container...");
            await _containerService.RemoveContainerAsync(dir);

            toast.Update("Creating new container...");
            await _containerService.EnsureContainerRunningAsync(dir);

            toast.Complete("Container recreated — reloading tab...");
            ReloadTerminalTab(tab);
        }
        catch (Exception ex)
        {
            toast.Fail($"Recreate failed: {ex.Message}");
        }
    }

    private async Task StopCurrentContainerAsync()
    {
        if (_containerService == null || SelectedTab is not TerminalPairTabViewModel tab) return;
        try
        {
            await _containerService.StopContainerAsync(tab.Pair.WorkingDirectory);
            _toastService.Show("Container stopped", ToastType.Success);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to stop container: {ex.Message}", ToastType.Error);
        }
    }

    private async Task RemoveCurrentContainerAsync()
    {
        if (_containerService == null || SelectedTab is not TerminalPairTabViewModel tab) return;
        try
        {
            await _containerService.RemoveContainerAsync(tab.Pair.WorkingDirectory);
            _toastService.Show("Container removed", ToastType.Success);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to remove container: {ex.Message}", ToastType.Error);
        }
    }

    private async Task ListContainersAsync()
    {
        if (_containerService == null) return;
        try
        {
            var containers = await _containerService.ListContainersAsync();
            if (containers.Count == 0)
            {
                _toastService.Show("No containers found", ToastType.Info);
                return;
            }

            var lines = containers.Select(c => $"{c.Name}: {c.State}");
            _toastService.Show($"{containers.Count} container(s):\n{string.Join("\n", lines)}", ToastType.Info);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to list containers: {ex.Message}", ToastType.Error);
        }
    }

    private async Task CleanStoppedContainersAsync()
    {
        if (_containerService == null) return;
        try
        {
            var count = await _containerService.CleanStoppedContainersAsync();
            _toastService.Show(count > 0 ? $"Removed {count} stopped container(s)" : "No stopped containers to remove", ToastType.Success);
        }
        catch (Exception ex)
        {
            _toastService.Show($"Failed to clean containers: {ex.Message}", ToastType.Error);
        }
    }

    private async Task CheckDockerStatusAsync()
    {
        if (_containerService == null) return;
        var available = await _containerService.IsDockerAvailableAsync();
        _toastService.Show(
            available ? "Docker is available and running" : "Docker is not available. Ensure Docker Desktop is running.",
            available ? ToastType.Success : ToastType.Warning);
    }

    // ── Channel command helpers ─────────────────────────────────────────

    private void SendChannelMessage()
    {
        var message = _dialogService.ShowInput("Enter a message to send to Claude Code via the channel:", "Send to Claude");
        if (string.IsNullOrWhiteSpace(message)) return;

        // Get current repo index
        int? repoIndex = null;
        if (SelectedTab is TerminalPairTabViewModel termTab)
        {
            repoIndex = Tabs.OfType<TerminalPairTabViewModel>().ToList().IndexOf(termTab);
        }

        // Publish the message as a channel event via the event aggregator
        _eventAggregator?.Publish(new ApiEvent
        {
            Type = "channel.user_message",
            RepoIndex = repoIndex,
            Data = new { message, sender = "user" }
        });

        _toastService.Show("Message sent to Claude via channel", ToastType.Success);
    }

    private void ToggleChannelIntegration()
    {
        var config = _configService.Load();
        config.Settings.Channel.Enabled = !config.Settings.Channel.Enabled;
        _configService.Save(config);

        var status = config.Settings.Channel.Enabled ? "enabled" : "disabled";
        _toastService.Show($"Channel integration {status}. Restart Claude Code terminals to apply.", ToastType.Info);
    }

    public void Shutdown()
    {
        // Stop and dispose timers
        _gitStatusTimer.Dispose();
        _gitAutoFetchTimer.Dispose();
        _activityTimer.Dispose();
        _linkDetectionTimer.Dispose();
        _runUrlDetectionTimer.Dispose();

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

        // Stop containers if configured — run on thread pool to avoid UI thread
        // deadlock, but wait with a timeout so the process doesn't exit too early
        if (_containerService != null)
        {
            try
            {
                var config = _configService.Load();
                if (config.Settings.Container.StopContainersOnExit)
                {
                    var dockerPath = config.Settings.Container.DockerPath;
                    var stopTask = Task.Run(async () =>
                    {
                        try
                        {
                            var containers = await _containerService.ListContainersAsync();
                            var stopTasks = containers
                                .Where(c => c.State == ContainerState.Running)
                                .Select(c => _processService.RunAsync(dockerPath, $"stop -t 2 {c.Name}",
                                    timeout: TimeSpan.FromSeconds(5)))
                                .ToList();
                            await Task.WhenAll(stopTasks);
                        }
                        catch { }
                    });
                    stopTask.Wait(TimeSpan.FromSeconds(10));
                }
            }
            catch { }
        }

        _sessionManager.CloseAllSessions();
        foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
        {
            tab.Pair.Dispose();
        }
    }

    #region Voice Commands

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

        foreach (var cmd in _palette.Commands)
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
                Execute = () => ExecuteQuickCommand(qc),
                Category = "Quick Command"
            });
        }

        _voiceCommandService.UpdateGrammar(entries);
    }

    /// <summary>
    /// Toggle voice listening on/off (F4 shortcut).
    /// </summary>
    public void ToggleVoiceListening()
    {
        var settings = _configService.Load().Settings.Voice;
        if (!settings.Enabled)
        {
            _toastService.Show("Voice commands are disabled. Enable them in Settings.", ToastType.Info);
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

    #endregion
}

public class RunTerminalRequestedEventArgs : EventArgs
{
    public required TerminalPairTabViewModel Tab { get; init; }
    public required RunConfiguration Configuration { get; init; }
    public bool IsStop { get; init; }
}

public class FileHistoryRequestedEventArgs : EventArgs
{
    public required string WorkingDirectory { get; init; }
    public required string FilePath { get; init; }
}

public class FileBlameRequestedEventArgs : EventArgs
{
    public required string WorkingDirectory { get; init; }
    public required string FilePath { get; init; }
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

/// <summary>
/// Display model for a shortcut in the Help view, with pre-formatted platform-specific text.
/// </summary>
public record HelpDisplayItem(string Shortcut, string Description);

/// <summary>
/// Display model for a section of shortcuts in the Help view.
/// </summary>
public record HelpDisplaySection(string Name, List<HelpDisplayItem> Items);

/// <summary>
/// Display model for a command line example in the Help view.
/// </summary>
public record HelpCommandLineExample(string Command, string Description);
