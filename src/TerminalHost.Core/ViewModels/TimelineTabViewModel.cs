using System.Collections.ObjectModel;
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
    private System.Timers.Timer? _refreshTimer;

    public string Title => "Timeline";
    public string TabIcon => "⏱️";
    public bool IsCloseable => true;
    public bool CanDuplicate => false;
    public string WorkingDirectory => string.Empty;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsSelected { get; set; }
    public bool IsVisibleInFocusMode => true;
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

    #endregion

    public TimelineTabViewModel(
        ITimelineService timelineService,
        IDialogService dialogService,
        IConfigurationService configService)
    {
        _timelineService = timelineService;
        _dialogService = dialogService;
        _configService = configService;

        // Subscribe to events
        _timelineService.EnabledChanged += OnEnabledChanged;
        _timelineService.IntentsChanged += OnIntentsChanged;
        _timelineService.SessionsChanged += OnSessionsChanged;
        _timelineService.FocusStateChanged += OnFocusStateChanged;
        _timelineService.TimeScaleChanged += OnTimeScaleChanged;

        // Initialize state
        LoadState();
        UpdateTimeDisplay();

        // Start refresh timer for time display
        _refreshTimer = new System.Timers.Timer(1000);
        _refreshTimer.Elapsed += (s, e) => UpdateTimeDisplay();
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
        UpdateStatusBar();
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
    }

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
    }

    #region Event Handlers

    private void OnEnabledChanged(object? sender, bool enabled)
    {
        IsEnabled = enabled;
    }

    private void OnIntentsChanged(object? sender, EventArgs e)
    {
        RefreshIntents();
        UpdateStatusBar();
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        RefreshIntents();
        UpdateStatusBar();
    }

    private void OnFocusStateChanged(object? sender, bool isFocusing)
    {
        IsFocusing = isFocusing;
        UpdateFocusTimeDisplay();
    }

    private void OnTimeScaleChanged(object? sender, TimeScale scale)
    {
        CurrentTimeScale = scale;
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
        // Show dialog to get intent details
        var name = _dialogService.ShowInput("Intent name:", "New Intent", "Implement feature...");

        if (string.IsNullOrWhiteSpace(name))
            return;

        var branchName = _dialogService.ShowInput("Branch name:", "New Intent", SuggestBranchName(name));

        if (string.IsNullOrWhiteSpace(branchName))
            return;

        // Get the main repo path from config (first open folder)
        var config = _configService.Load();
        var mainRepoPath = config.OpenFolders?.FirstOrDefault();

        if (string.IsNullOrEmpty(mainRepoPath))
        {
            _dialogService.ShowError("Please open a project before creating an intent.", "No project open");
            return;
        }

        var context = _dialogService.ShowInput("Context for Claude Code sessions (optional):", "Intent Context");

        var intent = await _timelineService.CreateIntentAsync(
            name,
            branchName,
            mainRepoPath,
            context: context);

        if (intent == null)
        {
            _dialogService.ShowError("Could not create the worktree for this intent.", "Failed to create intent");
        }
    }

    private static string SuggestBranchName(string intentName)
    {
        // Convert "Implement user auth" -> "feature/implement-user-auth"
        var sanitized = intentName
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        // Remove any non-alphanumeric characters except hyphens
        sanitized = new string(sanitized
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());

        // Collapse multiple hyphens
        while (sanitized.Contains("--"))
            sanitized = sanitized.Replace("--", "-");

        return $"feature/{sanitized.Trim('-')}";
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
    }
}
