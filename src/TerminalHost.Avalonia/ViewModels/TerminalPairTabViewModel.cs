using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class TerminalPairTabViewModel : ObservableObject, ITabViewModel
{
    [ObservableProperty]
    private string _title = "Terminal";

    public string TabIcon => "📁";
    public string WorkingDirectory => Pair.WorkingDirectory;
    public bool IsCloseable => true;
    public bool CanDuplicate => true; // Project tabs can be duplicated

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
    private Control? _customTerminalContent;

    [ObservableProperty]
    private Control? _shellTerminalContent;

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
    private Control? _runTerminalContent;

    [ObservableProperty]
    private bool _isRunTerminalVisible;

    [ObservableProperty]
    private double _runSplitRatio = 0.3;

    // Explorer panel properties
    [ObservableProperty]
    private bool _isExplorerVisible;

    [ObservableProperty]
    private double _explorerSplitRatio = 0.25;

    [ObservableProperty]
    private FileExplorerViewModel? _explorerViewModel;

    [ObservableProperty]
    private RunState _runState = RunState.Stopped;

    [ObservableProperty]
    private string? _detectedRunUrl;

    [ObservableProperty]
    private bool _isRunTerminalActive;

    [ObservableProperty]
    private bool _hasUnreadActivity;

    // Track previous activity state to detect transitions
    private bool _wasAnyTerminalActive;

    // Terminal factory for lazy initialization
    private readonly ITerminalControlFactory _terminalFactory;

    /// <summary>
    /// Whether the terminal controls have been created (lazy initialization).
    /// </summary>
    public bool IsTerminalInitialized { get; private set; }

    // Backing field for IsSelected
    private bool _isSelected;

    /// <summary>
    /// Whether this tab is currently selected. Set by MainViewModel.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(ShowActivitySpinner));
                OnPropertyChanged(nameof(ShowCompletedIndicator));

                // When tab becomes selected, suppress activity briefly to avoid
                // false positives from terminal redraw output
                if (value)
                {
                    Pair.CustomTerminal.SuppressActivityBriefly();
                    Pair.ShellTerminal.SuppressActivityBriefly();
                    Pair.RunTerminal?.SuppressActivityBriefly();
                }
            }
        }
    }

    [ObservableProperty]
    private bool _isVisibleInFocusMode = true;

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

    // Shell Profile support
    [ObservableProperty]
    private ObservableCollection<Profile> _availableShellProfiles = [];

    [ObservableProperty]
    private Profile? _selectedShellProfile;

    private bool _suppressShellSwitchEvent;

    /// <summary>
    /// Whether multiple shell profiles are available (controls visibility of selector).
    /// </summary>
    public bool HasMultipleShellProfiles => AvailableShellProfiles.Count > 1;

    /// <summary>
    /// Event raised when the user selects a different shell profile.
    /// </summary>
    public event EventHandler<Profile>? ShellProfileSwitchRequested;

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
    /// True if either terminal is currently producing output.
    /// </summary>
    public bool IsAnyTerminalActive => IsCustomTerminalActive || IsShellTerminalActive || IsRunTerminalActive;

    /// <summary>
    /// Whether to show the activity spinner on the tab.
    /// True when terminal is active AND tab is NOT selected.
    /// </summary>
    public bool ShowActivitySpinner => IsAnyTerminalActive && !IsSelected;

    /// <summary>
    /// Whether to show the completed indicator (green dot) on the tab.
    /// True when activity finished AND has unread activity AND tab is NOT selected.
    /// </summary>
    public bool ShowCompletedIndicator => HasUnreadActivity && !IsAnyTerminalActive && !IsSelected;

    /// <summary>
    /// Whether the terminal is waiting for user input.
    /// Not yet implemented in Avalonia version.
    /// </summary>
    public bool IsWaitingForInput => false;

    /// <summary>
    /// Whether to show the waiting indicator on the tab.
    /// </summary>
    public bool ShowWaitingIndicator => false;

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

    // Main content column width (terminals + run) - takes remaining space after explorer
    public GridLength MainContentColumnWidth
    {
        get
        {
            double explorerPortion = IsExplorerVisible ? ExplorerSplitRatio : 0;
            double mainPortion = 1.0 - explorerPortion;
            return new GridLength(Math.Max(0.1, mainPortion), GridUnitType.Star);
        }
    }

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

    // Explorer column widths - use Pixel unit with 0 when hidden
    public GridLength ExplorerColumnWidth => IsExplorerVisible
        ? new GridLength(ExplorerSplitRatio, GridUnitType.Star)
        : new GridLength(0, GridUnitType.Pixel);

    public GridLength ExplorerSplitterWidth => IsExplorerVisible
        ? new GridLength(4, GridUnitType.Pixel)
        : new GridLength(0, GridUnitType.Pixel);

    // Git display properties
    public string TitleWithGit => GitStatus?.IsGitRepository == true
        ? $"{Title} {GitStatus.BranchDisplayShort}"
        : Title;

    public string DisplayTitle => TitleWithGit;

    public string GitStatusDisplay => GitStatus?.StatusDisplayFull ?? "";

    public TerminalPair Pair { get; }
    private readonly IStatisticsService _statisticsService;

    public string CurrentIcon => ActiveTerminal == ActiveTerminal.Custom ? CustomIcon : ShellIcon;

    public Control? CurrentTerminalContent => ActiveTerminal == ActiveTerminal.Custom
        ? CustomTerminalContent
        : ShellTerminalContent;

    public event EventHandler? CloseRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler<AiAssistantSwitchEventArgs>? AiAssistantSwitchRequested;

    public TerminalPairTabViewModel(TerminalPair pair, string customIcon, string shellIcon, IStatisticsService statisticsService, ITerminalControlFactory terminalFactory)
    {
        Pair = pair;
        Title = pair.DirectoryName;
        CustomIcon = customIcon;
        ShellIcon = shellIcon;
        _statisticsService = statisticsService;
        _terminalFactory = terminalFactory;
        ActiveTerminal = pair.ActiveTerminal;
    }

    public TerminalPairTabViewModel(TerminalPair pair, AiAssistant activeAiAssistant, IReadOnlyList<AiAssistant> enabledAssistants, string shellIcon, IStatisticsService statisticsService, ITerminalControlFactory terminalFactory)
    {
        Pair = pair;
        Title = pair.DirectoryName;
        ActiveAiAssistant = activeAiAssistant;
        SelectedAiAssistant = activeAiAssistant;
        CustomIcon = activeAiAssistant.DisplayLabel;
        ShellIcon = shellIcon;
        _statisticsService = statisticsService;
        _terminalFactory = terminalFactory;
        ActiveTerminal = pair.ActiveTerminal;

        // Populate available assistants
        foreach (var assistant in enabledAssistants)
        {
            AvailableAiAssistants.Add(assistant);
        }
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

    partial void OnSelectedShellProfileChanged(Profile? oldValue, Profile? newValue)
    {
        // Only fire event if the user actually changed the selection (not suppressed)
        if (_suppressShellSwitchEvent)
            return;

        if (newValue != null && oldValue != null && newValue.Id != oldValue.Id)
        {
            ShellProfileSwitchRequested?.Invoke(this, newValue);
        }
    }

    /// <summary>
    /// Initializes the terminal controls. Called when the tab is first selected (lazy initialization).
    /// </summary>
    public async Task InitializeTerminalsAsync()
    {
        if (IsTerminalInitialized)
            return;

        // Create terminal controls
        var customControl = await _terminalFactory.CreateTerminalControlAsync(Pair.CustomTerminal);
        var shellControl = await _terminalFactory.CreateTerminalControlAsync(Pair.ShellTerminal);

        // Set up the controls
        SetTerminalControls(customControl, shellControl);

        // Initialize run terminal if it should be visible
        if (IsRunTerminalVisible)
        {
            await InitializeRunTerminalAsync();
        }

        IsTerminalInitialized = true;
    }

    /// <summary>
    /// Initializes the run terminal with a shell. Called when IsRunTerminalVisible is true at startup,
    /// or when the run terminal is first shown.
    /// </summary>
    public async Task InitializeRunTerminalAsync()
    {
        if (Pair.RunTerminal != null)
            return; // Already initialized

        // Create a shell profile for the run terminal (using the same shell as the Shell terminal)
        var shellProfile = new Profile
        {
            Id = "run-shell",
            Name = "Run Shell",
            Command = Pair.ShellTerminal.Profile.Command,
            WorkingDir = Pair.WorkingDirectory,
            Icon = "▶"
        };

        // Create the run terminal session
        var runSession = Pair.CreateRunTerminal(shellProfile);

        // Create the terminal control
        var runControl = await _terminalFactory.CreateTerminalControlAsync(runSession);

        // Set up the control
        SetRunTerminalControl(runControl);
    }

    public void SetTerminalControls(ITerminalControl customControl, ITerminalControl shellControl)
    {
        CustomTerminalContent = customControl.NativeControl as Control;
        ShellTerminalContent = shellControl.NativeControl as Control;

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
    public void SetCustomTerminalControl(ITerminalControl newControl)
    {
        CustomTerminalContent = newControl.NativeControl as Control;
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
        };

        // Reset title for new terminal
        CustomTerminalTitle = string.Empty;

        OnPropertyChanged(nameof(CurrentTerminalContent));
    }

    /// <summary>
    /// Sets the shell terminal control when switching shell profile.
    /// </summary>
    public void SetShellTerminalControl(ITerminalControl newControl)
    {
        ShellTerminalContent = newControl.NativeControl as Control;
        Pair.ShellTerminal.SetTerminalControl(newControl);

        // Subscribe to activity changes
        Pair.ShellTerminal.ActivityChanged += (s, e) =>
        {
            IsShellTerminalActive = Pair.ShellTerminal.IsActive;
        };

        // Subscribe to title changes
        Pair.ShellTerminal.TitleChanged += (s, title) =>
        {
            ShellTerminalTitle = title;
        };

        // Reset title for new terminal
        ShellTerminalTitle = string.Empty;

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

    /// <summary>
    /// Refreshes the available shell profiles list.
    /// </summary>
    public void RefreshAvailableShellProfiles(IReadOnlyList<Profile> profiles)
    {
        _suppressShellSwitchEvent = true;

        AvailableShellProfiles.Clear();
        foreach (var profile in profiles)
        {
            AvailableShellProfiles.Add(profile);
        }

        // Update selected to match current active (find by ID)
        if (SelectedShellProfile != null)
        {
            var matchingProfile = profiles.FirstOrDefault(p => p.Id == SelectedShellProfile.Id);
            if (matchingProfile != null)
            {
                SelectedShellProfile = matchingProfile;
                ShellIcon = matchingProfile.Icon ?? "💻";
            }
            else if (profiles.Count > 0)
            {
                // Current profile was removed, switch to first available
                var firstProfile = profiles[0];
                SelectedShellProfile = firstProfile;
                ShellIcon = firstProfile.Icon ?? "💻";
            }
        }
        else if (profiles.Count > 0)
        {
            // No profile selected yet, select the first one
            SelectedShellProfile = profiles[0];
            ShellIcon = profiles[0].Icon ?? "💻";
        }

        _suppressShellSwitchEvent = false;
        OnPropertyChanged(nameof(HasMultipleShellProfiles));
    }

    /// <summary>
    /// Updates the shell profile after switching.
    /// </summary>
    public void UpdateActiveShellProfile(Profile newProfile)
    {
        ShellIcon = newProfile.Icon ?? "💻";

        // Update selected without triggering the change event
        _suppressShellSwitchEvent = true;
        SelectedShellProfile = newProfile;
        _suppressShellSwitchEvent = false;
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
    private void SetCustomFullLayout() => LayoutMode = TerminalLayoutMode.CustomFull;

    [RelayCommand]
    private void SetHorizontalSplitLayout() => LayoutMode = TerminalLayoutMode.HorizontalSplit;

    [RelayCommand]
    private void SetVerticalSplitLayout() => LayoutMode = TerminalLayoutMode.VerticalSplit;

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

        // Refresh file explorer's git status when tab git status changes
        if (ExplorerViewModel != null)
        {
            _ = ExplorerViewModel.RefreshGitStatusAsync();
        }
    }

    partial void OnIsCustomTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
        OnPropertyChanged(nameof(ShowActivitySpinner));
        OnPropertyChanged(nameof(ShowCompletedIndicator));
        CheckActivityTransition();
    }

    partial void OnIsShellTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
        OnPropertyChanged(nameof(ShowActivitySpinner));
        OnPropertyChanged(nameof(ShowCompletedIndicator));
        CheckActivityTransition();
    }

    partial void OnIsRunTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
        OnPropertyChanged(nameof(ShowActivitySpinner));
        OnPropertyChanged(nameof(ShowCompletedIndicator));
        CheckActivityTransition();
    }

    partial void OnHasUnreadActivityChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCompletedIndicator));
    }

    /// <summary>
    /// Checks for activity state transitions and updates HasUnreadActivity accordingly.
    /// When activity stops (active → idle) on a non-selected tab, marks it as having unread activity.
    /// </summary>
    private void CheckActivityTransition()
    {
        var isCurrentlyActive = IsAnyTerminalActive;

        // Transition from active to idle: mark as unread, but only if tab is NOT selected
        // This prevents false positives from terminal focus/blur rendering events
        if (_wasAnyTerminalActive && !isCurrentlyActive && !IsSelected)
        {
            HasUnreadActivity = true;
        }

        _wasAnyTerminalActive = isCurrentlyActive;
    }

    /// <summary>
    /// Clears the unread activity state. Called when the tab becomes focused/selected.
    /// Also resets the transition tracking to prevent false positives from terminal
    /// rendering/focus events that briefly trigger activity.
    /// </summary>
    public void ClearUnreadActivity()
    {
        HasUnreadActivity = false;
        // Sync tracking state to current state to avoid false transition detection
        _wasAnyTerminalActive = IsAnyTerminalActive;
        // Update activity indicators since IsSelected state changed
        OnPropertyChanged(nameof(ShowActivitySpinner));
        OnPropertyChanged(nameof(ShowCompletedIndicator));
    }

    partial void OnIsRunTerminalVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(RunColumnWidth));
        OnPropertyChanged(nameof(RunSplitterWidth));
        OnPropertyChanged(nameof(MainTerminalsColumnWidth));
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        // Initialize run terminal if becoming visible and not yet initialized
        if (value && IsTerminalInitialized && Pair.RunTerminal == null)
        {
            // Use Dispatcher to ensure proper async handling
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await InitializeRunTerminalAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize run terminal: {ex}");
                }
            });
        }
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

    partial void OnIsExplorerVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ExplorerColumnWidth));
        OnPropertyChanged(nameof(ExplorerSplitterWidth));
        OnPropertyChanged(nameof(MainContentColumnWidth));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnExplorerSplitRatioChanged(double value)
    {
        OnPropertyChanged(nameof(ExplorerColumnWidth));
        OnPropertyChanged(nameof(MainContentColumnWidth));
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
        Pair.RunTerminal?.CheckActivityState();

        // Update properties
        IsCustomTerminalActive = Pair.CustomTerminal.IsActive;
        IsShellTerminalActive = Pair.ShellTerminal.IsActive;
        if (Pair.RunTerminal != null)
        {
            IsRunTerminalActive = Pair.RunTerminal.IsActive;
        }
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
        IsExplorerVisible = !IsExplorerVisible;
    }

    [RelayCommand]
    private void ShowExplorer()
    {
        IsExplorerVisible = true;
    }

    [RelayCommand]
    private void HideExplorer()
    {
        IsExplorerVisible = false;
    }

    // Clipboard commands

    /// <summary>
    /// Copies the selected text from the focused terminal to the clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopySelectionAsync()
    {
        var session = GetFocusedSession();
        if (session != null)
        {
            await session.CopySelectionToClipboardAsync();
        }
    }

    /// <summary>
    /// Gets the currently focused terminal session.
    /// Uses the terminal control's focus state to determine which terminal has focus.
    /// </summary>
    public Domain.TerminalSession? GetFocusedSession()
    {
        // Check which terminal has focus
        if (Pair.RunTerminal != null && IsRunTerminalVisible && Pair.RunTerminal.HasFocus())
            return Pair.RunTerminal;
        if (Pair.ShellTerminal.HasFocus())
            return Pair.ShellTerminal;
        if (Pair.CustomTerminal.HasFocus())
            return Pair.CustomTerminal;

        // Fallback to tracked property
        return FocusedTerminal == ActiveTerminal.Custom
            ? Pair.CustomTerminal
            : Pair.ShellTerminal;
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
    public void SetRunTerminalControl(ITerminalControl runControl)
    {
        RunTerminalContent = runControl.NativeControl as Control;

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

        // Set active configuration - prioritize IsDefault flag over activeConfigId
        // because user explicitly marking a config as default should take precedence
        var defaultConfig = RunConfigurations.FirstOrDefault(c => c.IsDefault);
        if (defaultConfig != null)
        {
            ActiveRunConfiguration = defaultConfig;
        }
        else if (!string.IsNullOrEmpty(activeConfigId))
        {
            ActiveRunConfiguration = RunConfigurations.FirstOrDefault(c => c.Id == activeConfigId);
        }

        ActiveRunConfiguration ??= RunConfigurations.FirstOrDefault();

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
}
