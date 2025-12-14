using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWindowsTerminalControl;
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

    [ObservableProperty]
    private string _customIcon = "🤖";

    [ObservableProperty]
    private string _shellIcon = "💻";

    [ObservableProperty]
    private ActiveTerminal _activeTerminal = ActiveTerminal.Custom;

    [ObservableProperty]
    private ContentControl? _customTerminalContent;

    [ObservableProperty]
    private ContentControl? _shellTerminalContent;

    [ObservableProperty]
    private bool _isSplitView = true;  // Default to split view

    [ObservableProperty]
    private double _splitRatio = 0.6;  // Custom terminal takes 60% by default

    [ObservableProperty]
    private GitStatus? _gitStatus;

    [ObservableProperty]
    private bool _isCustomTerminalActive;

    [ObservableProperty]
    private bool _isShellTerminalActive;

    // Run terminal properties
    [ObservableProperty]
    private ContentControl? _runTerminalContent;

    [ObservableProperty]
    private bool _isRunTerminalVisible;

    [ObservableProperty]
    private double _runSplitRatio = 0.3;

    [ObservableProperty]
    private RunState _runState = RunState.Stopped;

    [ObservableProperty]
    private string? _detectedRunUrl;

    [ObservableProperty]
    private bool _isRunTerminalActive;

    [ObservableProperty]
    private RunConfiguration? _activeRunConfiguration;

    /// <summary>
    /// Collection of available run configurations for this project.
    /// </summary>
    public ObservableCollection<RunConfiguration> RunConfigurations { get; } = new();

    /// <summary>
    /// True if either terminal is currently producing output.
    /// </summary>
    public bool IsAnyTerminalActive => IsCustomTerminalActive || IsShellTerminalActive || IsRunTerminalActive;

    /// <summary>
    /// Collection of detected links from terminal output.
    /// </summary>
    public ObservableCollection<DetectedLink> DetectedLinks { get; } = new();

    /// <summary>
    /// True if there are any detected links to display.
    /// </summary>
    public bool HasDetectedLinks => DetectedLinks.Count > 0;

    // Computed column widths from split ratio
    public GridLength CustomColumnWidth => new GridLength(SplitRatio, GridUnitType.Star);
    public GridLength ShellColumnWidth => new GridLength(1 - SplitRatio, GridUnitType.Star);

    // Run terminal column width (only shown when visible)
    // Use Pixel unit with 0 when hidden so it doesn't participate in star distribution
    public GridLength RunColumnWidth => IsRunTerminalVisible
        ? new GridLength(RunSplitRatio, GridUnitType.Star)
        : new GridLength(0, GridUnitType.Pixel);

    public GridLength RunSplitterWidth => IsRunTerminalVisible
        ? new GridLength(4, GridUnitType.Pixel)
        : new GridLength(0, GridUnitType.Pixel);

    // Run state computed properties
    public bool CanRun => RunState == RunState.Stopped && ActiveRunConfiguration != null;
    public bool CanStop => RunState == RunState.Running || RunState == RunState.Starting;
    public bool HasDetectedRunUrl => !string.IsNullOrEmpty(DetectedRunUrl);
    public bool HasMultipleRunConfigs => RunConfigurations.Count > 1;

    // Git display properties
    public string TitleWithGit => GitStatus?.IsGitRepository == true
        ? $"{Title} {GitStatus.BranchDisplayShort}"
        : Title;

    public string DisplayTitle => TitleWithGit;

    public string GitStatusDisplay => GitStatus?.StatusDisplayFull ?? "";

    public TerminalPair Pair { get; }
    private readonly StatisticsService _statisticsService;

    public string CurrentIcon => ActiveTerminal == ActiveTerminal.Custom ? CustomIcon : ShellIcon;

    public ContentControl? CurrentTerminalContent => ActiveTerminal == ActiveTerminal.Custom
        ? CustomTerminalContent
        : ShellTerminalContent;

    public event EventHandler? CloseRequested;
    public event EventHandler? SettingsChanged;

    public TerminalPairTabViewModel(TerminalPair pair, string customIcon, string shellIcon, StatisticsService statisticsService)
    {
        Pair = pair;
        Title = pair.DirectoryName;
        CustomIcon = customIcon;
        ShellIcon = shellIcon;
        _statisticsService = statisticsService;
        ActiveTerminal = pair.ActiveTerminal;
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

        // Notify that CurrentTerminalContent has changed
        OnPropertyChanged(nameof(CurrentTerminalContent));
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

    partial void OnSplitRatioChanged(double value)
    {
        OnPropertyChanged(nameof(CustomColumnWidth));
        OnPropertyChanged(nameof(ShellColumnWidth));
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
    }

    partial void OnIsCustomTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
    }

    partial void OnIsShellTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
    }

    partial void OnIsRunTerminalActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyTerminalActive));
    }

    partial void OnIsRunTerminalVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(RunColumnWidth));
        OnPropertyChanged(nameof(RunSplitterWidth));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnRunSplitRatioChanged(double value)
    {
        OnPropertyChanged(nameof(RunColumnWidth));
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
    /// Only updates if the actual links have changed to preserve UI selection state.
    /// </summary>
    /// <param name="linkDetectionService">The link detection service to use.</param>
    public void UpdateDetectedLinks(LinkDetectionService linkDetectionService)
    {
        // Get recent output from both terminals
        var customOutput = Pair.CustomTerminal.GetRecentOutput(10000);
        var shellOutput = Pair.ShellTerminal.GetRecentOutput(10000);
        var combinedOutput = customOutput + "\n" + shellOutput;

        // Detect links
        var newLinks = linkDetectionService.DetectAllLinks(combinedOutput, Pair.WorkingDirectory, 20);

        // Check if links have changed by comparing URLs
        var currentUrls = DetectedLinks.Select(l => l.Url).ToList();
        var newUrls = newLinks.Select(l => l.Url).ToList();

        if (currentUrls.SequenceEqual(newUrls))
        {
            // No change, preserve selection state
            return;
        }

        // Links changed, update collection
        DetectedLinks.Clear();
        foreach (var link in newLinks)
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
        }
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
