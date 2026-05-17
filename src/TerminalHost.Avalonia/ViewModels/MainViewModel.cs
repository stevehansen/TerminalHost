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
    internal readonly IConfigurationService _configService;
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
    internal readonly IToastService _toastService;
    private readonly IClipboardService _clipboardService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFilePickerService _filePickerService;
    private readonly ITimerService _timerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ITimelineService _timelineService;
    private readonly IClaudeTaskDetectionService? _claudeTaskDetectionService;
    private readonly ITaskAggregator? _taskAggregator;
    internal readonly IApiServer? _apiServer;
    private readonly ISessionActivityService? _sessionActivityService;
    private readonly IEventAggregatorService? _eventAggregator;
    private readonly IWebhookDeliveryService? _webhookDeliveryService;
    private readonly IAiExecutionService? _aiExecutionService;
    private readonly IInputPromptDetectionService _inputPromptDetectionService;
    private readonly ISoundService? _soundService;
    private readonly StatusOverlayService? _statusOverlayService;
    private readonly IContainerService? _containerService;
    private readonly IVoiceCommandService? _voiceCommandService;
    private readonly IApiStateProjector _apiStateProjector;
    private readonly Core.Interfaces.ITimerService _coreTimerService;
    private readonly TabRouter _router;

    private readonly IProjectMonitor _projectMonitor;
    private readonly IDirectorySettingsStore _directorySettings;
    private readonly ITabFactory _tabFactory;
    private readonly IWorkspaceStateStore _workspaceStateStore;

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

    // The tab collection lives on the workspace service (Step 4a of #48). The
    // service is constructed in the ctor; this property surfaces its collection
    // so XAML bindings and TabRouter keep the same reference type they had
    // before the seam landed.
    private readonly IWorkspaceService _workspace = new WorkspaceService();
    public ObservableCollection<ITabViewModel> Tabs => _workspace.Tabs;

    // SelectedTab forwards to the workspace service (Step 4b of #48). The
    // service owns the actual value, toggles IsSelected on old/new tabs, and
    // raises SelectedTabChanged — host-specific side effects run in
    // OnWorkspaceSelectedTabChanged, subscribed in the ctor.
    public ITabViewModel? SelectedTab
    {
        get => _workspace.SelectedTab;
        set => _workspace.SelectedTab = value;
    }

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
        ITabFactory tabFactory,
        IWorkspaceStateStore workspaceStateStore,
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
        Core.Interfaces.ITimerService? coreTimerService = null,
        IApiStateProjector? apiStateProjector = null)
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
        _tabFactory = tabFactory;
        _workspaceStateStore = workspaceStateStore;
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
        _apiStateProjector = apiStateProjector ?? new ApiStateProjector();
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

        _workspace.SelectedTabChanged += OnWorkspaceSelectedTabChanged;

        // Subscribe to Claude command changes (dispatch to UI thread since FileSystemWatcher raises events on thread pool)
        _claudeCommandService.CommandsChanged += (_, _) => _dispatcherService.BeginInvoke(FilterPaletteCommands);

        _router = new TabRouter(_workspace.Tabs, tab => SelectedTab = tab);
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
            providers: new ICommandProvider[]
            {
                new TabCommandProvider(this),
                new FileCommandProvider(this),
                new PanelCommandProvider(this),
                new SettingsToggleCommandProvider(this),
                new AppCommandProvider(this),
                new ContainerCommandProvider(this),
                new ApiCommandProvider(this),
                new VoiceCommandProvider(this),
                new GitCommandProvider(this),
                new RunCommandProvider(this),
                new AiCommandProvider(this),
                new GitHubCommandProvider(this),
                new LayoutCommandProvider(this),
                new TimelineCommandProvider(this),
                new SparkCanvasCommandProvider(this),
                new ChannelCommandProvider(this),
                new StatusOverlayCommandProvider(this),
            },
            context: commandContext);
        InitializeVoiceGrammar();   // Build voice grammar from palette commands

        // Step 3a (#48): all five periodic refresh paths run through one monitor.
        _projectMonitor = new ProjectMonitor(_coreTimerService);
        var fetchInterval = Math.Max(30, _configService.Load().Settings.GitAutoFetchIntervalSeconds);
        _projectMonitor.SetInterval(SignalKind.GitAutoFetch, TimeSpan.FromSeconds(fetchInterval));
        _projectMonitor.Tick += OnProjectSignal;

        // Step 4d (#48): per-directory settings + recent-folders persistence
        // lives behind a single port so both hosts share the same normalization
        // and load-mutate-save sequence.
        _directorySettings = new DirectorySettingsStore(_configService);
    }

    private void OnProjectSignal(object? sender, ProjectSignalEventArgs e)
    {
        switch (e.Kind)
        {
            case SignalKind.GitStatus:    _ = RefreshSelectedTabGitStatusAsync(); break;
            case SignalKind.GitAutoFetch: _ = AutoFetchAllAsync(); break;
            case SignalKind.Activity:     RefreshActivityState(); break;
            case SignalKind.Links:        RefreshDetectedLinks(); break;
            case SignalKind.RunUrl:       RefreshRunUrlDetection(); break;
        }
    }

    partial void OnDropdownSearchTextChanged(string value)
    {
        UpdateFilteredDropdownTabs();
    }

    partial void OnSwitcherSearchTextChanged(string value)
    {
        UpdateFilteredSwitcherTabs();
    }

    private void OnWorkspaceSelectedTabChanged(object? sender, TabSelectionChangedEventArgs e)
    {
        var oldValue = e.OldValue;
        var newValue = e.NewValue;

        // Notify XAML bindings — the [ObservableProperty]/[NotifyPropertyChangedFor]
        // pair that previously fired these auto is gone now that SelectedTab
        // forwards to IWorkspaceService.
        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(WindowTitle));

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

        if (newValue != null)
        {
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
                _eventAggregator.Publish("repo.activated", tabIndex, new
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

        // Start the always-on signals.
        _projectMonitor.Start(SignalKind.Activity | SignalKind.Links | SignalKind.RunUrl);

        // Git signals depend on tracking mode + auto-fetch setting.
        if (_gitTrackingMode != GitTrackingMode.Disabled)
            _projectMonitor.Start(SignalKind.GitStatus);
        if (_gitTrackingMode == GitTrackingMode.All && _configService.Load().Settings.GitAutoFetch)
            _projectMonitor.Start(SignalKind.GitAutoFetch);
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
                _eventAggregator.Publish("repo.git_status_changed", tabIndex, new
                {
                    branch = status.BranchName, isDirty = status.IsDirty,
                    ahead = status.AheadCount, behind = status.BehindCount
                });

                if (previousBranch != null && previousBranch != status.BranchName)
                {
                    _eventAggregator.Publish("repo.branch_switched", tabIndex, new
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

        var existingFolders = config.OpenFolders.Where(_fileSystem.DirectoryExists).ToList();
        if (_containerService != null)
        {
            Task.Run(() => _containerService.PreWarmContainersAsync(existingFolders)).GetAwaiter().GetResult();
        }

        foreach (var folder in existingFolders)
        {
            // Don't select tabs during restore - lazy initialization will happen when user clicks
            OpenProjectTab(folder, selectTab: false);
        }

        // Capture and stop deferring
        var pendingRestores = _deferredCenterPanelRestores;
        _deferredCenterPanelRestores = null;

        // Restore the last selected tab (this is the only one that will be initialized on startup)
        var tabToSelect = _workspaceStateStore.FindLastSelectedTab(Tabs, lastTabType: null, config.LastSelectedFolder);
        if (tabToSelect != null)
        {
            SelectedTab = tabToSelect;
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


    private void SaveDirectorySettings(TerminalPairTabViewModel tab)
    {
        _directorySettings.Update(tab.Pair.WorkingDirectory, tab.WriteToDirectorySettings);
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
            workingDirectory = WorkspaceService.NormalizeWorkingDirectory(workingDirectory);

            if (!_fileSystem.DirectoryExists(workingDirectory)) // Use injected IFileSystem
            {
                _dialogService.ShowError($"Directory not found: {workingDirectory}"); // Use injected IDialogService
                return;
            }

            if (!forceNew)
            {
                var existingTab = _workspace.FindByWorkingDirectory<TerminalPairTabViewModel>(workingDirectory).FirstOrDefault();
                if (existingTab != null)
                {
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
                    // During restore, containers were pre-warmed in parallel, so skip
                    // the blocking per-tab call and let the async check retry if needed.
                    if (selectTab)
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
            var tabViewModel = _tabFactory.CreateTerminalPairTab(pair, aiAssistant, enabledAssistants, settings.ShellCommandIcon, duplicateIndex);
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
            var dirSettings = _directorySettings.Get(workingDirectory);
            if (dirSettings != null)
            {
                tabViewModel.LoadLayoutFromDirectorySettings(dirSettings);
            }

            // Initialize run configurations (from settings or auto-detect)
            var runSettings = dirSettings ?? new DirectorySettings();
            var runConfigs = _projectDetectionService.GetOrCreateConfigurations(workingDirectory, runSettings);
            tabViewModel.InitializeRunConfigurations(runConfigs, runSettings.ActiveRunConfigurationId);

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
                tabViewModel.LoadPanelStateFromDirectorySettings(dirSettings);
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
            _eventAggregator.Publish("repo.opened", data: new { workingDirectory, title = tabViewModel.Title });

            // Only select the tab if requested (false during startup restore for lazy init)
            if (selectTab)
            {
                SelectedTab = tabViewModel;
            }

            // Track in recent folders
            _directorySettings.AddRecent(workingDirectory);

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

            effectiveWorkingDir = WorkspaceService.NormalizeWorkingDirectory(effectiveWorkingDir);

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
        if (!tab.ConfirmCanClose(_dialogService, _profileRegistry.Settings.ConfirmOnClose)) return;

        if (tab is TerminalPairTabViewModel terminalTab)
        {
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
            profileTab.CloseRequested -= OnTabCloseRequested;
            _sessionManager.CloseSession(profileTab.Session);
            profileTab.Session.Dispose();
        }

        // Publish API event for repo closed
        if (tab is TerminalPairTabViewModel closedTerminalTab)
        {
            _eventAggregator.Publish("repo.closed", data: new { workingDirectory = closedTerminalTab.WorkingDirectory, title = closedTerminalTab.Title });
        }

        _workspace.RemoveAndPickNext(tab);
    }

    /// <summary>
    /// Gets the next duplicate index for tabs with the same working directory.
    /// Returns 0 for the first tab (no suffix), 2 for the second, etc.
    /// </summary>
    private int GetDuplicateTabIndex(string workingDirectory)
    {
        var existingTabs = _workspace.FindByWorkingDirectory<TerminalPairTabViewModel>(workingDirectory).ToList();
        if (existingTabs.Count == 0) return 0;
        return Math.Max(2, existingTabs.Max(t => t.DuplicateIndex) + 1);
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
    private void MoveTabToFront(ITabViewModel? tab) => _workspace.MoveToFront(tab);

    /// <summary>
    /// Moves the specified tab to the end of the tab list.
    /// </summary>
    [RelayCommand]
    private void MoveTabToEnd(ITabViewModel? tab) => _workspace.MoveToEnd(tab);

    /// <summary>
    /// Closes all tabs except the specified one.
    /// </summary>
    [RelayCommand]
    private void CloseOtherTabs(ITabViewModel? tab)
    {
        foreach (var t in _workspace.GetTabsToCloseExcept(tab))
            CloseTabCommand.Execute(t);
    }

    /// <summary>
    /// Closes all tabs to the right of the specified tab.
    /// </summary>
    [RelayCommand]
    private void CloseTabsToRight(ITabViewModel? tab)
    {
        foreach (var t in _workspace.GetTabsToCloseToRightOf(tab))
            CloseTabCommand.Execute(t);
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

    internal void OpenSparkCanvasAndLoadJsonl()
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
                _projectMonitor.Stop(SignalKind.GitStatus | SignalKind.GitAutoFetch);
            }
            else
            {
                _projectMonitor.Start(SignalKind.GitStatus);
                if (_gitTrackingMode == GitTrackingMode.All && config.Settings.GitAutoFetch)
                    _projectMonitor.Start(SignalKind.GitAutoFetch);
                else
                    _projectMonitor.Stop(SignalKind.GitAutoFetch);
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
            var runSettings = dirSettings ?? new DirectorySettings();
            var runConfigs = _projectDetectionService.GetOrCreateConfigurations(workingDirectory, runSettings);
            tab.InitializeRunConfigurations(runConfigs, runSettings.ActiveRunConfigurationId);
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

    // ── Event raise helpers for ICommandProvider classes (Step 2c) ──────
    // C# only allows events to be raised from the declaring class, so providers
    // call these wrappers instead of touching the events directly.
    internal void RequestGitChanges() => GitChangesRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestPrReview() => PrReviewRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestAiPanelCommand(string command) => AiPanelCommandRequested?.Invoke(this, command);

    // ── Event raise helpers for ICommandProvider classes (Step 2e) ──────
    internal void RequestFilePreview(string path, int line, int column)
        => FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = path, Line = line, Column = column });
    internal void RequestMarkdownPreview() => MarkdownPreviewRequested?.Invoke(this, EventArgs.Empty);

    // ── Helpers for ICommandProvider classes (Step 2d) ──────────────────
    internal StatusOverlayService? StatusOverlayService => _statusOverlayService;

    internal void OpenTimelineHookDebug()
    {
        if (_apiServer == null)
        {
            _toastService.Show("API server not available", ToastType.Error);
            return;
        }
        var dialog = new Views.Dialogs.HookDebugDialog(_apiServer);
        dialog.Show();
    }

    internal void OpenSparkCanvasWindow()
    {
        var vm = new SparkCanvasViewModel(
            activityService: _sessionActivityService,
            apiServer: _apiServer,
            timelineService: _timelineService,
            configService: _configService);
        var window = new Views.SparkCanvasWindow(vm);
        window.Show();
    }

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

    private (IReadOnlyList<ProjectTabApiState> Tabs, int SelectedIndex) SnapshotProjectTabs()
    {
        var tabs = Tabs.OfType<TerminalPairTabViewModel>().ToList();
        var selectedIndex = SelectedTab is TerminalPairTabViewModel s ? tabs.IndexOf(s) : -1;
        return (tabs.Select(t => t.ToApiState()).ToList(), selectedIndex);
    }

    private List<ApiRepoInfo> BuildRepoList()
    {
        var (tabs, sel) = SnapshotProjectTabs();
        return _apiStateProjector.BuildRepoList(tabs, sel);
    }

    private ApiRepoDetailInfo? BuildRepoDetail(int index)
    {
        var (tabs, sel) = SnapshotProjectTabs();
        return _apiStateProjector.BuildRepoDetail(tabs, sel, index);
    }

    private List<ApiWorkspaceInfo> BuildWorkspaceList()
        => _apiStateProjector.BuildWorkspaceList(_configService.Load().Workspaces, BuildRepoList());

    internal async Task StartApiServerAsync()
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

    internal async Task StopApiServerAsync()
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

    internal void ToggleContainerForCurrentWorkspace()
    {
        if (SelectedTab is not TerminalPairTabViewModel tab) return;
        var dir = tab.Pair.WorkingDirectory;
        var currentlyEnabled = _containerService?.IsEnabledForDirectory(dir) ?? false;
        var nowEnabled = !currentlyEnabled;
        _directorySettings.Update(dir, settings => settings.ContainerEnabled = nowEnabled);

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

    internal async Task RebuildContainerImageAsync()
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

    internal async Task RecreateCurrentContainerAsync()
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

    internal async Task StopCurrentContainerAsync()
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

    internal async Task RemoveCurrentContainerAsync()
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

    internal async Task ListContainersAsync()
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

    internal async Task CleanStoppedContainersAsync()
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

    internal async Task CheckDockerStatusAsync()
    {
        if (_containerService == null) return;
        var available = await _containerService.IsDockerAvailableAsync();
        _toastService.Show(
            available ? "Docker is available and running" : "Docker is not available. Ensure Docker Desktop is running.",
            available ? ToastType.Success : ToastType.Warning);
    }

    // ── Channel command helpers ─────────────────────────────────────────

    internal void SendChannelMessage()
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
        _eventAggregator.Publish("channel.user_message", repoIndex, new { message, sender = "user" });

        _toastService.Show("Message sent to Claude via channel", ToastType.Success);
    }

    internal void ToggleChannelIntegration()
    {
        var config = _configService.Load();
        config.Settings.Channel.Enabled = !config.Settings.Channel.Enabled;
        _configService.Save(config);

        var status = config.Settings.Channel.Enabled ? "enabled" : "disabled";
        _toastService.Show($"Channel integration {status}. Restart Claude Code terminals to apply.", ToastType.Info);
    }

    public void Shutdown()
    {
        // Stop and dispose the project signal monitor.
        _projectMonitor.Dispose();

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
        _workspaceStateStore.SaveOpenFolders(Tabs, SelectedTab);

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
