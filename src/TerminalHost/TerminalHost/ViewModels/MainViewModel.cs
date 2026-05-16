using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;
using TerminalHost.Core.Workspace;
using TerminalHost.Services;

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

    private readonly IFilePreviewService _filePreviewService;
    private readonly IClaudeCommandService _claudeCommandService;
    private readonly IAiAssistantService _aiAssistantService;
    internal readonly IProcessService _processService;
    internal readonly IToastService _toastService;
    private readonly ITimerService _timerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly ITabFactory _tabFactory;
    private readonly IWorkspaceStateStore _workspaceStateStore;
    private readonly ITimelineService _timelineService;
    private readonly IInputPromptDetectionService _inputPromptDetectionService;
    private readonly ITaskService? _taskService;
    private readonly IVoiceCommandService? _voiceCommandService;
    private readonly IEventAggregatorService? _eventAggregator;
    internal readonly IApiServer? _apiServer;
    private readonly IWebhookDeliveryService? _webhookDeliveryService;
    private readonly StatusOverlayService? _statusOverlayService;
    private readonly IContainerService? _containerService;
    private readonly TabRouter _router;

    private readonly IProjectMonitor _projectMonitor;
    private readonly IDirectorySettingsStore _directorySettings;

    // Cached git tracking mode to avoid config loads on every timer tick
    private GitTrackingMode _gitTrackingMode;

    // Cached settings reference for command palette NameProvider lambdas
    // (avoids 30 × 145KB config loads on every palette render/keystroke)
    internal AppSettings _cachedSettings = new();

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

    // The tab collection lives on the workspace service (Step 4a of #48). The
    // service is constructed in the ctor; this property surfaces its collection
    // so XAML bindings and TabRouter keep the same reference type they had
    // before the seam landed.
    private readonly IWorkspaceService _workspace = new WorkspaceService();
    public ObservableCollection<ITabViewModel> Tabs => _workspace.Tabs;

    // SelectedTab forwards to the workspace service (Step 4b of #48). The
    // service owns the actual value, toggles IsSelected on old/new tabs, and
    // raises SelectedTabChanged — host-specific side effects (focus tracking,
    // API events, panel updates) run in the OnWorkspaceSelectedTabChanged
    // handler subscribed in the ctor.
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

    private ICommandPalette _palette = null!;
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
        ITabFactory tabFactory,
        IWorkspaceStateStore workspaceStateStore,
        ITimelineService timelineService,
        IInputPromptDetectionService inputPromptDetectionService,
        ITaskService? taskService = null,
        IVoiceCommandService? voiceCommandService = null,
        IEventAggregatorService? eventAggregator = null,
        IApiServer? apiServer = null,
        IWebhookDeliveryService? webhookDeliveryService = null,
        StatusOverlayService? statusOverlayService = null,
        IContainerService? containerService = null)
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
        _tabFactory = tabFactory;
        _workspaceStateStore = workspaceStateStore;
        _timelineService = timelineService;
        _inputPromptDetectionService = inputPromptDetectionService;
        _taskService = taskService;
        _voiceCommandService = voiceCommandService;
        _eventAggregator = eventAggregator;
        _apiServer = apiServer;
        _webhookDeliveryService = webhookDeliveryService;
        _statusOverlayService = statusOverlayService;
        _containerService = containerService;

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

        // Cache settings reference for command palette NameProvider lambdas
        _cachedSettings = configService.Load().Settings;

        // Initialize touch mode from config
        TouchMode = _cachedSettings.TouchMode;

        // Subscribe to Tabs collection changes for NonProjectTabs updates
        _workspace.Tabs.CollectionChanged += (s, e) =>
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

        _workspace.SelectedTabChanged += OnWorkspaceSelectedTabChanged;

        // Subscribe to Claude command changes (dispatch to UI thread since FileSystemWatcher raises events on thread pool)
        _claudeCommandService.CommandsChanged += (_, _) => _dispatcherService.BeginInvoke(FilterPaletteCommands);

        _router = new TabRouter(_workspace.Tabs, tab => SelectedTab = tab);
        _router.Register<SettingsTabViewModel>(
            factory: () => _viewModelFactory.CreateSettings(),
            onCreated: tab =>
            {
                tab.CloseRequested += OnTabCloseRequested;
                tab.ConfigSaved += OnConfigSaved;
                RefreshSettingsMemoryStatus(tab);
            });
        _router.Register<DashboardTabViewModel>(
            factory: () => _viewModelFactory.CreateDashboard(this),
            onCreated: tab =>
            {
                tab.CloseRequested += OnTabCloseRequested;
                tab.PrReviewRequested += OnDashboardPrReviewRequested;
            });
        _router.Register<StatisticsTabViewModel>(
            factory: () => _viewModelFactory.CreateStatistics(),
            onCreated: tab => tab.CloseRequested += OnTabCloseRequested);
        _router.Register<TimelineTabViewModel>(
            factory: () => _viewModelFactory.CreateTimeline(),
            onCreated: tab =>
            {
                tab.CloseRequested += OnTabCloseRequested;
                tab.PopOutRequested += OnTimelinePopOutRequested;
            });

        FilteredDropdownTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredDropdownTabs);
        UpdateFilteredDropdownTabs(); // Initial population

        FilteredSwitcherTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filteredSwitcherTabs);
        UpdateFilteredSwitcherTabs(); // Initial population

        FilteredPaletteCommands = new ReadOnlyObservableCollection<PaletteCommand>(_filteredPaletteCommands);
        using (StartupProfiler.Instance.Measure("InitializeCommandPalette"))
        {
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
            _ = _palette.Commands; // force provider evaluation inside the profiler scope
        }
        using (StartupProfiler.Instance.Measure("InitializeVoiceGrammar"))
            InitializeVoiceGrammar();   // Build voice grammar from palette commands

        // Step 3a (#48): all five periodic refresh paths run through one monitor.
        _projectMonitor = new ProjectMonitor(_timerService);
        var fetchInterval = Math.Max(30, configService.Load().Settings.GitAutoFetchIntervalSeconds);
        _projectMonitor.SetInterval(SignalKind.GitAutoFetch, TimeSpan.FromSeconds(fetchInterval));
        _projectMonitor.Tick += OnProjectSignal;

        // Step 4d (#48): per-directory settings + recent-folders persistence
        // lives behind a single port so both hosts share the same normalization
        // and load-mutate-save sequence.
        _directorySettings = new DirectorySettingsStore(configService);
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

        // Start tracking focus time for the new tab
        if (newValue is TerminalPairTabViewModel newTerminalTab)
        {
            // Lazy-init file explorer for tabs that were deferred during startup restore
            if (newTerminalTab.DeferredExplorerInit != null)
            {
                var init = newTerminalTab.DeferredExplorerInit;
                newTerminalTab.DeferredExplorerInit = null;
                _ = init();
            }

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

        if (newValue != null)
        {
            // Clear unread activity indicator when tab is selected/focused
            newValue.ClearUnreadActivity();

            // Update workspace sidebar highlighting
            if (newValue is TerminalPairTabViewModel terminalTab)
            {
                WorkspaceSidebar?.ClearUnreadActivity(terminalTab.Pair.WorkingDirectory);
                WorkspaceSidebar?.UpdateCurrentTab(terminalTab.Pair.WorkingDirectory);
                WorkspaceSidebar?.UpdateContainerState(terminalTab.Pair.WorkingDirectory, terminalTab.IsContainerized);
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
        var sp = StartupProfiler.Instance;

        // Load quick commands from config
        using (sp.Measure("LoadQuickCommands"))
            LoadQuickCommands();

        // Load layout mode and initialize workspace sidebar
        sp.Log("InitializeWorkspaceSidebarAsync (fire-and-forget)");
        _ = InitializeWorkspaceSidebarAsync();

        // Restore previously open folders
        using (sp.Measure("RestoreOpenFolders"))
            RestoreOpenFolders();

        sp.Log("Starting timers");

        // Cache git tracking mode
        _gitTrackingMode = _configService.Load().Settings.GitTrackingMode;

        // Start the always-on signals.
        _projectMonitor.Start(SignalKind.Activity | SignalKind.Links | SignalKind.RunUrl);

        // Git signals depend on tracking mode + auto-fetch setting.
        if (_gitTrackingMode != GitTrackingMode.Disabled)
            _projectMonitor.Start(SignalKind.GitStatus);
        if (_gitTrackingMode == GitTrackingMode.All && _configService.Load().Settings.GitAutoFetch)
            _projectMonitor.Start(SignalKind.GitAutoFetch);

        sp.Log("Initialize — done");
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

            // UI property updates happen synchronously on the UI thread
            terminalTab.GitStatus = status;
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
                var sidebarStatus = await Task.Run(() => _gitStatusService.GetGitStatusAsync(workDir));
                var workspace = WorkspaceSidebar.GetAllWorkspaceEntries()
                    .FirstOrDefault(w => string.Equals(w.Path, workDir, StringComparison.OrdinalIgnoreCase));
                if (workspace != null)
                    workspace.GitStatus = sidebarStatus;
            }
        }
        catch
        {
            // Silently ignore git status errors
        }
    }

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

            // Step 5: Staleness warnings (non-blocking) — include project name for identification
            var projectName = Path.GetFileName(workspaceDir);
            if (result.IsConfigStale)
            {
                _toastService.Show(
                    $"Container for '{projectName}' has stale settings. Use 'Container: Recreate Current' from the command palette to apply.",
                    ToastType.Warning);
            }
            else if (result.IsImageStale && dockerfileStatus != DockerfileStatus.Stale)
            {
                _toastService.Show(
                    $"Container for '{projectName}' was built from an older image. Rebuild and recreate for latest tools.",
                    ToastType.Info);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start container for {workspaceDir}: {ex.Message}");
            _toastService.Show($"Docker container failed to start: {ex.Message}", ToastType.Error);
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
        IoCounters.CurrentUiOperation = "RefreshActivityState";
        try
        {
            // Update activity state for all terminal tabs (to detect idle transitions)
            foreach (var tab in Tabs.OfType<TerminalPairTabViewModel>())
            {
                tab.UpdateActivityState();
                tab.UpdateWaitingState(_inputPromptDetectionService);

                // Sync activity and container state to workspace sidebar
                WorkspaceSidebar?.UpdateActivity(
                    tab.Pair.WorkingDirectory,
                    tab.IsAnyTerminalActive,
                    tab.HasUnreadActivity,
                    tab.IsWaitingForInput);
                WorkspaceSidebar?.UpdateContainerState(
                    tab.Pair.WorkingDirectory,
                    tab.IsContainerized);
            }

            // Also update profile terminal tabs
            foreach (var tab in Tabs.OfType<ProfileTerminalTabViewModel>())
            {
                tab.UpdateActivityState();
                tab.UpdateWaitingState(_inputPromptDetectionService);
            }
        }
        finally { IoCounters.CurrentUiOperation = null; }
    }

    /// <summary>
    /// Automatically fetches from git remotes for all workspaces in the sidebar.
    /// This runs periodically to keep behind counts up to date.
    /// </summary>
    private async Task AutoFetchAllAsync()
    {
        // Respect git tracking mode — skip entirely if not in All mode
        if (_gitTrackingMode != GitTrackingMode.All) return;

        // Fetch git data in batches on the thread pool, then post ONE BeginInvoke
        // per batch to update UI properties. This prevents 540+ individual cross-thread
        // property change notifications from flooding the WPF dispatcher.
        var tabs = Tabs.OfType<TerminalPairTabViewModel>().ToList();
        var workspaceEntries = WorkspaceSidebar?.GetAllWorkspaceEntries() ?? [];
        const int batchSize = 5;

        // Phase 1: Fetch + refresh sidebar workspaces in batches
        for (var i = 0; i < workspaceEntries.Count; i += batchSize)
        {
            var batch = workspaceEntries.Skip(i).Take(batchSize).ToList();

            // Run git I/O on thread pool, collect results
            var results = new System.Collections.Concurrent.ConcurrentBag<(WorkspaceEntryViewModel vm, Core.Domain.GitStatus? status)>();
            await Task.Run(async () =>
            {
                await Task.WhenAll(batch.Select(async w =>
                {
                    try
                    {
                        await _gitStatusService.FetchAllAsync(w.Path);
                        var status = await _gitStatusService.GetGitStatusAsync(w.Path);
                        results.Add((w, status));
                    }
                    catch { /* Silently ignore fetch errors */ }
                }));
            });

            // Post ONE dispatcher call for the entire batch
            _dispatcherService.BeginInvoke(() =>
            {
                foreach (var (vm, status) in results)
                    vm.GitStatus = status;
            });
        }

        // Phase 2: Refresh open tab git status in batches
        for (var i = 0; i < tabs.Count; i += batchSize)
        {
            var batch = tabs.Skip(i).Take(batchSize).ToList();

            var results = new System.Collections.Concurrent.ConcurrentBag<(TerminalPairTabViewModel tab, Core.Domain.GitStatus? status)>();
            await Task.Run(async () =>
            {
                await Task.WhenAll(batch.Select(async tab =>
                {
                    try
                    {
                        var status = await _gitStatusService.GetGitStatusAsync(tab.Pair.WorkingDirectory);
                        results.Add((tab, status));
                    }
                    catch { /* Silently ignore git status errors */ }
                }));
            });

            _dispatcherService.BeginInvoke(() =>
            {
                foreach (var (tab, status) in results)
                    tab.GitStatus = status;
            });
        }

        _dispatcherService.BeginInvoke(() => OnPropertyChanged(nameof(WindowTitle)));
    }

    private void RefreshDetectedLinks()
    {
        IoCounters.CurrentUiOperation = "RefreshDetectedLinks";
        try
        {
            // Only refresh the selected tab to keep it lightweight
            if (SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.UpdateDetectedLinks(_linkDetectionService);
            }
        }
        finally { IoCounters.CurrentUiOperation = null; }
    }

    private void RefreshRunUrlDetection()
    {
        IoCounters.CurrentUiOperation = "RefreshRunUrlDetection";
        try
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
        finally { IoCounters.CurrentUiOperation = null; }
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

        var sp = StartupProfiler.Instance;

        // Pre-build directory settings lookup from the already-loaded config
        // so we don't re-read 145KB from disk per tab (was 2 loads + 1 save per tab).
        var dirSettingsLookup = config.DirectorySettings;

        // Collect tabs for deferred git refresh
        var restoredTabs = new List<TerminalPairTabViewModel>();

        // Pre-warm containers in parallel so the per-tab synchronous ensure is near-instant.
        // Without this, each tab blocks sequentially on docker inspect/start (~6s each).
        if (_containerService != null)
        {
            var existingFolders = config.OpenFolders.Where(_fileSystem.DirectoryExists).ToList();
            var containerSw = System.Diagnostics.Stopwatch.StartNew();
            var warmed = Task.Run(() => _containerService.PreWarmContainersAsync(existingFolders)).GetAwaiter().GetResult();
            if (warmed > 0)
                sp.Log($"Pre-warmed {warmed} containers in {containerSw.ElapsedMilliseconds}ms");
        }

        sp.Log($"Restoring {config.OpenFolders.Count} tabs");
        var tabSw = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < config.OpenFolders.Count; i++)
        {
            var folder = config.OpenFolders[i];
            if (_fileSystem.DirectoryExists(folder))
            {
                var tab = OpenProjectTabCore(folder, dirSettingsLookup, isRestore: true);
                if (tab != null) restoredTabs.Add(tab);
            }

            // Log every 10th tab and the last one
            if ((i + 1) % 10 == 0 || i == config.OpenFolders.Count - 1)
            {
                sp.Log($"  Tab {i + 1}/{config.OpenFolders.Count} — {tabSw.ElapsedMilliseconds}ms total");
            }
        }

        sp.Log($"All {restoredTabs.Count} tabs created in {tabSw.ElapsedMilliseconds}ms");

        // Defer all git status refreshes to after the UI is painted.
        // This lets the user see the tabs immediately instead of a frozen window.
        _ = DeferredGitRefreshAsync(restoredTabs);

        // Capture and stop deferring
        var pendingRestores = _deferredCenterPanelRestores;
        _deferredCenterPanelRestores = null;

        // Restore the last selected tab based on type
        var tabToSelect = _workspaceStateStore.FindLastSelectedTab(Tabs, lastTabType, config.LastSelectedFolder);
        if (tabToSelect != null)
        {
            SelectedTab = tabToSelect;
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

    /// <summary>
    /// Runs git status refresh for all restored tabs after yielding to the UI thread,
    /// so the window renders immediately instead of hanging during startup.
    /// Processes tabs sequentially to avoid spawning hundreds of git processes at once.
    /// </summary>
    private async Task DeferredGitRefreshAsync(List<TerminalPairTabViewModel> tabs)
    {
        // Yield once so WPF can render the tabs
        await Task.Delay(100);

        // Run git work on thread pool so Process.Start() calls don't block the UI thread
        await Task.Run(async () =>
        {
            foreach (var tab in tabs)
            {
                try
                {
                    var status = await _gitStatusService.GetGitStatusAsync(tab.Pair.WorkingDirectory);
                    _dispatcherService.BeginInvoke(() => tab.GitStatus = status);
                }
                catch { }
            }
        });
    }

    private void SaveDirectorySettings(TerminalPairTabViewModel tab)
    {
        _directorySettings.Update(tab.Pair.WorkingDirectory, settings =>
        {
            settings.LayoutMode = tab.LayoutMode;
            settings.SplitRatio = tab.SplitRatio;
            settings.ActiveTerminal = tab.ActiveTerminal.ToString();

            settings.IsRunTerminalVisible = tab.IsRunTerminalVisible;
            settings.RunSplitRatio = tab.RunSplitRatio;
            settings.ActiveRunConfigurationId = tab.ActiveRunConfiguration?.Id;
            settings.RunConfigurations = [.. tab.RunConfigurations];

            settings.IsExplorerVisible = tab.IsExplorerVisible;
            settings.ExplorerSplitRatio = tab.ExplorerSplitRatio;
            settings.IsLeftPanelVisible = tab.IsLeftPanelVisible;
            settings.LeftPanelSplitRatio = tab.LeftPanelSplitRatio;

            settings.ActiveCenterPanel = tab.ActiveCenterPanel?.PanelId;
            if (tab.ActiveCenterPanel is UnifiedGitPanelViewModel gitPanel)
            {
                settings.GitPanelActiveTab = gitPanel.ActiveTab.ToString();
            }

            settings.OpenRightPanels = tab.RightPanels.Select(p => p.PanelId).ToList();
            settings.ActiveRightPanel = tab.ActiveRightPanel?.PanelId;
        });
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
        var tab = OpenProjectTabCore(workingDirectory, null, isRestore: false, forceNew);
        if (tab != null)
        {
            // Only do these for interactive opens — during restore they are deferred/skipped
            _directorySettings.AddRecent(workingDirectory);
            _ = RefreshTabGitStatusAsync(tab);
            _ = WorkspaceSidebar?.SyncWithOpenTabAsync(workingDirectory);

            // Memory: auto-intake for this project via Eidet
            var eidet = App.Current?.Services?.GetService<IEidetService>();
            if (eidet != null)
                _ = eidet.OnProjectOpenedAsync(workingDirectory);
        }
    }

    /// <summary>
    /// Core tab creation shared by interactive opens and startup restore.
    /// When <paramref name="cachedDirSettings"/> is provided, skips the config reload per tab.
    /// When <paramref name="isRestore"/> is true, skips UpdateRecentFolders and defers git refresh.
    /// </summary>
    private TerminalPairTabViewModel? OpenProjectTabCore(
        string workingDirectory,
        IDictionary<string, DirectorySettings>? cachedDirSettings,
        bool isRestore,
        bool forceNew = false)
    {
        try
        {
            workingDirectory = WorkspaceService.NormalizeWorkingDirectory(workingDirectory);

            if (!_fileSystem.DirectoryExists(workingDirectory))
            {
                if (!isRestore) _dialogService.ShowError($"Directory not found: {workingDirectory}");
                return null;
            }

            if (!forceNew)
            {
                var existingTab = _workspace.FindByWorkingDirectory<TerminalPairTabViewModel>(workingDirectory).FirstOrDefault();
                if (existingTab != null)
                {
                    SelectedTab = existingTab;
                    return null;
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
                    // During restore, containers were pre-warmed in parallel by RestoreOpenFolders,
                    // so skip the blocking call to avoid sequential per-tab startup delays.
                    if (!isRestore)
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
            var pair = new TerminalPair(workingDirectory, customProfile, shellProfile, _statisticsService);

            // Create terminal controls for both
            var customControl = _terminalFactory.CreateTerminalControl(pair.CustomTerminal);
            var shellControl = _terminalFactory.CreateTerminalControl(pair.ShellTerminal);

            // Create view model with AI assistant info
            var tabViewModel = _tabFactory.CreateTerminalPairTab(pair, aiAssistant, enabledAssistants, settings.ShellCommandIcon, duplicateIndex);
            tabViewModel.IsContainerized = containerName != null;
            WorkspaceSidebar?.UpdateContainerState(workingDirectory, containerName != null);
            tabViewModel.AiAssistantSwitchRequested += OnAiAssistantSwitchRequested;
            tabViewModel.SetTerminalControls(customControl, shellControl);
            tabViewModel.CloseRequested += OnTabCloseRequested;
            // NOTE: SettingsChanged subscription is deferred until AFTER property restoration
            // to avoid 165 config Load+Save cycles during startup (was the #1 hang cause).

            // Restore per-directory settings — use cached lookup if available (avoids disk I/O)
            DirectorySettings? dirSettings;
            if (cachedDirSettings != null)
            {
                cachedDirSettings.TryGetValue(DirectorySettingsStore.NormalizeKey(workingDirectory), out dirSettings);
            }
            else
            {
                dirSettings = _directorySettings.Get(workingDirectory);
            }

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

            // Subscribe to settings changes AFTER all properties are restored
            // (setting properties above triggers SettingsChanged which would Save config per property)
            tabViewModel.SettingsChanged += OnTabSettingsChanged;

            // Wire up explorer events
            explorerViewModel.CdToShellRequested += (s, path) => tabViewModel.SendCdToShell(path);
            explorerViewModel.FileViewerRequested += OnExplorerFileViewerRequested;
            explorerViewModel.PopOutRequested += OnExplorerPopOutRequested;
            explorerViewModel.RenameRequested += OnExplorerRenameRequested;
            explorerViewModel.FileHistoryRequested += OnExplorerFileHistoryRequested;
            explorerViewModel.FileBlameRequested += OnExplorerFileBlameRequested;

            // Initialize explorer async — during restore, defer to avoid flooding
            // the dispatcher with 60 concurrent directory scans + git status checks.
            // Non-selected tabs will be initialized lazily when first selected.
            if (!isRestore)
            {
                _ = explorerViewModel.InitializeAsync(workingDirectory);
            }
            else
            {
                tabViewModel.DeferredExplorerInit = () => explorerViewModel.InitializeAsync(workingDirectory);
            }

            Tabs.Add(tabViewModel);

            // During restore, skip per-tab SelectedTab assignment — it triggers expensive
            // WPF data template loading for each of 60 tabs. Set once at end of restore instead.
            if (!isRestore)
            {
                SelectedTab = tabViewModel;
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

            return tabViewModel;
        }
        catch (Exception ex)
        {
            if (!isRestore) _dialogService.ShowError($"Error creating terminal: {ex.Message}");
            return null;
        }
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

        _workspace.RemoveAndPickNext(tab);
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
        _router.OpenSingleton<SettingsTabViewModel>(RefreshSettingsMemoryStatus);
    }

    private static void RefreshSettingsMemoryStatus(SettingsTabViewModel settingsTab)
    {
        var eidet = App.Current?.Services?.GetService<IEidetService>();
        settingsTab.UpdateMemoryStatus(eidet?.Status);
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
                // Refresh stats when re-focusing the existing tab (matches prior behavior).
                statsTab.LoadStatsCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"An error occurred while opening the statistics view:\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenTimeline()
    {
        try
        {
            var firstOpen = !_router.IsOpen<TimelineTabViewModel>();
            _router.OpenSingleton<TimelineTabViewModel>();
            if (firstOpen)
            {
                var config = _configService.Load();
                config.Settings.Timeline.ShowOnStartup = true;
                _configService.Save(config);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"An error occurred while opening Timeline Mode:\n\n{ex.Message}");
        }
    }

    private SparkCanvasViewModel? _sparkCanvasViewModel;

    internal void OpenSparkCanvas(string? sessionId = null)
    {
        try
        {
            if (SelectedTab is not TerminalPairTabViewModel terminalTab)
            {
                _toastService.Show("Select a project tab first", ToastType.Warning);
                return;
            }

            _sparkCanvasViewModel ??= _viewModelFactory.CreateSparkCanvas();

            if (sessionId != null)
            {
                _sparkCanvasViewModel.OpenSession(sessionId);
            }

            terminalTab.ShowCenterPanel(_sparkCanvasViewModel);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to open Spark Canvas:\n\n{ex.Message}");
        }
    }

    public void OpenSparkCanvasWindow(string? sessionId = null)
    {
        try
        {
            var vm = _viewModelFactory.CreateSparkCanvas();
            if (sessionId != null)
            {
                vm.OpenSession(sessionId);
            }

            var window = new Views.SparkCanvasWindow { DataContext = vm };
            window.Show();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to open Spark Canvas window:\n\n{ex.Message}");
        }
    }

    internal void OpenSparkCanvasAndLoadJsonl()
    {
        try
        {
            if (SelectedTab is not TerminalPairTabViewModel terminalTab)
            {
                _toastService.Show("Select a project tab first", ToastType.Warning);
                return;
            }

            _sparkCanvasViewModel ??= _viewModelFactory.CreateSparkCanvas();
            terminalTab.ShowCenterPanel(_sparkCanvasViewModel);

            // Trigger the file open dialog via the ViewModel's event
            _sparkCanvasViewModel.OpenJsonlFileCommand.Execute(null);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to open Spark Canvas:\n\n{ex.Message}");
        }
    }

    private void OnConfigSaved(object? sender, EventArgs e)
    {
        // Reload quick commands when config is saved
        LoadQuickCommands();

        // Refresh cached settings for NameProvider lambdas
        _cachedSettings = _configService.Load().Settings;

        // Reload touch mode setting and adjust sidebar width
        var newTouchMode = _cachedSettings.TouchMode;
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

        // Refresh git tracking mode and adjust timers accordingly
        var newGitTrackingMode = _configService.Load().Settings.GitTrackingMode;
        if (newGitTrackingMode != _gitTrackingMode)
        {
            _gitTrackingMode = newGitTrackingMode;
            switch (_gitTrackingMode)
            {
                case GitTrackingMode.All:
                    _projectMonitor.Start(SignalKind.GitStatus);
                    if (_configService.Load().Settings.GitAutoFetch)
                        _projectMonitor.Start(SignalKind.GitAutoFetch);
                    break;
                case GitTrackingMode.CurrentOnly:
                    _projectMonitor.Start(SignalKind.GitStatus);
                    _projectMonitor.Stop(SignalKind.GitAutoFetch);
                    break;
                case GitTrackingMode.Disabled:
                    _projectMonitor.Stop(SignalKind.GitStatus | SignalKind.GitAutoFetch);
                    break;
            }
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
    public event EventHandler? SessionsTreeRequested;
    public event EventHandler? TestRunnerRequested;
    public event EventHandler? WhatsNewRequested;
    public event EventHandler? MemoryBrowserRequested;
    public event EventHandler? DebugLogRequested;
    public event EventHandler<string>? AiPanelCommandRequested;

    /// <summary>
    /// Returns the static palette commands for the Recent Features page.
    /// </summary>
    internal IReadOnlyList<PaletteCommand> GetPaletteCommandsForFeatures() => _palette.Commands;

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

    internal void OpenUnifiedGitPanel(GitPanelTab tab)
    {
        UnifiedGitPanelRequested?.Invoke(this, tab);
    }

    // ── Event raise helpers for ICommandProvider classes (Step 2c) ──────
    // C# only allows events to be raised from the declaring class, so providers
    // call these wrappers instead of touching the events directly.
    internal void RequestPrReview() => PrReviewRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestReflog() => ReflogRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestRepositorySwitcher() => RepositorySwitcherRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestAiPanelCommand(string command) => AiPanelCommandRequested?.Invoke(this, command);

    // ── Helpers for ICommandProvider classes (Step 2d) ──────────────────
    internal StatusOverlayService? StatusOverlayService => _statusOverlayService;

    // ── Event raise helpers for ICommandProvider classes (Step 2e) ──────
    internal void RequestFilePreview(string path, int line, int column)
        => FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = path, Line = line, Column = column });
    internal void RequestSearch() => SearchRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestMarkdownPreview() => MarkdownPreviewRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestClaudeTasks() => ClaudeTasksRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestSessionsTree() => SessionsTreeRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestTestRunner() => TestRunnerRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestMemoryBrowser() => MemoryBrowserRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestDebugLog() => DebugLogRequested?.Invoke(this, EventArgs.Empty);
    internal void RequestWhatsNew() => WhatsNewRequested?.Invoke(this, EventArgs.Empty);

    internal void InstallTimelineHooks()
    {
        if (_timelineService.InstallHooks())
        {
            _toastService.Show("Session tracking hooks installed", ToastType.Success);
            var timeline = Tabs.OfType<TimelineTabViewModel>().FirstOrDefault();
            timeline?.InstallHooksCommand.Execute(null);
        }
        else
        {
            _toastService.Show("Failed to install hooks", ToastType.Error);
        }
    }

    internal void UninstallTimelineHooks()
    {
        if (_timelineService.UninstallHooks())
        {
            _toastService.Show("Session tracking hooks removed", ToastType.Success);
            var timeline = Tabs.OfType<TimelineTabViewModel>().FirstOrDefault();
            timeline?.UninstallHooksCommand.Execute(null);
        }
        else
        {
            _toastService.Show("Failed to remove hooks", ToastType.Error);
        }
    }

    internal void OpenTimelinePopout()
    {
        var windowVm = _viewModelFactory.CreateTimeline();
        var window = new Views.TimelineWindow { DataContext = windowVm };
        window.Show();
    }

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

    // ── Container command helpers ───────────────────────────────────────

    internal void ToggleContainerForCurrentWorkspace()
    {
        if (SelectedTab is not TerminalPairTabViewModel tab) return;
        var dir = tab.Pair.WorkingDirectory;
        var currentlyEnabled = _containerService?.IsEnabledForDirectory(dir) ?? false;
        var nowEnabled = !currentlyEnabled;
        _directorySettings.Update(dir, settings => settings.ContainerEnabled = nowEnabled);

        var state = nowEnabled ? "enabled" : "disabled";
        _toastService.Show($"Container {state} for {Path.GetFileName(dir)}. Reloading tab...", ToastType.Info);

        // Auto-reload the tab so container settings take effect immediately
        ReloadTerminalTab(tab);
    }

    internal void ReloadTerminalTab(TerminalPairTabViewModel tab)
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

    internal static string GetCrashLogDirectoryPath()
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

    private void OnTimelinePopOutRequested(object? sender, EventArgs e)
    {
        if (sender is not TimelineTabViewModel timelineTab) return;

        // Remove from tabs and dispose old VM (stops its timer and event subscriptions)
        timelineTab.CloseRequested -= OnTabCloseRequested;
        timelineTab.PopOutRequested -= OnTimelinePopOutRequested;
        Tabs.Remove(timelineTab);
        timelineTab.Dispose();

        // Create a new ViewModel for the standalone window
        var windowVm = _viewModelFactory.CreateTimeline();

        var window = new Views.TimelineWindow { DataContext = windowVm };
        window.Show();
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
        // Set layout mode synchronously so the UI renders correctly from the start
        var config = _configService.Load();
        LayoutMode = config.Settings.LayoutMode;

        // Yield so the fire-and-forget caller (Initialize) can proceed
        // to RestoreOpenFolders without waiting for sidebar git loads.
        await Task.Yield();

        if (WorkspaceSidebar != null)
        {
            // Load workspace VMs on the UI thread (they need ObservableCollection access),
            // but run the git status/worktree loads on the thread pool.
            await WorkspaceSidebar.LoadAsync();
        }
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

    #region API Server Helpers

    internal async Task StartApiServerAsync()
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

    internal async Task StopApiServerAsync()
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

    internal void CopyApiUrl()
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

    internal void OpenApiInBrowser()
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

    internal async Task TestWebhooksAsync()
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

    internal void ShowWebhookStats()
    {
        if (_webhookDeliveryService == null) return;

        var stats = _webhookDeliveryService.GetStats();
        var msg = $"Delivered: {stats.TotalDelivered} | Failed: {stats.TotalFailed} | Pending retries: {stats.PendingRetries}";
        _toastService.Show(msg, ToastType.Info);
    }

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
        _eventAggregator?.Publish(new ApiEvent
        {
            Type = "channel.user_message",
            RepoIndex = repoIndex,
            Data = new { message, sender = "user" }
        });

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
        // Use the already-loaded workspace sidebar state instead of re-reading
        // 145KB config from disk on every API request (was 927 loads in one session).
        var workspaces = WorkspaceSidebar?.GetAllWorkspaces();
        if (workspaces == null || workspaces.Count == 0)
        {
            var config = _configService.Load();
            workspaces = config.Workspaces;
        }
        var openRepos = BuildRepoList();

        return workspaces.Select(w =>
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
