using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for the Timeline IDE tab.
/// Displays a flat list of session cards sourced from ClaudeSessionIndexService (historical)
/// and TimelineService (live). Uses stable ObservableCollection with in-place property updates.
/// </summary>
public partial class TimelineTabViewModel : ObservableObject, ITabViewModel, IDisposable
{
    private readonly ITimelineService _timelineService;
    private readonly IClaudeSessionIndexService? _sessionIndexService;
    private readonly ISessionLifecycleCoordinator? _coord;
    private readonly IDialogService _dialogService;
    private readonly ITimerService _timerService;
    private readonly IProcessService? _processService;
    private readonly IClipboardService? _clipboardService;
    private IAppTimer? _refreshTimer;


    #region ITabViewModel

    public string Title => "Timeline";
    public string TabIcon => "⏱️";
    public bool IsCloseable => true;
    public bool CanDuplicate => false;
    public string WorkingDirectory => string.Empty;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsVisibleInFocusMode => true;
    [ObservableProperty] private bool _isSelected;
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

    #endregion

    #region Observable Properties

    /// <summary>
    /// The stable session list — never replaced, only reconciled via property updates.
    /// </summary>
    public ObservableCollection<SessionCardViewModel> Sessions { get; } = new();

    [ObservableProperty] private SessionCardViewModel? _selectedSession;
    [ObservableProperty] private bool _isDetailVisible;
    [ObservableProperty] private bool _isEnabled;

    // Focus time
    [ObservableProperty] private bool _isFocusing;
    [ObservableProperty] private string _focusTimeDisplay = "0m";
    [ObservableProperty] private string _currentTimeDisplay = "";
    [ObservableProperty] private string _currentDateDisplay = "";

    // Stats
    [ObservableProperty] private int _totalSessionCount;
    [ObservableProperty] private int _liveSessionCount;
    [ObservableProperty] private int _totalIntentsCount;

    // Troubleshooting
    [ObservableProperty] private bool _hooksInstalled;
    [ObservableProperty] private string _hookStatusMessage = "";
    [ObservableProperty] private bool _hostOnPath = true;
    [ObservableProperty] private bool _showTroubleshooting;

    // Filter
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _showLiveOnly;

    // Filter changes picked up automatically by next reconciliation tick

    #endregion

    public TimelineTabViewModel(
        ITimelineService timelineService,
        IClaudeSessionIndexService? sessionIndexService,
        IDialogService dialogService,
        ITimerService timerService,
        ISessionLifecycleCoordinator? sessionCoordinator = null,
        IProcessService? processService = null,
        IClipboardService? clipboardService = null)
    {
        _timelineService = timelineService;
        _sessionIndexService = sessionIndexService;
        _dialogService = dialogService;
        _timerService = timerService;
        _coord = sessionCoordinator;
        _processService = processService;
        _clipboardService = clipboardService;

        // Subscribe to events that need immediate property updates
        // (reconciliation runs every timer tick regardless)
        _timelineService.EnabledChanged += OnEnabledChanged;
        _timelineService.IntentsChanged += OnIntentsChanged;
        _timelineService.FocusStateChanged += OnFocusStateChanged;

        // Initialize state
        var state = _timelineService.GetState();
        IsEnabled = state.Enabled;
        IsFocusing = state.IsFocusing;
        TotalIntentsCount = state.Intents.Count;
        UpdateFocusTimeDisplay();
        UpdateTimeDisplay();
        UpdateTroubleshootingStatus();

        // Start timer — runs on UI thread, handles reconciliation + time display
        _refreshTimer = _timerService.CreateTimer(TimeSpan.FromSeconds(2), OnTimerTick);
        _refreshTimer.Start();
    }

    #region Timer & Reconciliation

    private void OnTimerTick()
    {
        UpdateTimeDisplay();
        UpdateFocusTimeDisplay();
        ReconcileSessions();
    }

