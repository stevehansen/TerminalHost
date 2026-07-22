using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWindowsTerminalControl;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;
using TerminalHost.Services.Panels;

namespace TerminalHost.ViewModels;

public partial class TerminalPairTabViewModel : ObservableObject, ITabViewModel
{
    [ObservableProperty]
    private string _title = "Terminal";

    public string TabIcon => "📁";
    public string WorkingDirectory => Pair.WorkingDirectory;
    public bool IsCloseable => true;
    public bool CanDuplicate => true;

    public bool ConfirmCanClose(IDialogService dialogService, bool confirmIfBusy)
    {
        if (!confirmIfBusy) return true;
        if (!Pair.CustomTerminal.IsProcessRunning() && !Pair.ShellTerminal.IsProcessRunning()) return true;
        return dialogService.ShowConfirmation(
            $"Terminals in '{Title}' are still running. Close anyway?",
            "Confirm Close");
    }

    public ProjectTabApiState ToApiState() => new(
        Title: Title,
        WorkingDirectory: WorkingDirectory,
        Layout: LayoutMode.ToString(),
        SplitRatio: SplitRatio,
        ActiveTerminal: ActiveTerminal.ToString(),
        Git: GitStatus is null ? null : new ApiGitInfo
        {
            Branch = GitStatus.BranchName,
            IsDirty = GitStatus.IsDirty,
            Ahead = GitStatus.AheadCount,
            Behind = GitStatus.BehindCount,
            StashCount = GitStatus.StashCount
        },
        Terminals: new ApiTerminalsInfo
        {
            Custom = new ApiTerminalInfo
            {
                Title = CustomTerminalTitle ?? "",
                IsActive = ActiveTerminal == ActiveTerminal.Custom,
                IsBusy = Pair.CustomTerminal.IsActive,
                LastActivityAt = Pair.CustomTerminal.LastOutputTime?.ToUniversalTime(),
            },
            Shell = new ApiTerminalInfo
            {
                Title = ShellTerminalTitle ?? "",
                IsActive = ActiveTerminal == ActiveTerminal.Shell,
                IsBusy = Pair.ShellTerminal.IsActive,
                LastActivityAt = Pair.ShellTerminal.LastOutputTime?.ToUniversalTime(),
            },
            Run = Pair.RunTerminal is null ? null : new ApiTerminalInfo
            {
                Title = "Run",
                IsActive = ActiveTerminal == ActiveTerminal.Run,
                IsBusy = Pair.RunTerminal.IsActive,
                LastActivityAt = Pair.RunTerminal.LastOutputTime?.ToUniversalTime(),
            }
        },
        ActivityIndicator: new ApiActivityIndicator
        {
            State = IsAnyTerminalActive ? "busy"
                : IsWaitingForInput ? "waiting"
                : HasUnreadActivity ? "done"
                : "idle",
            HasUnreadActivity = HasUnreadActivity,
            IsWaitingForInput = IsWaitingForInput,
        },
        AiAssistant: ActiveAiAssistant is null ? null : new ApiAiAssistantInfo
        {
            Id = ActiveAiAssistant.Id,
            Name = ActiveAiAssistant.Name,
            Icon = ActiveAiAssistant.DisplayLabel
        });

    [ObservableProperty]
    private string _customIcon = "🤖";

