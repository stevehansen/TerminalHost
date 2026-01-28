using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for the Timeline IDE tab - visual timeline of AI-assisted development.
/// </summary>
public partial class TimelineTabViewModel : ObservableObject, ITabViewModel
{
    private readonly ITimelineService _timelineService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationService _configService;
    private readonly IGitStatusService _gitStatusService;
    private readonly ITimerService _timerService;
    private IAppTimer? _refreshTimer;

    public string Title => "Timeline";
    public string TabIcon => "⏱️";
    public bool IsCloseable => true;
    public bool CanDuplicate => false;
    public string WorkingDirectory => string.Empty;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsVisibleInFocusMode => true;

    [ObservableProperty]
    private bool _isSelected;
    public bool ShowActivitySpinner => false;
    public bool ShowCompletedIndicator => false;
    public bool IsWaitingForInput => false;
    public bool ShowWaitingIndicator => false;
    public bool ShowClaudeTaskIndicator => false;
    public bool IsTerminalInitialized => true;
    public Task InitializeTerminalsAsync() => Task.CompletedTask;
    public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects) { }
    public void ClearUnreadActivity() { }
    public string DisplayTitle => Title;

    public event EventHandler? CloseRequested;

    #region Observable Properties

    [ObservableProperty]
    private ObservableCollection<IntentRowViewModel> _intents = [];

    [ObservableProperty]
    private IntentRowViewModel? _selectedIntent;

    [ObservableProperty]
    private SessionBlockViewModel? _selectedSession;

    [ObservableProperty]
    private TimeScale _currentTimeScale = TimeScale.Hours;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isFocusing;

    [ObservableProperty]
    private string _focusTimeDisplay = "0h 0m";

    [ObservableProperty]
    private string _currentTimeDisplay = "";

    [ObservableProperty]
    private string _currentDateDisplay = "";

    [ObservableProperty]
    private int _totalIntentsCount;

    [ObservableProperty]
    private int _activeForksCount;

    [ObservableProperty]
    private int _runningSessionsCount;

    [ObservableProperty]
    private int _totalCommitsCount;

    [ObservableProperty]
    private bool _isSessionDetailVisible;

    // Troubleshooting properties
    [ObservableProperty]
    private bool _hooksInstalled;

    [ObservableProperty]
    private int _unassignedSessionsCount;

    [ObservableProperty]
    private string _hookStatusMessage = "";

    [ObservableProperty]
    private bool _showTroubleshooting;

    [ObservableProperty]
    private ObservableCollection<OrphanSessionViewModel> _orphanSessions = [];

    // Timeline positioning properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimeXPosition))]
    private DateTime _viewStartTime;

    [ObservableProperty]
    private DateTime _viewEndTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimeXPosition))]
    private double _pixelsPerMinute = 3.0; // Default: 3 pixels per minute = 180px per hour

    [ObservableProperty]
    private double _timelineWidth = 1000;

    private double _containerWidth = 800; // Default fallback

    /// <summary>
    /// Set by the View when container size changes. Triggers recalculation.
    /// </summary>
    public void SetContainerWidth(double width)
    {
        if (width > 100 && Math.Abs(_containerWidth - width) > 10)
        {
            _containerWidth = width;
            UpdateTimelineView();
        }
    }

    /// <summary>
    /// Get time markers for the time ruler.
    /// </summary>
    public ObservableCollection<TimeMarker> TimeMarkers { get; } = [];

    #endregion

    public TimelineTabViewModel(
        ITimelineService timelineService,
        IDialogService dialogService,
        IConfigurationService configService,
        IGitStatusService gitStatusService,
        ITimerService timerService)
    {
        _timelineService = timelineService;
        _dialogService = dialogService;
        _configService = configService;
        _gitStatusService = gitStatusService;
        _timerService = timerService;

        // Subscribe to events
        _timelineService.EnabledChanged += OnEnabledChanged;
        _timelineService.IntentsChanged += OnIntentsChanged;
        _timelineService.SessionsChanged += OnSessionsChanged;
        _timelineService.FocusStateChanged += OnFocusStateChanged;
        _timelineService.TimeScaleChanged += OnTimeScaleChanged;
        _timelineService.OrphanSessionsChanged += OnOrphanSessionsChanged;

        // Initialize state
        LoadState();
        UpdateTimeDisplay();

        // Start refresh timer for time display (uses UI-thread-safe timer)
        _refreshTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(1), UpdateTimeDisplay);
        _refreshTimer.Start();
    }

    private void LoadState()
    {
        var state = _timelineService.GetState();
        IsEnabled = _timelineService.IsEnabled;
        IsFocusing = _timelineService.IsFocusing;
        CurrentTimeScale = _timelineService.CurrentScale;

        UpdateFocusTimeDisplay();
        RefreshIntents();
        RefreshOrphanSessions();
        UpdateStatusBar();

        // Initialize timeline view immediately (don't wait for timer)
        UpdateTimelineView();
    }

    private void RefreshOrphanSessions()
    {
        var orphans = _timelineService.GetOrphanSessions();
        OrphanSessions.Clear();

        foreach (var orphan in orphans)
        {
            OrphanSessions.Add(new OrphanSessionViewModel(orphan, this));
        }
    }

    private void UpdateTimeDisplay()
    {
        var now = DateTime.Now;
        CurrentTimeDisplay = now.ToString("HH:mm");
        CurrentDateDisplay = now.ToString("ddd, MMM d");

        if (IsFocusing)
        {
            UpdateFocusTimeDisplay();
        }

        UpdateTimelineView();
    }

    private void UpdateTimelineView()
    {
        // Calculate visible time range based on scale
        var now = DateTime.Now;
        var today = now.Date;

        // Find earliest session time to include it in view (convert UTC to local)
        // Take a snapshot of intents to avoid collection modified during enumeration
        DateTime? earliestSession = null;
        var intentsSnapshot = Intents.ToList();
        foreach (var intent in intentsSnapshot)
        {
            if (intent?.Sessions == null) continue;
            foreach (var session in intent.Sessions.ToList())
            {
                if (session == null) continue;
                var localTime = session.StartTime.ToLocalTime();
                if (earliestSession == null || localTime < earliestSession)
                    earliestSession = localTime;
            }
        }

        // Default view adapts based on current time and earliest session
        switch (CurrentTimeScale)
        {
            case TimeScale.Minutes:
                // 40 min total span, now at 80% (32min past, 8min future)
                ViewStartTime = now.AddMinutes(-32);
                ViewEndTime = now.AddMinutes(8);
                break;
            case TimeScale.Hours:
                // ~5 hour span, now at ~85% (4.5h past, 0.75h future)
                var defaultStart = now.AddHours(-4.5);
                if (earliestSession.HasValue && earliestSession.Value.Date == today)
                {
                    var sessionHour = earliestSession.Value.Date.AddHours(earliestSession.Value.Hour);
                    ViewStartTime = sessionHour < defaultStart ? sessionHour : defaultStart;
                }
                else
                {
                    ViewStartTime = defaultStart;
                }
                ViewEndTime = now.AddMinutes(45);
                break;
            case TimeScale.Days:
                // ~4 day span, now at ~80% (3.5 days past, 0.5 day future)
                ViewStartTime = today.AddDays(-3).AddHours(-12);
                ViewEndTime = now.AddHours(12);
                break;
        }

        // Calculate PixelsPerMinute to fill the container width
        var totalMinutes = (ViewEndTime - ViewStartTime).TotalMinutes;
        if (totalMinutes > 0 && _containerWidth > 100)
        {
            PixelsPerMinute = _containerWidth / totalMinutes;
        }
        TimelineWidth = _containerWidth;

        // Generate time markers
        UpdateTimeMarkers();

        // Update session positions (use snapshot to avoid collection modified during enumeration)
        foreach (var intent in intentsSnapshot)
        {
            if (intent?.Sessions == null) continue;
            foreach (var session in intent.Sessions.ToList())
            {
                session?.UpdatePosition(ViewStartTime, ViewEndTime, PixelsPerMinute);
            }
        }
    }

    private void UpdateTimeMarkers()
    {
        try
        {
            // Build new markers first to minimize collection modification time
            var newMarkers = new List<TimeMarker>();

            // Determine marker interval based on scale
            TimeSpan interval = CurrentTimeScale switch
            {
                TimeScale.Minutes => TimeSpan.FromMinutes(5),
                TimeScale.Hours => TimeSpan.FromHours(1),
                TimeScale.Days => TimeSpan.FromDays(1), // Show only day boundaries
                _ => TimeSpan.FromHours(1)
            };

            var current = ViewStartTime;
            // Round to interval
            var totalMinutes = (int)interval.TotalMinutes;
            var minutes = (int)current.TimeOfDay.TotalMinutes;
            var roundedMinutes = (minutes / totalMinutes) * totalMinutes;
            current = current.Date.AddMinutes(roundedMinutes);

            while (current <= ViewEndTime)
            {
                var x = (current - ViewStartTime).TotalMinutes * PixelsPerMinute;
                var isMajor = CurrentTimeScale switch
                {
                    TimeScale.Minutes => current.Minute == 0,
                    TimeScale.Hours => current.Minute == 0,
                    TimeScale.Days => current.Hour == 0,
                    _ => false
                };

                newMarkers.Add(new TimeMarker
                {
                    Time = current,
                    XPosition = x,
                    Label = FormatTimeMarker(current),
                    IsMajor = isMajor
                });

                current = current.Add(interval);
            }

            // Now update the collection
            TimeMarkers.Clear();
            foreach (var marker in newMarkers)
            {
                TimeMarkers.Add(marker);
            }
        }
        catch (InvalidOperationException)
        {
            // Collection was modified during enumeration by UI, skip this update
        }
        catch (ArgumentOutOfRangeException)
        {
            // Collection index out of range during concurrent access, skip this update
        }
    }

    private string FormatTimeMarker(DateTime time) => CurrentTimeScale switch
    {
        TimeScale.Minutes => time.ToString("HH:mm"),
        TimeScale.Hours => time.ToString("HH:mm"),
        TimeScale.Days => time.ToString("ddd d"),  // e.g., "Fri 27"
        _ => time.ToString("HH:mm")
    };

    /// <summary>
    /// Get X position for current time indicator.
    /// </summary>
    public double CurrentTimeXPosition => (DateTime.Now - ViewStartTime).TotalMinutes * PixelsPerMinute;

    private void UpdateFocusTimeDisplay()
    {
        var focusTime = _timelineService.GetTotalFocusTime();
        if (focusTime.TotalHours >= 1)
        {
            FocusTimeDisplay = $"{(int)focusTime.TotalHours}h {focusTime.Minutes}m";
        }
        else
        {
            FocusTimeDisplay = $"{focusTime.Minutes}m {focusTime.Seconds}s";
        }
    }

    private void RefreshIntents()
    {
        var orderedIntents = _timelineService.GetOrderedIntents();
        Intents.Clear();

        foreach (var intent in orderedIntents)
        {
            var sessions = _timelineService.GetSessionsForIntent(intent.Id);
            var vm = new IntentRowViewModel(intent, sessions, this);
            Intents.Add(vm);
        }
    }

    private void UpdateStatusBar()
    {
        var allIntents = _timelineService.GetAllIntents();
        var allSessions = _timelineService.GetAllSessions();

        TotalIntentsCount = allIntents.Count;
        ActiveForksCount = allSessions.Count(s => !string.IsNullOrEmpty(s.ParentSessionId));
        RunningSessionsCount = allSessions.Count(s => s.Status == ClaudeSessionStatus.Running);
        TotalCommitsCount = allSessions.Count(s => !string.IsNullOrEmpty(s.CommitHash));

        // Update troubleshooting status
        UpdateTroubleshootingStatus();
    }

    private void UpdateTroubleshootingStatus()
    {
        // Auto-detect if plugin is installed by checking Claude's installed_plugins.json
        HooksInstalled = DetectHooksInstalled();

        // Count unassigned sessions (sessions not matched to any intent)
        var allSessions = _timelineService.GetAllSessions();
        var intentIds = _timelineService.GetAllIntents().Select(i => i.Id).ToHashSet();
        UnassignedSessionsCount = allSessions.Count(s => string.IsNullOrEmpty(s.IntentId) || !intentIds.Contains(s.IntentId));

        // Build status message
        if (!HooksInstalled)
        {
            HookStatusMessage = "Hooks not installed - Install plugin to track sessions";
        }
        else if (TotalIntentsCount == 0)
        {
            HookStatusMessage = "No intents - Create an intent to start tracking";
        }
        else if (RunningSessionsCount > 0)
        {
            HookStatusMessage = $"{RunningSessionsCount} active session(s)";
        }
        else
        {
            HookStatusMessage = "Ready - Start Claude Code in an intent folder";
        }
    }

    /// <summary>
    /// Detects if the TerminalHost session tracker plugin is installed in Claude Code.
    /// Checks ~/.claude/plugins/installed_plugins.json for the plugin entry.
    /// </summary>
    private static bool DetectHooksInstalled()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var installedPluginsPath = Path.Combine(userProfile, ".claude", "plugins", "installed_plugins.json");

            if (!File.Exists(installedPluginsPath))
                return false;

            var json = File.ReadAllText(installedPluginsPath);

            // Check if our plugin is in the installed plugins list
            // Look for "terminalhost-session-tracker" in the JSON
            return json.Contains("terminalhost-session-tracker", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private void ToggleTroubleshooting()
    {
        ShowTroubleshooting = !ShowTroubleshooting;
    }

    #region Event Handlers

    private void OnEnabledChanged(object? sender, bool enabled)
    {
        IsEnabled = enabled;
    }

    private void OnIntentsChanged(object? sender, EventArgs e)
    {
        RefreshIntents();
        UpdateTimelineView(); // Recalculate positions for new view models
        UpdateStatusBar();
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        // Remember selected session ID before refresh
        var selectedSessionId = SelectedSession?.Id;

        RefreshIntents();
        UpdateTimelineView(); // Recalculate positions for new view models
        UpdateStatusBar();

        // Re-select the session if one was selected (the old VM is gone, need to find new one)
        if (selectedSessionId != null)
        {
            foreach (var intent in Intents)
            {
                var session = intent.Sessions.FirstOrDefault(s => s.Id == selectedSessionId);
                if (session != null)
                {
                    SelectedSession = session;
                    IsSessionDetailVisible = true;
                    return;
                }
            }
            // Session not found - close detail panel
            SelectedSession = null;
            IsSessionDetailVisible = false;
        }
    }

    private void OnFocusStateChanged(object? sender, bool isFocusing)
    {
        IsFocusing = isFocusing;
        UpdateFocusTimeDisplay();
    }

    private void OnTimeScaleChanged(object? sender, TimeScale scale)
    {
        CurrentTimeScale = scale;
        UpdateTimelineView();
    }

    private void OnOrphanSessionsChanged(object? sender, EventArgs e)
    {
        RefreshOrphanSessions();
        UpdateStatusBar();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void SetTimeScale(TimeScale scale)
    {
        _timelineService.SetTimeScale(scale);
    }

    [RelayCommand]
    private void ToggleFocus()
    {
        if (IsFocusing)
        {
            _timelineService.PauseFocusTimer();
        }
        else
        {
            _timelineService.StartFocusTimer();
        }
    }

    [RelayCommand]
    private void ResetFocusTime()
    {
        _timelineService.ResetFocusTime();
        UpdateFocusTimeDisplay();
    }

    [RelayCommand]
    private async Task CreateNewIntent()
    {
        // Get the main repo path from config (first open folder)
        var config = _configService.Load();
        var mainRepoPath = config.OpenFolders?.FirstOrDefault();

        if (string.IsNullOrEmpty(mainRepoPath))
        {
            _dialogService.ShowError("Please open a project before creating an intent.", "No project open");
            return;
        }

        // Get branches for the repo
        var branches = await _gitStatusService.GetBranchesAsync(mainRepoPath);
        var openFolders = config.OpenFolders ?? [];

        // Show the Create Intent dialog
        var result = _dialogService.ShowCreateIntentDialog(
            mainRepoPath,
            branches,
            mainRepoPath,
            openFolders);

        if (result == null)
            return;

        Intent? intent;
        if (result.UseExistingFolder && !string.IsNullOrEmpty(result.ExistingFolderPath))
        {
            // Create intent from existing folder
            intent = await _timelineService.CreateIntentFromExistingFolderAsync(
                result.Name,
                result.ExistingFolderPath,
                result.Context);
        }
        else if (!string.IsNullOrEmpty(result.BranchName) && !string.IsNullOrEmpty(result.WorktreePath))
        {
            // Create intent with new worktree
            intent = await _timelineService.CreateIntentAsync(
                result.Name,
                result.BranchName,
                mainRepoPath,
                context: result.Context);

            if (intent == null)
            {
                _dialogService.ShowError("Could not create the worktree for this intent.", "Failed to create intent");
            }
        }
    }

    [RelayCommand]
    public void SelectSession(SessionBlockViewModel? session)
    {
        SelectedSession = session;
        IsSessionDetailVisible = session != null;
    }

    [RelayCommand]
    private void CloseSessionDetail()
    {
        SelectedSession = null;
        IsSessionDetailVisible = false;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Public Methods for Child ViewModels

    public void StartSession(string intentId, string? prompt = null)
    {
        _timelineService.StartSession(intentId, prompt);
    }

    public async Task ForkSession(string sessionId)
    {
        var prompt = _dialogService.ShowInput("Initial prompt for new fork:", "Fork Session");

        await _timelineService.ForkSessionAsync(sessionId, prompt);
    }

    public async Task CherryPickSession(string sessionId)
    {
        // Show dialog to select target intent
        var intents = _timelineService.GetAllIntents()
            .Where(i => i.Id != _timelineService.GetSession(sessionId)?.IntentId)
            .ToList();

        if (!intents.Any())
        {
            _dialogService.ShowError("Create another intent to cherry-pick to.", "No target intents");
            return;
        }

        // Build message with options
        var message = "Select target intent:\n\n" + string.Join("\n", intents.Select((i, idx) => $"{idx + 1}. {i.Name}"));
        var buttons = intents.Select(i => i.Name).ToArray();

        var selected = _dialogService.ShowCustomButtons(message, "Cherry-pick to Intent", buttons);

        if (selected >= 0 && selected < intents.Count)
        {
            var result = await _timelineService.CherryPickSessionAsync(sessionId, intents[selected].Id);
            if (!result.Success)
            {
                _dialogService.ShowError(result.Error ?? "Unknown error", "Cherry-pick failed");
            }
        }
    }

    public void UpdateIntentStatus(string intentId, IntentStatus status)
    {
        _timelineService.UpdateIntentStatus(intentId, status);
    }

    public void SaveIntentExpandedState(string intentId, bool isExpanded)
    {
        var intent = _timelineService.GetIntent(intentId);
        if (intent != null)
        {
            intent.IsExpanded = isExpanded;
            _timelineService.UpdateIntent(intent);
        }
    }

    public async Task DeleteIntent(string intentId)
    {
        var confirm = _dialogService.ShowConfirmation("Delete this intent and its worktree?", "Delete Intent");

        if (confirm)
        {
            await _timelineService.DeleteIntentAsync(intentId, removeWorktree: true);
        }
    }

    public void SetCurrentIntent(string? intentId)
    {
        _timelineService.SetCurrentIntent(intentId);
    }

    public void MarkSessionSuccess(string sessionId)
    {
        _timelineService.MarkSessionSuccess(sessionId);
    }

    public void MarkSessionFailed(string sessionId)
    {
        _timelineService.MarkSessionFailed(sessionId);
    }

    public void MarkSessionAbandoned(string sessionId)
    {
        _timelineService.MarkSessionAbandoned(sessionId);
    }

    #endregion

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();

        _timelineService.EnabledChanged -= OnEnabledChanged;
        _timelineService.IntentsChanged -= OnIntentsChanged;
        _timelineService.SessionsChanged -= OnSessionsChanged;
        _timelineService.FocusStateChanged -= OnFocusStateChanged;
        _timelineService.TimeScaleChanged -= OnTimeScaleChanged;
        _timelineService.OrphanSessionsChanged -= OnOrphanSessionsChanged;
    }

    #region Orphan Session Methods

    /// <summary>
    /// Creates a new intent from an orphan session's working directory.
    /// </summary>
    public async Task CreateIntentFromOrphan(string orphanSessionId)
    {
        var orphans = _timelineService.GetOrphanSessions();
        var orphan = orphans.FirstOrDefault(o => o.SessionId == orphanSessionId);
        if (orphan == null) return;

        // Prompt for intent name
        var name = _dialogService.ShowInput(
            $"Intent name for '{orphan.DisplayName}':",
            "Create Intent from Session");

        if (string.IsNullOrEmpty(name)) return;

        // Create intent from the orphan's working directory
        var intent = await _timelineService.CreateIntentFromExistingFolderAsync(
            name,
            orphan.Cwd,
            context: null);

        if (intent == null)
        {
            _dialogService.ShowError("Failed to create intent.", "Error");
        }
    }

    /// <summary>
    /// Removes/dismisses an orphan session.
    /// </summary>
    public void DismissOrphan(string orphanSessionId)
    {
        _timelineService.RemoveOrphanSession(orphanSessionId);
    }

    #endregion
}

/// <summary>
/// ViewModel for an orphan (unassigned) Claude Code session.
/// </summary>
public partial class OrphanSessionViewModel : ObservableObject
{
    private readonly OrphanSession _orphan;
    private readonly TimelineTabViewModel _parent;

    public string SessionId => _orphan.SessionId;
    public string Cwd => _orphan.Cwd;
    public string DisplayName => _orphan.DisplayName;
    public string DurationDisplay => _orphan.DurationDisplay;
    public bool IsRunning => _orphan.IsRunning;
    public int FileCount => _orphan.FilesModified.Count;
    public string StartTimeDisplay => _orphan.StartTime.ToLocalTime().ToString("MM/dd HH:mm");
    public string? LastActivityDisplay => _orphan.LastActivityTime?.ToLocalTime().ToString("MM/dd HH:mm");

    public string StatusText => IsRunning ? "Running" : "Completed";
    public string StatusIcon => IsRunning ? "●" : "✓";

    public OrphanSessionViewModel(OrphanSession orphan, TimelineTabViewModel parent)
    {
        _orphan = orphan;
        _parent = parent;
    }

    [RelayCommand]
    private async Task CreateIntent()
    {
        await _parent.CreateIntentFromOrphan(SessionId);
    }

    [RelayCommand]
    private void Dismiss()
    {
        _parent.DismissOrphan(SessionId);
    }
}

/// <summary>
/// Represents a time marker on the timeline ruler.
/// </summary>
public class TimeMarker
{
    public DateTime Time { get; set; }
    public double XPosition { get; set; }
    public string Label { get; set; } = "";
    public bool IsMajor { get; set; }
}