    /// <summary>
    /// Merges historical + live session data into the stable Sessions collection.
    /// Updates existing cards in-place via property setters — no collection replacement.
    /// </summary>
    private void ReconcileSessions()
    {
        // 1. Gather data from both sources
        var liveSessions = _timelineService.GetLiveSessions();
        var historicalSessions = _sessionIndexService?.GetAllSessions()
            ?? (IReadOnlyList<ClaudeSessionIndexEntry>)[];

        // 2. Build desired session map (live takes priority)
        var desired = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var live in liveSessions)
            desired[live.ClaudeSessionId] = live;
        foreach (var hist in historicalSessions)
        {
            if (!desired.ContainsKey(hist.SessionId))
                desired[hist.SessionId] = hist;
        }

        // 3. Apply filters
        var filtered = ApplyFilters(desired);

        // 4. Build existing card lookup
        var existingMap = new Dictionary<string, SessionCardViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in Sessions)
            existingMap[card.SessionId] = card;

        // 5. Remove cards not in filtered set
        var filteredSet = new HashSet<string>(filtered.Select(f => f.id), StringComparer.OrdinalIgnoreCase);
        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!filteredSet.Contains(Sessions[i].SessionId))
                Sessions.RemoveAt(i);
        }

        // 6. Update existing and insert new, maintaining order
        for (int targetIndex = 0; targetIndex < filtered.Count; targetIndex++)
        {
            var (id, source) = filtered[targetIndex];

            if (existingMap.TryGetValue(id, out var existing))
            {
                // Update in place
                UpdateCard(existing, source);

                // Move to correct position if needed
                var currentIndex = Sessions.IndexOf(existing);
                if (currentIndex != targetIndex && currentIndex >= 0 && targetIndex < Sessions.Count)
                {
                    Sessions.Move(currentIndex, targetIndex);
                }
            }
            else
            {
                // Insert new card
                var card = new SessionCardViewModel(id, this);
                UpdateCard(card, source);
                if (targetIndex <= Sessions.Count)
                    Sessions.Insert(targetIndex, card);
                else
                    Sessions.Add(card);
            }
        }

        // 7. Update stats
        TotalSessionCount = Sessions.Count;
        LiveSessionCount = Sessions.Count(s => s.IsLive);
        UpdateTroubleshootingStatus();
    }

    private List<(string id, object source)> ApplyFilters(Dictionary<string, object> desired)
    {
        var results = new List<(string id, object source, DateTime sortTime)>();

        foreach (var (id, source) in desired)
        {
            // Live-only filter: show active sessions + recently ended (within 60s)
            if (ShowLiveOnly)
            {
                if (source is LiveSession live)
                {
                    if (!live.IsActive && live.EndTime.HasValue &&
                        (DateTime.UtcNow - live.EndTime.Value).TotalSeconds > 60)
                        continue;
                }
                else
                    continue; // not a live session at all
            }

            // Search filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var match = false;
                if (source is ClaudeSessionIndexEntry entry)
                {
                    match = MatchesSearch(entry.Summary, SearchText)
                        || MatchesSearch(entry.FirstPrompt, SearchText)
                        || MatchesSearch(entry.GitBranch, SearchText)
                        || MatchesSearch(entry.ProjectPath, SearchText);
                }
                else if (source is LiveSession live)
                {
                    match = MatchesSearch(live.DisplayName, SearchText)
                        || MatchesSearch(live.WorkingDirectory, SearchText);
                }
                if (!match) continue;
            }

            // Sort time (most recent first)
            DateTime sortTime = source switch
            {
                LiveSession live => live.StartTime,
                ClaudeSessionIndexEntry entry => entry.Modified ?? entry.Created ?? DateTime.MinValue,
                _ => DateTime.MinValue
            };

            results.Add((id, source, sortTime));
        }

        // Sort: live first, then by recency
        return results
            .OrderByDescending(r => r.source is LiveSession { IsActive: true } ? 1 : 0)
            .ThenByDescending(r => r.sortTime)
            .Select(r => (r.id, r.source))
            .ToList();
    }

    private static bool MatchesSearch(string? text, string search)
        => text != null && text.Contains(search, StringComparison.OrdinalIgnoreCase);

    private void UpdateCard(SessionCardViewModel card, object source)
    {
        if (source is LiveSession live)
        {
            var state = _coord?.GetSession(live.ClaudeSessionId)?.ActivityState;
            card.UpdateFromLiveSession(live, state?.LastActivityTime);
            if (live.IsActive && _coord != null)
                card.UpdateActivityData(state);
        }
        else if (source is ClaudeSessionIndexEntry entry)
        {
            card.UpdateFromIndexEntry(entry);
        }
    }

    #endregion

    #region Event Handlers

    private void OnEnabledChanged(object? sender, bool enabled)
    {
        IsEnabled = enabled;
        /* reconciliation runs every tick */
    }

    private void OnIntentsChanged(object? sender, EventArgs e)
    {
        TotalIntentsCount = _timelineService.GetAllIntents().Count;
        /* reconciliation runs every tick */
    }

    private void OnFocusStateChanged(object? sender, bool focusing)
    {
        IsFocusing = focusing;
        UpdateFocusTimeDisplay();
    }

    #endregion

    #region Commands

    public void SelectSession(SessionCardViewModel? session)
    {
        SelectedSession = session;
        IsDetailVisible = session != null;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        SelectedSession = null;
        IsDetailVisible = false;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
    private async Task CreateNewIntent()
    {
        var name = _dialogService.ShowInput("Intent name:", "New Intent");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Use folder picker from dialog service (sync)
        var path = _dialogService.ShowInput("Project folder path:", "Select Folder");
        if (string.IsNullOrEmpty(path)) return;

        await _timelineService.CreateIntentFromExistingFolderAsync(name, path);
    }

    [RelayCommand]
    private void InstallHooks()
    {
        _timelineService.InstallHooks();
        UpdateTroubleshootingStatus();
        /* reconciliation runs every tick */
    }

    [RelayCommand]
    private void UninstallHooks()
    {
        _timelineService.UninstallHooks();
        UpdateTroubleshootingStatus();
    }

    [RelayCommand]
    private void ToggleTroubleshooting()
    {
        ShowTroubleshooting = !ShowTroubleshooting;
    }

    [RelayCommand]
    private void OpenSessionFolder(SessionCardViewModel? card)
    {
        var path = card?.ProjectPath;
        if (string.IsNullOrEmpty(path)) return;

        if (_processService != null)
            _processService.OpenFolder(path);
        else
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenSessionTranscript(SessionCardViewModel? card)
    {
        var path = card?.TranscriptPath;
        if (string.IsNullOrEmpty(path)) return;

        // Open the folder containing the transcript file
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            if (_processService != null)
                _processService.OpenFolder(dir);
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
    }

    [RelayCommand]
    private async Task CopySessionId(SessionCardViewModel? card)
    {
        if (card == null || _clipboardService == null) return;
        await _clipboardService.SetTextAsync(card.SessionId);
    }

    /// <summary>Pop-out request for hosting Timeline in a standalone window.</summary>
    public event EventHandler? PopOutRequested;

    [RelayCommand]
    private void PopOut()
    {
        PopOutRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Display Helpers

    private void UpdateTimeDisplay()
    {
        CurrentTimeDisplay = DateTime.Now.ToString("HH:mm:ss");
        CurrentDateDisplay = DateTime.Now.ToString("dddd, MMMM d");
    }

    private void UpdateFocusTimeDisplay()
    {
        var focusTime = _timelineService.GetCurrentFocusTime();
        if (focusTime.TotalHours >= 1)
            FocusTimeDisplay = $"{(int)focusTime.TotalHours}h {focusTime.Minutes:D2}m";
        else
            FocusTimeDisplay = $"{(int)focusTime.TotalMinutes}m";
    }

    private void UpdateTroubleshootingStatus()
    {
        HooksInstalled = _timelineService.AreHooksInstalled();
        HostOnPath = DetectHostOnPath();

        if (!HooksInstalled)
            HookStatusMessage = "Hooks not installed — click Install to enable session tracking";
        else if (!HostOnPath)
            HookStatusMessage = "host.exe not found on PATH — hooks may not work";
        else
            HookStatusMessage = "Session tracking active";
    }

    private static bool DetectHostOnPath()
    {
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in pathDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir, "host.exe");
                if (File.Exists(candidate)) return true;
            }
            catch { }
        }
        return false;
    }

    #endregion

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;

        _timelineService.EnabledChanged -= OnEnabledChanged;
        _timelineService.IntentsChanged -= OnIntentsChanged;
        _timelineService.FocusStateChanged -= OnFocusStateChanged;
    }
}