    [ObservableProperty]
    private string _shellIcon = "💻";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomTerminalTitleDisplay))]
    private string _customTerminalTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellTerminalTitleDisplay))]
    private string _shellTerminalTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunTerminalTitleDisplay))]
    private string _runTerminalTitle = string.Empty;

    /// <summary>
    /// Formatted custom terminal title for display (includes " - " prefix when title exists).
    /// </summary>
    public string CustomTerminalTitleDisplay => string.IsNullOrEmpty(CustomTerminalTitle)
        ? string.Empty
        : $" - {CustomTerminalTitle}";

    /// <summary>
    /// Formatted shell terminal title for display (includes " - " prefix when title exists).
    /// </summary>
    public string ShellTerminalTitleDisplay => string.IsNullOrEmpty(ShellTerminalTitle)
        ? string.Empty
        : $" - {ShellTerminalTitle}";

    /// <summary>
    /// Formatted run terminal title for display (includes " - " prefix when title exists).
    /// </summary>
    public string RunTerminalTitleDisplay => string.IsNullOrEmpty(RunTerminalTitle)
        ? string.Empty
        : $" - {RunTerminalTitle}";

    [ObservableProperty]
    private ActiveTerminal _activeTerminal = ActiveTerminal.Custom;

    [ObservableProperty]
    private ContentControl? _customTerminalContent;

    [ObservableProperty]
    private ContentControl? _shellTerminalContent;

    [ObservableProperty]
    private TerminalLayoutMode _layoutMode = TerminalLayoutMode.HorizontalSplit;

    [ObservableProperty]
    private double _splitRatio = 0.6;  // Custom terminal takes 60% by default

    [ObservableProperty]
    private GitStatus? _gitStatus;

    [ObservableProperty]
    private bool _isCustomTerminalActive;

    [ObservableProperty]
    private bool _isShellTerminalActive;

    /// <summary>
    /// Tracks which terminal currently has focus (for clipboard operations).
    /// Defaults to Custom, updated when user clicks/focuses a terminal.
    /// </summary>
    [ObservableProperty]
    private ActiveTerminal _focusedTerminal = ActiveTerminal.Custom;

    // Run terminal properties
    [ObservableProperty]
    private ContentControl? _runTerminalContent;

    [ObservableProperty]
    private bool _isRunTerminalVisible;

    [ObservableProperty]
    private double _runSplitRatio = 0.3;

    // Explorer panel properties
    // IsExplorerVisible is computed from the right-dock surface's HasMounted flag; the surface
    // is the single source of truth for "any right-dock panel is mounted".
    public bool IsExplorerVisible => _rightDock?.HasMounted ?? false;

    [ObservableProperty]
    private double _explorerSplitRatio = 0.25;

    [ObservableProperty]
    private FileExplorerViewModel? _explorerViewModel;

    // Center panel properties
    /// <summary>
    /// The panel currently displayed in the center area, replacing terminals. Derived from
    /// the center surface's <c>MountedPanel</c>; null means terminals are visible.
    /// </summary>
    public IPanelableViewModel? ActiveCenterPanel => _centerSurface?.MountedPanel;

    /// <summary>
    /// Whether the terminal pair is visible (no center panel active).
    /// </summary>
    public bool IsTerminalsVisible => ActiveCenterPanel == null;

    /// <summary>
    /// The file explorer wrapped as a panel.
    /// </summary>
    public FileExplorerPanelViewModel? ExplorerPanelViewModel { get; private set; }

    /// <summary>
    /// The right-dock surface for this tab. Owns the dock's panel collection imperatively.
    /// </summary>
    private WpfRightDockSurface? _rightDock;
    /// <summary>The center surface for this tab. Single-slot; mirrors the right-dock pattern.</summary>
    private WpfCenterSurface? _centerSurface;
    private readonly IPanelRouter? _router;

    /// <summary>
    /// Dictionary of registered panels by PanelId.
    /// </summary>
    private readonly Dictionary<string, IPanelableViewModel> _registeredPanels = new();

    /// <summary>
    /// Gets a registered panel by ID.
    /// </summary>
    public T? GetPanel<T>(string panelId) where T : class, IPanelableViewModel
    {
        return _registeredPanels.TryGetValue(panelId, out var panel) ? panel as T : null;
    }

    /// <summary>
    /// The markdown preview panel (shared across all tabs, managed by MainWindow).
    /// </summary>
    public MarkdownPreviewViewModel? MarkdownPreviewPanel => GetPanel<MarkdownPreviewViewModel>("markdownPreview");

    /// <summary>
    /// The git changes panel (shared across all tabs, managed by MainWindow).
    /// </summary>
    public GitFilesViewModel? GitFilesPanel => GetPanel<GitFilesViewModel>("gitChanges");

    /// <summary>
    /// The scratch pad panel (shared across all tabs, managed by MainWindow).
    /// </summary>
    public ScratchPadViewModel? ScratchPadPanel => GetPanel<ScratchPadViewModel>("scratchPad");

    [ObservableProperty]
    private RunState _runState = RunState.Stopped;

    [ObservableProperty]
    private string? _detectedRunUrl;

    [ObservableProperty]
    private bool _isRunTerminalActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCompletedIndicator))]
    private bool _hasUnreadActivity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWaitingIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowCompletedIndicator))]
    private bool _isWaitingForInput;

    // Track previous activity state to detect transitions
    private bool _wasAnyTerminalActive;

    // Activity tracking window state
    private DateTime? _unfocusedAt;           // When tab was unfocused (switched away from)
    private bool _isTrackingActivity;          // Whether we're in the tracking window
    private const int GracePeriodSeconds = 5;  // Ignore activity for this long after unfocus
    private const int TrackingWindowSeconds = 30; // Track for this long after grace period

    /// <summary>
    /// Whether this tab is currently selected. Set by MainViewModel.
    /// </summary>
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            var wasSelected = _isSelected;
            _isSelected = value;

            if (wasSelected && !value)
            {
                // Tab was deselected - start tracking window
                _unfocusedAt = DateTime.Now;
                _isTrackingActivity = false;
            }
            else if (!wasSelected && value)
            {
                // Tab was selected - clear tracking state
                _unfocusedAt = null;
                _isTrackingActivity = false;
            }
        }
    }

    [ObservableProperty]
    private bool _isVisibleInFocusMode = true;

    /// <summary>
    /// Index for duplicate tabs of the same directory.
    /// 0 = first/original tab (no suffix), 2+ = duplicate tabs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private int _duplicateIndex;

    // AI Assistant support
    [ObservableProperty]
    private AiAssistant? _activeAiAssistant;

    [ObservableProperty]
    private ObservableCollection<AiAssistant> _availableAiAssistants = [];

    [ObservableProperty]
    private AiAssistant? _selectedAiAssistant;

    private bool _suppressAiSwitchEvent;

    /// <summary>
    /// Whether multiple AI assistants are enabled (controls visibility of selector).
    /// </summary>
    public bool HasMultipleAiAssistants => AvailableAiAssistants.Count > 1;

    /// <summary>
    /// Updates visibility based on focus mode state.
    /// </summary>
    public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects)
    {
        if (!isFocusModeEnabled || currentTaskProjects.Count == 0)
        {
            // Focus mode disabled or no projects in task = show all tabs
            IsVisibleInFocusMode = true;
            return;
        }

        // Check if this tab's working directory matches any project in the current task
        var normalizedPath = WorkingDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        IsVisibleInFocusMode = currentTaskProjects.Any(p =>
            p.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    [ObservableProperty]
    private RunConfiguration? _activeRunConfiguration;

    /// <summary>
    /// Collection of available run configurations for this project.
    /// </summary>
    public ObservableCollection<RunConfiguration> RunConfigurations { get; } = [];

    /// <summary>
    /// True if either custom or shell terminal is currently producing output.
    /// Run terminal is excluded from activity tracking.
    /// </summary>
    public bool IsAnyTerminalActive => IsCustomTerminalActive || IsShellTerminalActive;

    /// <summary>
    /// True if activity spinner should be shown.
    /// Only shows when within the tracking window for unfocused tabs.
    /// </summary>
    public bool ShowActivitySpinner => IsAnyTerminalActive && (IsSelected || _isTrackingActivity);

    /// <summary>
    /// True if completed indicator should be shown.
    /// Shows when activity finished but NOT when waiting for input (waiting takes precedence).
    /// </summary>
    public bool ShowCompletedIndicator => HasUnreadActivity && !IsAnyTerminalActive && !IsWaitingForInput;

    /// <summary>
    /// True if waiting for input indicator should be shown.
    /// Shows when terminal is waiting for user input and tab is NOT selected.
    /// </summary>
    public bool ShowWaitingIndicator => IsWaitingForInput && !IsSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowClaudeTaskIndicator))]
    private bool _hasActiveClaudeTasks;

    /// <summary>
    /// True if Claude task indicator (blue robot) should be shown.
    /// Shows when there are active Claude tasks for this workspace.
    /// </summary>
    public bool ShowClaudeTaskIndicator => HasActiveClaudeTasks;

    /// <summary>
    /// True if terminals have been initialized.
    /// </summary>
    public bool IsTerminalInitialized => Pair?.CustomTerminal?.TerminalControl != null;

    /// <summary>
    /// Initializes terminals asynchronously.
    /// </summary>
    public Task InitializeTerminalsAsync() => Task.CompletedTask;

    /// <summary>
    /// Collection of detected links from terminal output.
    /// </summary>
    public ObservableCollection<DetectedLink> DetectedLinks { get; } = [];

    /// <summary>
    /// MRU cache of detected links - keeps links even after they scroll out of the terminal buffer.
    /// Key is URL (case-insensitive), value is (Link, LastSeenTime).
    /// </summary>
    private readonly Dictionary<string, (DetectedLink Link, DateTime LastSeen)> _linkCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCachedLinks = 50;

    /// <summary>
    /// True if there are any detected links to display.
    /// </summary>
    public bool HasDetectedLinks => DetectedLinks.Count > 0;

    // Layout mode computed properties
    public bool IsCustomFullMode => LayoutMode == TerminalLayoutMode.CustomFull;
    public bool IsHorizontalSplitMode => LayoutMode == TerminalLayoutMode.HorizontalSplit;
    public bool IsVerticalSplitMode => LayoutMode == TerminalLayoutMode.VerticalSplit;
    public bool IsShellVisible => LayoutMode != TerminalLayoutMode.CustomFull;

    // For Custom terminal spanning - needs to span all rows except in vertical mode, all columns except in horizontal mode
    public bool ShouldCustomSpanAllRows => LayoutMode != TerminalLayoutMode.VerticalSplit;
    public bool ShouldCustomSpanAllColumns => LayoutMode != TerminalLayoutMode.HorizontalSplit;

    // Computed column widths for horizontal split (columns)
    public GridLength CustomColumnWidth => LayoutMode switch
    {
        TerminalLayoutMode.CustomFull => new GridLength(1, GridUnitType.Star),
        TerminalLayoutMode.HorizontalSplit => new GridLength(SplitRatio, GridUnitType.Star),
        TerminalLayoutMode.VerticalSplit => new GridLength(1, GridUnitType.Star),
        _ => new GridLength(SplitRatio, GridUnitType.Star)
    };

    public GridLength ShellColumnWidth => LayoutMode switch
    {
        TerminalLayoutMode.CustomFull => new GridLength(0, GridUnitType.Pixel),
        TerminalLayoutMode.HorizontalSplit => new GridLength(1 - SplitRatio, GridUnitType.Star),
        TerminalLayoutMode.VerticalSplit => new GridLength(0, GridUnitType.Pixel),
        _ => new GridLength(1 - SplitRatio, GridUnitType.Star)
    };

    public GridLength MainSplitterWidth => LayoutMode == TerminalLayoutMode.HorizontalSplit
        ? new GridLength(4, GridUnitType.Pixel)
        : new GridLength(0, GridUnitType.Pixel);

    // Computed row heights for vertical split (rows)
    public GridLength CustomRowHeight => LayoutMode switch
    {
        TerminalLayoutMode.CustomFull => new GridLength(1, GridUnitType.Star),
        TerminalLayoutMode.HorizontalSplit => new GridLength(1, GridUnitType.Star),
        TerminalLayoutMode.VerticalSplit => new GridLength(SplitRatio, GridUnitType.Star),
        _ => new GridLength(1, GridUnitType.Star)
    };

    public GridLength ShellRowHeight => LayoutMode switch
    {
        TerminalLayoutMode.CustomFull => new GridLength(0, GridUnitType.Pixel),
        TerminalLayoutMode.HorizontalSplit => new GridLength(0, GridUnitType.Pixel),
        TerminalLayoutMode.VerticalSplit => new GridLength(1 - SplitRatio, GridUnitType.Star),
        _ => new GridLength(0, GridUnitType.Pixel)
    };

    public GridLength VerticalSplitterHeight => LayoutMode == TerminalLayoutMode.VerticalSplit
        ? new GridLength(4, GridUnitType.Pixel)
        : new GridLength(0, GridUnitType.Pixel);

    // Main terminals column width - takes remaining space after run (within MainContentGrid)
    public GridLength MainTerminalsColumnWidth
    {
        get
        {
            double runPortion = IsRunTerminalVisible ? RunSplitRatio : 0;
            double mainPortion = 1.0 - runPortion;
            return new GridLength(Math.Max(0.1, mainPortion), GridUnitType.Star);
        }
    }

    // Run terminal column width (only shown when visible)
    // Use Pixel unit with 0 when hidden so it doesn't participate in star distribution
    public GridLength RunColumnWidth => IsRunTerminalVisible
        ? new GridLength(RunSplitRatio, GridUnitType.Star)
        : new GridLength(0, GridUnitType.Pixel);

    public GridLength RunSplitterWidth => IsRunTerminalVisible
        ? new GridLength(4, GridUnitType.Pixel)
        : new GridLength(0, GridUnitType.Pixel);

    // Run state computed properties
    public bool CanRun => RunState == RunState.Stopped && ActiveRunConfiguration != null && !string.IsNullOrWhiteSpace(ActiveRunConfiguration.Command);
    public bool CanStop => RunState == RunState.Running || RunState == RunState.Starting;
    public bool HasDetectedRunUrl => !string.IsNullOrEmpty(DetectedRunUrl);
    public bool HasMultipleRunConfigs => RunConfigurations.Count(c => !string.IsNullOrWhiteSpace(c.Command)) > 1;
    public bool HasAnyRunConfiguration => RunConfigurations.Any(c => !string.IsNullOrWhiteSpace(c.Command));

    // Container state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleWithGit))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(ContainerTooltip))]
    private bool _isContainerized;

    public string? ContainerTooltip => IsContainerized ? "Running in Docker container" : null;

    // Git display properties
    public string TitleWithGit => (IsContainerized ? "🐳 " : "") +
        (GitStatus?.IsGitRepository == true
            ? $"{Title} {GitStatus.BranchDisplayShort}"
            : Title);

    public string DisplayTitle => DuplicateIndex > 0
        ? $"{TitleWithGit} ({DuplicateIndex})"
        : TitleWithGit;

    public string GitStatusDisplay => GitStatus?.StatusDisplayFull ?? "";

    public string GitPullButtonText => GitStatus?.BehindCount > 0 ? $"↓ {GitStatus.BehindCount}" : "↓";
    public string GitPushButtonText => GitStatus?.AheadCount > 0 ? $"↑ {GitStatus.AheadCount}" : "↑";

    public TerminalPair Pair { get; }
    private readonly IStatisticsService _statisticsService;
    private readonly ITaskService? _taskService;
    private readonly IGitStatusService? _gitStatusService;
    private readonly IToastService? _toastService;
    private readonly ISessionLifecycleCoordinator? _sessionCoordinator;

    public string CurrentIcon => ActiveTerminal == ActiveTerminal.Custom ? CustomIcon : ShellIcon;

    public ContentControl? CurrentTerminalContent => ActiveTerminal == ActiveTerminal.Custom
        ? CustomTerminalContent
        : ShellTerminalContent;

    public event EventHandler? CloseRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler<AiAssistantSwitchEventArgs>? AiAssistantSwitchRequested;

    /// <summary>
    /// Deferred file explorer initialization for tabs restored at startup.
    /// Called once when the tab is first selected. Set to null after execution.
    /// </summary>
    public Func<Task>? DeferredExplorerInit { get; set; }

    public TerminalPairTabViewModel(TerminalPair pair, string customIcon, string shellIcon, IStatisticsService statisticsService, IGitStatusService? gitStatusService = null, IToastService? toastService = null, int duplicateIndex = 0, ITaskService? taskService = null, ISessionLifecycleCoordinator? sessionCoordinator = null, IPanelRouter? router = null)
    {
        Pair = pair;
        Title = pair.DirectoryName;
        CustomIcon = customIcon;
        ShellIcon = shellIcon;
        _statisticsService = statisticsService;
        _gitStatusService = gitStatusService;
        _toastService = toastService;
        _taskService = taskService;
        _sessionCoordinator = sessionCoordinator;
        _router = router;
        ActiveTerminal = pair.ActiveTerminal;
        DuplicateIndex = duplicateIndex;

        InitializeRightDockSurface();
        InitializeCenterSurface();

        // Subscribe to task changes for Claude task indicator
        if (_taskService != null)
        {
            _taskService.TasksChanged += OnTasksChanged;
            RefreshClaudeTaskIndicator();
        }
    }

    public TerminalPairTabViewModel(TerminalPair pair, AiAssistant activeAiAssistant, IReadOnlyList<AiAssistant> enabledAssistants, string shellIcon, IStatisticsService statisticsService, IGitStatusService? gitStatusService = null, IToastService? toastService = null, int duplicateIndex = 0, ITaskService? taskService = null, ISessionLifecycleCoordinator? sessionCoordinator = null, IPanelRouter? router = null)
    {
        Pair = pair;
        Title = pair.DirectoryName;
        ActiveAiAssistant = activeAiAssistant;
        SelectedAiAssistant = activeAiAssistant;
        CustomIcon = activeAiAssistant.DisplayLabel;
        ShellIcon = shellIcon;
        _statisticsService = statisticsService;
        _gitStatusService = gitStatusService;
        _toastService = toastService;
        _taskService = taskService;
        _sessionCoordinator = sessionCoordinator;
        _router = router;
        ActiveTerminal = pair.ActiveTerminal;
        DuplicateIndex = duplicateIndex;

        // Populate available assistants
        foreach (var assistant in enabledAssistants)
        {
            AvailableAiAssistants.Add(assistant);
        }

        InitializeRightDockSurface();
        InitializeCenterSurface();

        // Subscribe to task changes for Claude task indicator
        if (_taskService != null)
        {
            _taskService.TasksChanged += OnTasksChanged;
            RefreshClaudeTaskIndicator();
        }
    }

    private void InitializeRightDockSurface()
    {
        if (_router is null) return;
        var scope = TabPanelScope.ForTab(Pair.WorkingDirectory);
        _rightDock = new WpfRightDockSurface(scope);
        _rightDock.PropertyChanged += OnRightDockPropertyChanged;
        _router.RegisterSurface(_rightDock);
    }

    private void InitializeCenterSurface()
    {
        if (_router is null) return;
        var scope = TabPanelScope.ForTab(Pair.WorkingDirectory);
        _centerSurface = new WpfCenterSurface(scope);
        _centerSurface.PropertyChanged += OnCenterSurfacePropertyChanged;
        _router.RegisterSurface(_centerSurface);
    }

    private void OnCenterSurfacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WpfCenterSurface.MountedPanel))
        {
            OnPropertyChanged(nameof(ActiveCenterPanel));
            OnPropertyChanged(nameof(IsTerminalsVisible));
        }
    }

    private void OnRightDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WpfRightDockSurface.HasMounted))
        {
            OnPropertyChanged(nameof(IsExplorerVisible));
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Tab scope used by the right-dock surface and persistence. Exposed for the host so it can
    /// project router operations to this tab.
    /// </summary>
    public PanelScope RightDockScope => _rightDock?.Scope ?? TabPanelScope.ForTab(Pair.WorkingDirectory);

    /// <summary>Tab scope used by the center surface and persistence (same scope as right-dock).</summary>
    public PanelScope CenterScope => _centerSurface?.Scope ?? TabPanelScope.ForTab(Pair.WorkingDirectory);

    /// <summary>
    /// This tab's right-dock surface, handed to the main-window-owned dock coordinator on tab switch
    /// so its panels can be merged into the single hoisted dock. Null until the surface is created.
    /// </summary>
    public WpfRightDockSurface? RightDockSurface => _rightDock;

    /// <summary>
    /// Replays persisted tab-scope state (Center + RightDock) through the router, mounting any
    /// panels the resolver can produce. Call after singleton panels have been registered via
    /// <see cref="SetPanel"/>. Both zones share the same scope, so one Restore call covers both.
    /// OnOpenedAsync is suppressed during Restore — call <see cref="HydrateActiveCenterPanelAsync"/>
    /// on the selected tab after the restore loop to trigger data loads.
    /// </summary>
    public void RestoreTabPanels()
    {
        if (_router is null || _rightDock is null) return;
        _router.Restore(_rightDock.Scope, panelId => _registeredPanels.GetValueOrDefault(panelId));
    }

    /// <summary>
    /// Invokes <see cref="IPanelOpenContext.OnOpenedAsync"/> on the currently mounted center
    /// panel (if any). Hosts call this on the SELECTED tab only, after the per-tab Restore loop,
    /// so non-selected tabs stay placed-but-not-hydrated until the user switches to them.
    /// </summary>
    public Task HydrateActiveCenterPanelAsync()
    {
        if (ActiveCenterPanel is IPanelOpenContext ctx)
            return ctx.OnOpenedAsync(this);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Toggles a panel in the right dock via the router. Acts as a one-line replacement for the
    /// host's old <c>TogglePanel</c>/<c>ShowPanel</c> entry points so callers don't repeat the
    /// zone/scope plumbing at every site.
    /// </summary>
    public void ShowRightDockPanel(IPanelableViewModel panel)
    {
        if (_router is null) return;
        _router.Show(panel, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: RightDockScope));
    }

    /// <summary>
    /// Forces a panel into the right dock (no toggle) and makes it active.
    /// </summary>
    public void ForceShowRightDockPanel(IPanelableViewModel panel)
    {
        if (_router is null) return;
        _router.Show(panel, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: RightDockScope, ForceShow: true));
    }

    partial void OnSelectedAiAssistantChanged(AiAssistant? oldValue, AiAssistant? newValue)
    {
        // Only fire event if the user actually changed the selection (not suppressed)
        if (_suppressAiSwitchEvent)
            return;

        if (newValue != null && oldValue != null && newValue.Id != oldValue.Id)
        {
            AiAssistantSwitchRequested?.Invoke(this, new AiAssistantSwitchEventArgs { NewAssistant = newValue });
        }
    }

    public void SetTerminalControls(EasyTerminalControl customControl, EasyTerminalControl shellControl)
    {
        CustomTerminalContent = customControl;
        ShellTerminalContent = shellControl;

        Pair.CustomTerminal.SetTerminalControl(customControl);
        Pair.ShellTerminal.SetTerminalControl(shellControl);

        // Subscribe to activity changes for immediate UI updates
        Pair.CustomTerminal.ActivityChanged += (s, e) =>
        {
            IsCustomTerminalActive = Pair.CustomTerminal.IsActive;
        };
        Pair.ShellTerminal.ActivityChanged += (s, e) =>
        {
            IsShellTerminalActive = Pair.ShellTerminal.IsActive;
        };

        // Subscribe to title changes
        Pair.CustomTerminal.TitleChanged += (s, title) =>
        {
            CustomTerminalTitle = title;
            // Claude's terminal title (spinner while working, idle icon when done) is an
            // authoritative session-state signal that survives missed Stop/Activity hooks.
            _sessionCoordinator?.RecordTerminalTitleActivity(WorkingDirectory, title);
        };
        Pair.ShellTerminal.TitleChanged += (s, title) =>
        {
            ShellTerminalTitle = title;
        };

        // Note: Focus tracking for clipboard operations is handled in TerminalPairView.xaml.cs
        // via MouseDown handlers on the Grid containers (works better with HWND-hosted controls)

        // Notify that CurrentTerminalContent has changed
        OnPropertyChanged(nameof(CurrentTerminalContent));
    }

    /// <summary>
    /// Sets the custom terminal control when switching AI assistant.
    /// </summary>
    public void SetCustomTerminalControl(EasyTerminalControl newControl)
    {
        CustomTerminalContent = newControl;
        Pair.CustomTerminal.SetTerminalControl(newControl);

        // Subscribe to activity changes
        Pair.CustomTerminal.ActivityChanged += (s, e) =>
        {
            IsCustomTerminalActive = Pair.CustomTerminal.IsActive;
        };

        // Subscribe to title changes
        Pair.CustomTerminal.TitleChanged += (s, title) =>
        {
            CustomTerminalTitle = title;
            _sessionCoordinator?.RecordTerminalTitleActivity(WorkingDirectory, title);
        };

        // Reset title for new terminal
        CustomTerminalTitle = string.Empty;

        OnPropertyChanged(nameof(CurrentTerminalContent));
    }

    /// <summary>
    /// Updates the active AI assistant after switching.
    /// </summary>
    public void UpdateActiveAiAssistant(AiAssistant newAssistant)
    {
        ActiveAiAssistant = newAssistant;
        CustomIcon = newAssistant.DisplayLabel;

        // Update selected without triggering the change event
        _suppressAiSwitchEvent = true;
        SelectedAiAssistant = newAssistant;
        _suppressAiSwitchEvent = false;
    }

    /// <summary>
    /// Refreshes the available AI assistants list after settings change.
    /// </summary>
    public void RefreshAvailableAiAssistants(IReadOnlyList<AiAssistant> enabledAssistants)
    {
        _suppressAiSwitchEvent = true;

        AvailableAiAssistants.Clear();
        foreach (var assistant in enabledAssistants)
        {
            AvailableAiAssistants.Add(assistant);
        }

        // Update selected to match current active (find by ID)
        if (ActiveAiAssistant != null)
        {
            var matchingAssistant = enabledAssistants.FirstOrDefault(a => a.Id == ActiveAiAssistant.Id);
            if (matchingAssistant != null)
            {
                SelectedAiAssistant = matchingAssistant;
                ActiveAiAssistant = matchingAssistant;
                CustomIcon = matchingAssistant.DisplayLabel;
            }
            else if (enabledAssistants.Count > 0)
            {
                // Current assistant was disabled, switch to first enabled
                var firstEnabled = enabledAssistants[0];
                SelectedAiAssistant = firstEnabled;
                ActiveAiAssistant = firstEnabled;
                CustomIcon = firstEnabled.DisplayLabel;
            }
        }

        _suppressAiSwitchEvent = false;
        OnPropertyChanged(nameof(HasMultipleAiAssistants));
    }

    [RelayCommand]
    private void SwitchTerminal()
    {
        Pair.SwitchTerminal();
        ActiveTerminal = Pair.ActiveTerminal;
    }

    [RelayCommand]
    private void ShowCustomTerminal()
    {
        if (ActiveTerminal != ActiveTerminal.Custom)
        {
            Pair.ActiveTerminal = ActiveTerminal.Custom;
            ActiveTerminal = ActiveTerminal.Custom;
        }
    }

    [RelayCommand]
    private void ShowShellTerminal()
    {
        if (ActiveTerminal != ActiveTerminal.Shell)
        {
            Pair.ActiveTerminal = ActiveTerminal.Shell;
            ActiveTerminal = ActiveTerminal.Shell;
        }
    }

    [RelayCommand]
    private void SetLayoutMode(TerminalLayoutMode mode)
    {
        LayoutMode = mode;
    }

    [RelayCommand]
    private void SetCustomFullLayout()
    {
        LayoutMode = TerminalLayoutMode.CustomFull;
        // Focus custom terminal when switching to full view
        FocusedTerminal = ActiveTerminal.Custom;
        Pair.CustomTerminal.Focus();
    }

    [RelayCommand]
    private void SetHorizontalSplitLayout()
    {
        LayoutMode = TerminalLayoutMode.HorizontalSplit;
        // Focus shell terminal when switching to split view (shell is being revealed)
        FocusedTerminal = ActiveTerminal.Shell;
        Pair.ShellTerminal.Focus();
    }

    [RelayCommand]
    private void SetVerticalSplitLayout()
    {
        LayoutMode = TerminalLayoutMode.VerticalSplit;
        // Focus shell terminal when switching to split view (shell is being revealed)
        FocusedTerminal = ActiveTerminal.Shell;
        Pair.ShellTerminal.Focus();
    }

    [RelayCommand]
    private async Task GitPullAsync()
    {
        if (GitStatus?.IsGitRepository != true || _gitStatusService == null || _toastService == null) return;
        var workDir = Pair.WorkingDirectory;
        var isDirty = GitStatus.IsDirty;
        using var toast = _toastService.ShowProgress(isDirty ? "Stashing & pulling..." : "Pulling...");

        // Stash if dirty to avoid pull failures
        if (isDirty)
        {
            var stashResult = await _gitStatusService.CreateStashAsync(workDir, "auto-stash before pull", includeUntracked: true);
            if (!stashResult.Success)
            {
                toast.Fail($"Stash failed: {stashResult.Error}");
                return;
            }
        }

        var result = await _gitStatusService.PullRebaseAsync(workDir);

        // Pop stash if we stashed
        if (isDirty)
        {
            var popResult = await _gitStatusService.PopStashAsync(workDir, 0);
            if (!popResult.Success)
            {
                // Pull may have succeeded but pop failed (conflicts)
                toast.Fail(result.Success
                    ? $"Pull succeeded but stash pop failed: {popResult.Error}"
                    : $"Pull failed: {result.Error}; stash pop also failed: {popResult.Error}");
                GitStatus = await _gitStatusService.GetGitStatusAsync(workDir);
                return;
            }
        }

        if (result.Success)
        {
            toast.Complete("Pull complete");
            GitStatus = await _gitStatusService.GetGitStatusAsync(workDir);
        }
        else
            toast.Fail($"Pull failed: {result.Error}");
    }

    [RelayCommand]
    private async Task GitPushAsync()
    {
        if (GitStatus?.IsGitRepository != true || _gitStatusService == null || _toastService == null) return;
        using var toast = _toastService.ShowProgress("Pushing...");
        var result = await _gitStatusService.PushAsync(Pair.WorkingDirectory);
        if (result.Success)
        {
            toast.Complete("Push complete");
            GitStatus = await _gitStatusService.GetGitStatusAsync(Pair.WorkingDirectory);
        }
        else
            toast.Fail($"Push failed: {result.Error}");
    }

    partial void OnLayoutModeChanged(TerminalLayoutMode value)
    {
        OnPropertyChanged(nameof(IsCustomFullMode));
        OnPropertyChanged(nameof(IsHorizontalSplitMode));
        OnPropertyChanged(nameof(IsVerticalSplitMode));
        OnPropertyChanged(nameof(IsShellVisible));
        OnPropertyChanged(nameof(ShouldCustomSpanAllRows));
        OnPropertyChanged(nameof(ShouldCustomSpanAllColumns));
        OnPropertyChanged(nameof(CustomColumnWidth));
        OnPropertyChanged(nameof(ShellColumnWidth));
        OnPropertyChanged(nameof(MainSplitterWidth));
        OnPropertyChanged(nameof(CustomRowHeight));
        OnPropertyChanged(nameof(ShellRowHeight));
        OnPropertyChanged(nameof(VerticalSplitterHeight));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSplitRatioChanged(double value)
    {
        OnPropertyChanged(nameof(CustomColumnWidth));
        OnPropertyChanged(nameof(ShellColumnWidth));
        OnPropertyChanged(nameof(CustomRowHeight));
        OnPropertyChanged(nameof(ShellRowHeight));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActiveTerminalChanged(ActiveTerminal value)
    {
        OnPropertyChanged(nameof(CurrentIcon));
        OnPropertyChanged(nameof(CurrentTerminalContent));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnGitStatusChanged(GitStatus? value)
    {
        OnPropertyChanged(nameof(TitleWithGit));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(GitStatusDisplay));
        OnPropertyChanged(nameof(GitPullButtonText));
        OnPropertyChanged(nameof(GitPushButtonText));

        // Refresh file explorer's git status when tab git status changes
        if (ExplorerViewModel != null)
        {
            _ = ExplorerViewModel.RefreshGitStatusAsync();
        }
    }

    partial void OnIsCustomTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
        OnPropertyChanged(nameof(ShowCompletedIndicator));
        CheckActivityTransition();
    }

    partial void OnIsShellTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
        OnPropertyChanged(nameof(ShowCompletedIndicator));
        CheckActivityTransition();
    }

    partial void OnIsRunTerminalActiveChanged(bool value)
    {
        // Run terminal is excluded from activity tracking - no need to update IsAnyTerminalActive
    }

    /// <summary>
    /// Checks for activity state transitions and updates HasUnreadActivity accordingly.
    /// Uses a tracking window to prevent false positives:
    /// - 5 second grace period after unfocusing (ignores activity)
    /// - 30 second tracking window after grace period
    /// - After tracking window expires with no activity, stops tracking
    /// </summary>
    private void CheckActivityTransition()
    {
        var isCurrentlyActive = IsAnyTerminalActive;

        // For selected tabs, just track transitions without setting unread
        if (IsSelected)
        {
            _wasAnyTerminalActive = isCurrentlyActive;
            OnPropertyChanged(nameof(ShowActivitySpinner));
            return;
        }

        // For unfocused tabs, use the tracking window
        if (_unfocusedAt.HasValue)
        {
            var elapsed = (DateTime.Now - _unfocusedAt.Value).TotalSeconds;

            if (elapsed < GracePeriodSeconds)
            {
                // Within grace period - ignore all activity
                _wasAnyTerminalActive = isCurrentlyActive;
                return;
            }

            if (elapsed < GracePeriodSeconds + TrackingWindowSeconds)
            {
                // Within tracking window - track normally
                if (isCurrentlyActive)
                {
                    _isTrackingActivity = true;
                }

                // Transition from active to idle: mark as unread
                if (_wasAnyTerminalActive && !isCurrentlyActive)
                {
                    HasUnreadActivity = true;
                }
            }
            else
            {
                // Past tracking window
                if (!isCurrentlyActive)
                {
                    // No activity - stop tracking entirely
                    _isTrackingActivity = false;
                }
                // If still active past the window, keep showing but don't start new tracking
            }
        }
        else
        {
            // No unfocus timestamp (shouldn't happen for unselected tabs, but handle gracefully)
            if (_wasAnyTerminalActive && !isCurrentlyActive)
            {
                HasUnreadActivity = true;
            }
        }

        _wasAnyTerminalActive = isCurrentlyActive;
        OnPropertyChanged(nameof(ShowActivitySpinner));
    }

    /// <summary>
    /// Clears the unread activity state. Called when the tab becomes focused/selected.
    /// Also resets the transition tracking and tracking window state to prevent false
    /// positives from terminal rendering/focus events that briefly trigger activity.
    /// </summary>
    public void ClearUnreadActivity()
    {
        HasUnreadActivity = false;
        // Sync tracking state to current state to avoid false transition detection
        _wasAnyTerminalActive = IsAnyTerminalActive;
        // Reset tracking window state
        _unfocusedAt = null;
        _isTrackingActivity = false;
        OnPropertyChanged(nameof(ShowActivitySpinner));
    }

    partial void OnIsRunTerminalVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(RunColumnWidth));
        OnPropertyChanged(nameof(RunSplitterWidth));
        OnPropertyChanged(nameof(MainTerminalsColumnWidth));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnRunSplitRatioChanged(double value)
    {
        OnPropertyChanged(nameof(RunColumnWidth));
        OnPropertyChanged(nameof(MainTerminalsColumnWidth));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnRunStateChanged(RunState value)
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanStop));
    }

    partial void OnDetectedRunUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDetectedRunUrl));
    }

    partial void OnActiveRunConfigurationChanged(RunConfiguration? value)
    {
        OnPropertyChanged(nameof(CanRun));
    }

    partial void OnExplorerSplitRatioChanged(double value)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// Updates activity state from the terminal sessions.
    /// </summary>
    public void UpdateActivityState()
    {
        // Check for idle transitions
        Pair.CustomTerminal.CheckActivityState();
        Pair.ShellTerminal.CheckActivityState();

        // Update properties
        IsCustomTerminalActive = Pair.CustomTerminal.IsActive;
        IsShellTerminalActive = Pair.ShellTerminal.IsActive;
    }

    /// <summary>
    /// Updates the waiting for input state by checking terminal output against patterns.
    /// Called periodically when the terminal is idle.
    /// </summary>
    /// <param name="inputPromptDetectionService">The service to detect input prompts.</param>
    public void UpdateWaitingState(IInputPromptDetectionService inputPromptDetectionService)
    {
        // Only check for waiting state if:
        // 1. Detection is enabled
        // 2. Custom terminal is not actively producing output
        // 3. Custom terminal has been idle for the minimum time
        if (!inputPromptDetectionService.IsEnabled)
        {
            IsWaitingForInput = false;
            return;
        }

        // If terminal is actively producing output, it's not waiting
        if (IsCustomTerminalActive)
        {
            IsWaitingForInput = false;
            return;
        }

        // Check if we've been idle long enough
        var lastOutputTime = Pair.CustomTerminal.LastOutputTime;
        if (lastOutputTime.HasValue)
        {
            var idleTimeMs = (DateTime.Now - lastOutputTime.Value).TotalMilliseconds;
            if (idleTimeMs < inputPromptDetectionService.MinIdleTimeMs)
            {
                // Not idle long enough yet - keep current state
                return;
            }
        }

        // Get recent output from the custom terminal (AI assistant)
        var recentOutput = Pair.CustomTerminal.GetRecentOutput(2000);
        IsWaitingForInput = inputPromptDetectionService.IsWaitingForInput(recentOutput);
    }

    public void UpdateSplitRatioFromColumnWidths(double customWidth, double shellWidth)
    {
        var total = customWidth + shellWidth;
        if (total > 0)
        {
            // Setting the property will trigger OnSplitRatioChanged which updates computed properties
            SplitRatio = customWidth / total;
        }
    }

    /// <summary>
    /// Updates the detected links collection from terminal output.
    /// Uses MRU caching to preserve links that scroll out of the terminal buffer.
    /// </summary>
    /// <param name="linkDetectionService">The link detection service to use.</param>
    public void UpdateDetectedLinks(ILinkDetectionService linkDetectionService)
    {
        // Get recent output from all terminals (including run terminal if present)
        var customOutput = Pair.CustomTerminal.GetRecentOutput(10000);
        var shellOutput = Pair.ShellTerminal.GetRecentOutput(10000);
        var runOutput = Pair.RunTerminal?.GetRecentOutput(10000) ?? string.Empty;
        var combinedOutput = customOutput + "\n" + shellOutput + "\n" + runOutput;

        // Detect links from current buffer
        var newLinks = linkDetectionService.DetectAllLinks(combinedOutput, Pair.WorkingDirectory, MaxCachedLinks);
        var now = DateTime.Now;

        // Update cache with newly detected links (add or update timestamp)
        foreach (var link in newLinks)
        {
            _linkCache[link.Url] = (link, now);
        }

        // Trim cache if it exceeds max size (remove oldest entries)
        if (_linkCache.Count > MaxCachedLinks)
        {
            var toRemove = _linkCache
                .OrderBy(kvp => kvp.Value.LastSeen)
                .Take(_linkCache.Count - MaxCachedLinks)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _linkCache.Remove(key);
            }
        }

        // Get links sorted by most recently seen (MRU order)
        var cachedLinks = _linkCache
            .OrderByDescending(kvp => kvp.Value.LastSeen)
            .Select(kvp => kvp.Value.Link)
            .Take(20)
            .ToList();

        // Check if the displayed links have changed
        var currentUrls = DetectedLinks.Select(l => l.Url).ToList();
        var cachedUrls = cachedLinks.Select(l => l.Url).ToList();

        if (currentUrls.SequenceEqual(cachedUrls))
        {
            // No change, preserve selection state
            return;
        }

        // Links changed, update collection
        DetectedLinks.Clear();
        foreach (var link in cachedLinks)
        {
            DetectedLinks.Add(link);
        }

        OnPropertyChanged(nameof(HasDetectedLinks));
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Run terminal commands

    [RelayCommand]
    private void ToggleRunTerminal()
    {
        IsRunTerminalVisible = !IsRunTerminalVisible;
    }

    [RelayCommand]
    private void ShowRunTerminal()
    {
        IsRunTerminalVisible = true;
    }

    [RelayCommand]
    private void HideRunTerminal()
    {
        IsRunTerminalVisible = false;
    }

    // Explorer commands

    [RelayCommand]
    private void ToggleExplorer()
    {
        if (ExplorerPanelViewModel == null || _router is null) return;
        ShowRightDockPanel(ExplorerPanelViewModel);
    }

    // Clipboard commands

    /// <summary>
    /// Copies the selected text from the focused terminal to the clipboard.
    /// </summary>
    [RelayCommand]
    private void CopySelection()
    {
        GetFocusedSession()?.CopySelectionToClipboard();
    }

    /// <summary>
    /// Gets the currently focused terminal session.
    /// Uses Win32 cursor position to determine which terminal has focus.
    /// </summary>
    public Domain.TerminalSession? GetFocusedSession()
    {
        // Check which terminal the cursor is over (works for HWND-hosted controls)
        if (Pair.RunTerminal != null && IsRunTerminalVisible && Pair.RunTerminal.HasWin32Focus())
            return Pair.RunTerminal;
        if (Pair.ShellTerminal.HasWin32Focus())
            return Pair.ShellTerminal;
        if (Pair.CustomTerminal.HasWin32Focus())
            return Pair.CustomTerminal;

        // Fallback to tracked property
        return FocusedTerminal == ActiveTerminal.Custom
            ? Pair.CustomTerminal
            : Pair.ShellTerminal;
    }

    /// <summary>
    /// Focuses the active terminal control (Custom or Shell based on FocusedTerminal state).
    /// Call this after tab selection to ensure keyboard input goes to the terminal.
    /// </summary>
    public void FocusActiveTerminal()
    {
        var session = FocusedTerminal == ActiveTerminal.Custom
            ? Pair.CustomTerminal
            : Pair.ShellTerminal;
        session.Focus();
    }

    /// <summary>
    /// Sends a cd command to the shell terminal for the specified path.
    /// </summary>
    public void SendCdToShell(string path)
    {
        var escaped = path.Replace("'", "''");
        Pair.ShellTerminal.SendText($"cd '{escaped}'", appendNewline: true);
    }

    /// <summary>
    /// Updates the explorer split ratio from actual column widths.
    /// </summary>
    public void UpdateExplorerSplitRatioFromColumnWidths(double mainWidth, double explorerWidth)
    {
        var total = mainWidth + explorerWidth;
        if (total > 0)
        {
            ExplorerSplitRatio = explorerWidth / total;
        }
    }

    /// <summary>
    /// Event raised when the run terminal needs to be created and started.
    /// The MainWindow handles creating the actual terminal control.
    /// </summary>
    public event EventHandler<RunConfiguration>? RunStartRequested;

    /// <summary>
    /// Event raised when the run terminal needs to be stopped.
    /// </summary>
    public event EventHandler? RunStopRequested;

    [RelayCommand]
    private void StartRun()
    {
        if (ActiveRunConfiguration == null || RunState != RunState.Stopped)
            return;

        RunState = RunState.Starting;
        IsRunTerminalVisible = true;
        DetectedRunUrl = null;

        // Request the run to be started (MainWindow will handle terminal creation)
        RunStartRequested?.Invoke(this, ActiveRunConfiguration);
    }

    [RelayCommand]
    private void StopRun()
    {
        if (RunState == RunState.Stopped)
            return;

        RunState = RunState.Stopping;
        RunStopRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleRun()
    {
        if (CanRun)
            StartRun();
        else if (CanStop)
            StopRun();
    }

    [RelayCommand]
    private void RestartRun()
    {
        if (RunState == RunState.Running || RunState == RunState.Starting)
        {
            StopRun();
            // The actual restart will need to be handled by MainWindow
            // after the stop completes
        }
        else if (CanRun)
        {
            StartRun();
        }
    }

    /// <summary>
    /// Called when the run process has started successfully.
    /// </summary>
    public void OnRunStarted()
    {
        RunState = RunState.Running;
    }

    /// <summary>
    /// Called when the run process has stopped.
    /// </summary>
    public void OnRunStopped()
    {
        RunState = RunState.Stopped;
        DetectedRunUrl = null;
    }

    /// <summary>
    /// Sets the run terminal control after it's created.
    /// </summary>
    public void SetRunTerminalControl(EasyTerminalControl runControl)
    {
        RunTerminalContent = runControl;

        if (Pair.RunTerminal != null)
        {
            Pair.RunTerminal.SetTerminalControl(runControl);

            // Subscribe to activity changes
            Pair.RunTerminal.ActivityChanged += (s, e) =>
            {
                IsRunTerminalActive = Pair.RunTerminal.IsActive;
            };

            // Subscribe to title changes
            Pair.RunTerminal.TitleChanged += (s, title) =>
            {
                RunTerminalTitle = title;
            };
        }

        // Reset title for new terminal
        RunTerminalTitle = string.Empty;
    }

    /// <summary>
    /// Initializes run configurations from project detection.
    /// </summary>
    public void InitializeRunConfigurations(List<RunConfiguration> configs, string? activeConfigId)
    {
        RunConfigurations.Clear();
        foreach (var config in configs)
        {
            RunConfigurations.Add(config);
        }

        // Set active configuration
        if (!string.IsNullOrEmpty(activeConfigId))
        {
            ActiveRunConfiguration = RunConfigurations.FirstOrDefault(c => c.Id == activeConfigId);
        }

        ActiveRunConfiguration ??= RunConfigurations.FirstOrDefault(c => c.IsDefault)
                                  ?? RunConfigurations.FirstOrDefault();

        OnPropertyChanged(nameof(HasMultipleRunConfigs));
        OnPropertyChanged(nameof(HasAnyRunConfiguration));
    }

    /// <summary>
    /// Updates the run split ratio from actual column widths.
    /// </summary>
    public void UpdateRunSplitRatioFromColumnWidths(double mainWidth, double runWidth)
    {
        var total = mainWidth + runWidth;
        if (total > 0)
        {
            RunSplitRatio = runWidth / total;
        }
    }

    /// <summary>
    /// Updates activity state for the run terminal.
    /// </summary>
    public void UpdateRunActivityState()
    {
        if (Pair.RunTerminal != null)
        {
            Pair.RunTerminal.CheckActivityState();
            IsRunTerminalActive = Pair.RunTerminal.IsActive;
        }
    }

    /// <summary>
    /// Initializes the panel system with the file explorer as the first panel.
    /// The explorer is registered with the panel cache but not auto-mounted; the router's
    /// <see cref="IPanelRouter.Restore"/> drives mounts based on persisted state.
    /// </summary>
    public void InitializePanelSystem(FileExplorerViewModel explorerViewModel)
    {
        ExplorerViewModel = explorerViewModel;
        ExplorerPanelViewModel = new FileExplorerPanelViewModel(explorerViewModel);
        SetPanel(ExplorerPanelViewModel);
    }

    #region Generic Panel Methods

    /// <summary>
    /// Registers a panel with this tab.
    /// Called from MainWindow when panels are initialized.
    /// </summary>
    public void SetPanel(IPanelableViewModel panel)
    {
        _registeredPanels[panel.PanelId] = panel;
    }

    #endregion

    #region Center Panel Methods

    /// <summary>
    /// Shows a panel in the center area via the router. Terminals continue running in the
    /// background. Symmetric with <see cref="ShowRightDockPanel"/>.
    /// </summary>
    public void ShowCenterPanel(IPanelableViewModel panel)
    {
        if (_router is null) return;
        // The center is a single slot: mounting a new panel evicts the visible one but leaves its
        // registration behind, which makes a later Show of that panel resolve to a no-op Focus
        // (it appears "stuck closed"). Close the current occupant's registration first so the slot
        // is genuinely free. CloseZone is scope-correct (closes only THIS tab's center panel).
        //
        // Note: this re-introduces, for cross-panel swaps only, the HasMounted=false→true transition
        // that WpfCenterSurface.Mount deliberately avoids for same-panel in-place updates (see the
        // flicker note there). It is accepted here because both the CloseZone and the Show run
        // synchronously on the UI thread within this method, so WPF coalesces the layout pass and the
        // transient ActiveCenterPanel==null / IsTerminalsVisible==true state never renders.
        var current = _centerSurface?.MountedPanel;
        if (current is not null && !ReferenceEquals(current, panel))
            _router.CloseZone(PanelZone.Center, CenterScope);
        _router.Show(panel, new PanelShowOptions(Zone: PanelZone.Center, Scope: CenterScope, ForceShow: true));
    }

    /// <summary>Returns to terminals by closing the active center panel via the router.</summary>
    [RelayCommand]
    public void CloseCenterPanel()
    {
        if (_router is null || _centerSurface is null) return;
        _router.CloseZone(PanelZone.Center, _centerSurface.Scope);
        FocusActiveTerminal();
    }

    /// <summary>
    /// Toggles a center panel: if it's the active center panel, close it (return to terminals);
    /// if not, show it in the center area.
    /// </summary>
    public void ToggleCenterPanel(IPanelableViewModel panel)
    {
        if (ActiveCenterPanel == panel)
            CloseCenterPanel();
        else
            ShowCenterPanel(panel);
    }

    #endregion

    /// <summary>
    /// Refreshes the Claude task indicator state by checking for active Claude tasks in this workspace.
    /// </summary>
    private void RefreshClaudeTaskIndicator()
    {
        if (_taskService == null)
        {
            HasActiveClaudeTasks = false;
            return;
        }

        // Normalize workspace path for comparison
        var normalizedWorkspace = NormalizePath(Pair.WorkingDirectory);

        // Get all tasks and check if any are active Claude tasks for this workspace
        var allTasks = _taskService.GetAllTasks();
        var hasActiveTasks = allTasks.Any(t =>
            t.Status == Core.Domain.FocusTaskStatus.InProgress &&
            t.IsClaudeTask &&
            t.ProjectPaths.Any(p => NormalizePath(p) == normalizedWorkspace));

        HasActiveClaudeTasks = hasActiveTasks;
    }

    /// <summary>
    /// Normalizes a file path for consistent comparison.
    /// Removes trailing separators and converts to lowercase.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    /// <summary>
    /// Called when tasks change in the task service.
    /// </summary>
    private void OnTasksChanged(object? sender, EventArgs e)
    {
        RefreshClaudeTaskIndicator();
    }

    /// <summary>
    /// Cleanup method to unsubscribe from events.
    /// Call this when the tab is being closed.
    /// </summary>
    /// <summary>
    /// Restores layout, terminal, and run-related state from a persisted <see cref="DirectorySettings"/>.
    /// Must be called before <see cref="InitializePanelSystem"/>; for explorer/panel state use
    /// <see cref="LoadPanelStateFromDirectorySettings"/> afterwards.
    /// </summary>
    public void LoadLayoutFromDirectorySettings(DirectorySettings settings)
    {
        LayoutMode = settings.LayoutMode;
        SplitRatio = settings.SplitRatio;
        if (Enum.TryParse<ActiveTerminal>(settings.ActiveTerminal, out var active))
        {
            ActiveTerminal = active;
            Pair.ActiveTerminal = active;
        }
        IsRunTerminalVisible = settings.IsRunTerminalVisible;
        RunSplitRatio = settings.RunSplitRatio;
    }

    /// <summary>
    /// Restores explorer split ratio from a persisted <see cref="DirectorySettings"/>.
    /// Visibility (<see cref="IsExplorerVisible"/>) is derived from the right-dock surface;
    /// mounts replay through <see cref="RestoreRightDockPanels"/>.
    /// </summary>
    public void LoadPanelStateFromDirectorySettings(DirectorySettings settings)
    {
        ExplorerSplitRatio = settings.ExplorerSplitRatio;
    }

    /// <summary>
    /// Writes the tab's persistence-relevant state into <paramref name="target"/>.
    /// Inverse of <see cref="LoadLayoutFromDirectorySettings"/> +
    /// <see cref="LoadPanelStateFromDirectorySettings"/>, plus center/right-panel state
    /// (which is restored elsewhere via an event so the host can hydrate the panel VMs).
    /// </summary>
    public void WriteToDirectorySettings(DirectorySettings target)
    {
        target.LayoutMode = LayoutMode;
        target.SplitRatio = SplitRatio;
        target.ActiveTerminal = ActiveTerminal.ToString();

        target.IsRunTerminalVisible = IsRunTerminalVisible;
        target.RunSplitRatio = RunSplitRatio;
        target.ActiveRunConfigurationId = ActiveRunConfiguration?.Id;
        target.RunConfigurations = [.. RunConfigurations];

        target.ExplorerSplitRatio = ExplorerSplitRatio;

        // Tab-scope panel state (OpenRightPanels / ActiveRightPanel / ActiveCenterPanel) is now
        // owned by DirectorySettingsPanelPersistence; the router calls Save on every Routed event.
        // GitPanelActiveTab round-trips via UnifiedGitPanelViewModel itself (config-backed).
    }

    public void Cleanup()
    {
        if (_taskService != null)
        {
            _taskService.TasksChanged -= OnTasksChanged;
        }

        if (_rightDock is not null)
        {
            _rightDock.PropertyChanged -= OnRightDockPropertyChanged;
            _router?.UnregisterSurface(PanelZone.RightDock, _rightDock.Scope);
            _rightDock.Dispose();
            _rightDock = null;
        }

        if (_centerSurface is not null)
        {
            _centerSurface.PropertyChanged -= OnCenterSurfacePropertyChanged;
            _router?.UnregisterSurface(PanelZone.Center, _centerSurface.Scope);
            _centerSurface.Dispose();
            _centerSurface = null;
        }
    }
}
