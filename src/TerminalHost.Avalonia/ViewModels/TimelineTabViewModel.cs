using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;
using ITimerService = TerminalHost.Services.ITimerService;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Timeline Mode tab.
/// </summary>
public partial class TimelineTabViewModel : ObservableObject, ITabViewModel
{
    private readonly ITimelineService _timelineService;
    private readonly IDialogService _dialogService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ITimerService _timerService;
    private readonly IPlatformTimer _refreshTimer;

    public TimelineTabViewModel(
        ITimelineService timelineService,
        IDialogService dialogService,
        IFolderPickerService folderPickerService,
        ITimerService timerService)
    {
        _timelineService = timelineService;
        _dialogService = dialogService;
        _folderPickerService = folderPickerService;
        _timerService = timerService;

        // Initialize collections
        RefreshIntents();

        // Subscribe to service events
        _timelineService.EnabledChanged += OnEnabledChanged;
        _timelineService.IntentsChanged += OnIntentsChanged;
        _timelineService.CurrentIntentChanged += OnCurrentIntentChanged;
        _timelineService.FocusStateChanged += OnFocusStateChanged;

        // Set up refresh timer for time displays (1 second)
        _refreshTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(1), RefreshTimeDisplays);
        _refreshTimer.Start();
    }

    // ITabViewModel implementation
    public string Title => "Timeline";
    public string TabIcon => "📅";
    public string WorkingDirectory => string.Empty;
    public bool IsCloseable => true;
    public bool CanDuplicate => false;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsSelected { get; set; }
    public bool IsVisibleInFocusMode => true;
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

    // Observable properties
    [ObservableProperty]
    private ObservableCollection<IntentRowViewModel> _intents = [];

    [ObservableProperty]
    private IntentRowViewModel? _selectedIntent;

    [ObservableProperty]
    private SessionBlockViewModel? _selectedSession;

    [ObservableProperty]
    private TimeScale _currentTimeScale;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isFocusing;

    [ObservableProperty]
    private string _focusTimeDisplay = "0m";

    [ObservableProperty]
    private string _currentTimeDisplay = "";

    [ObservableProperty]
    private string _currentDateDisplay = "";

    [ObservableProperty]
    private bool _isSessionDetailVisible;

    // Stats
    public int TotalIntentsCount => Intents.Count;
    public int ActiveIntentsCount => Intents.Count(i => i.Status == IntentStatus.Active);
    public int RunningSessionsCount => Intents.SelectMany(i => i.Sessions).Count(s => s.IsRunning);
    public int TotalSessionsCount => Intents.Sum(i => i.SessionCount);
    public int TotalCommitsCount => Intents.SelectMany(i => i.Sessions).Count(s => s.HasCommit);

    // Create intent dialog state
    [ObservableProperty]
    private bool _isCreateIntentDialogOpen;

    [ObservableProperty]
    private string _newIntentName = "";

    [ObservableProperty]
    private string _newIntentBranch = "";

    [ObservableProperty]
    private string _newIntentRepoPath = "";

    [ObservableProperty]
    private bool _createFromExistingFolder;

    // Event handlers
    private void OnEnabledChanged(object? sender, bool enabled)
    {
        IsEnabled = enabled;
    }

    private void OnIntentsChanged(object? sender, EventArgs e)
    {
        RefreshIntents();
    }

    private void OnCurrentIntentChanged(object? sender, Intent? intent)
    {
        if (intent == null)
        {
            SelectedIntent = null;
        }
        else
        {
            SelectedIntent = Intents.FirstOrDefault(i => i.Id == intent.Id);
        }
    }

    private void OnFocusStateChanged(object? sender, bool focusing)
    {
        IsFocusing = focusing;
        UpdateFocusTimeDisplay();
    }

    private void RefreshTimeDisplays()
    {
        var now = DateTime.Now;
        CurrentTimeDisplay = now.ToString("HH:mm:ss");
        CurrentDateDisplay = now.ToString("ddd, MMM d");
        UpdateFocusTimeDisplay();
    }

    private void UpdateFocusTimeDisplay()
    {
        var time = _timelineService.GetCurrentFocusTime();
        if (time.TotalHours >= 1)
            FocusTimeDisplay = $"{(int)time.TotalHours}h {time.Minutes}m";
        else if (time.TotalMinutes >= 1)
            FocusTimeDisplay = $"{(int)time.TotalMinutes}m";
        else
            FocusTimeDisplay = "< 1m";
    }

    private void RefreshIntents()
    {
        // Dispose old view models
        foreach (var intent in Intents)
        {
            intent.Dispose();
        }

        // Get ordered intents
        var orderedIntents = _timelineService.GetOrderedIntents();

        Intents.Clear();
        foreach (var intent in orderedIntents)
        {
            var vm = new IntentRowViewModel(intent, _timelineService);
            Intents.Add(vm);
        }

        OnPropertyChanged(nameof(TotalIntentsCount));
        OnPropertyChanged(nameof(ActiveIntentsCount));
        OnPropertyChanged(nameof(RunningSessionsCount));
        OnPropertyChanged(nameof(TotalSessionsCount));
        OnPropertyChanged(nameof(TotalCommitsCount));
    }

    partial void OnSelectedIntentChanged(IntentRowViewModel? oldValue, IntentRowViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.IsSelected = false;
        if (newValue != null)
            newValue.IsSelected = true;
    }

    partial void OnSelectedSessionChanged(SessionBlockViewModel? oldValue, SessionBlockViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.IsSelected = false;
        if (newValue != null)
        {
            newValue.IsSelected = true;
            IsSessionDetailVisible = true;
        }
        else
        {
            IsSessionDetailVisible = false;
        }
    }

    // Commands

    [RelayCommand]
    private void SetTimeScale(TimeScale scale)
    {
        // SetTimeScale was removed from ITimelineService - just update local state
        CurrentTimeScale = scale;
    }

    [RelayCommand]
    private void ToggleFocus()
    {
        if (_timelineService.IsFocusing)
            _timelineService.PauseFocusTimer();
        else
            _timelineService.StartFocusTimer();
    }

    [RelayCommand]
    private void ResetFocusTime()
    {
        _timelineService.ResetFocusTime();
        UpdateFocusTimeDisplay();
    }

    [RelayCommand]
    private void OpenCreateIntentDialog()
    {
        NewIntentName = "";
        NewIntentBranch = "";
        NewIntentRepoPath = "";
        CreateFromExistingFolder = false;
        IsCreateIntentDialogOpen = true;
    }

    [RelayCommand]
    private void CloseCreateIntentDialog()
    {
        IsCreateIntentDialogOpen = false;
    }

    [RelayCommand]
    private void BrowseForRepoPath()
    {
        var path = _folderPickerService.PickFolder("Select Repository");
        if (!string.IsNullOrEmpty(path))
        {
            NewIntentRepoPath = path;
        }
    }

    [RelayCommand]
    private async Task CreateIntent()
    {
        if (string.IsNullOrWhiteSpace(NewIntentName))
        {
            _dialogService.ShowError("Please enter an intent name.");
            return;
        }

        try
        {
            if (CreateFromExistingFolder)
            {
                if (string.IsNullOrWhiteSpace(NewIntentRepoPath))
                {
                    _dialogService.ShowError("Please select a folder.");
                    return;
                }
                await _timelineService.CreateIntentFromExistingFolderAsync(NewIntentName, NewIntentRepoPath);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(NewIntentBranch))
                {
                    _dialogService.ShowError("Please enter a branch name.");
                    return;
                }
                await _timelineService.CreateIntentAsync(NewIntentName, NewIntentBranch, NewIntentRepoPath);
            }

            IsCreateIntentDialogOpen = false;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to create intent: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CloseSessionDetail()
    {
        SelectedSession = null;
        IsSessionDetailVisible = false;
    }

    [RelayCommand]
    private void NavigateIntent(bool up)
    {
        if (Intents.Count == 0) return;

        var currentIndex = SelectedIntent != null ? Intents.IndexOf(SelectedIntent) : -1;
        int newIndex;

        if (up)
        {
            newIndex = currentIndex <= 0 ? Intents.Count - 1 : currentIndex - 1;
        }
        else
        {
            newIndex = currentIndex >= Intents.Count - 1 ? 0 : currentIndex + 1;
        }

        SelectedIntent = Intents[newIndex];
        _timelineService.SetCurrentIntent(SelectedIntent.Id);
    }

    [RelayCommand]
    private void NavigateSession(bool left)
    {
        if (SelectedIntent == null || SelectedIntent.Sessions.Count == 0) return;

        var sessions = SelectedIntent.Sessions;
        var currentIndex = SelectedSession != null ? sessions.IndexOf(SelectedSession) : -1;
        int newIndex;

        if (left)
        {
            newIndex = currentIndex <= 0 ? sessions.Count - 1 : currentIndex - 1;
        }
        else
        {
            newIndex = currentIndex >= sessions.Count - 1 ? 0 : currentIndex + 1;
        }

        SelectedSession = sessions[newIndex];
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Cleans up resources.
    /// </summary>
    public void Dispose()
    {
        _refreshTimer.Dispose();

        _timelineService.EnabledChanged -= OnEnabledChanged;
        _timelineService.IntentsChanged -= OnIntentsChanged;
        _timelineService.CurrentIntentChanged -= OnCurrentIntentChanged;
        _timelineService.FocusStateChanged -= OnFocusStateChanged;

        foreach (var intent in Intents)
        {
            intent.Dispose();
        }
    }
}
