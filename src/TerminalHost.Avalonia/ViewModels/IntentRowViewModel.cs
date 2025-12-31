using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for an intent row (swimlane) in the timeline.
/// </summary>
public partial class IntentRowViewModel : ObservableObject
{
    private readonly ITimelineService _timelineService;
    private readonly Intent _intent;

    public IntentRowViewModel(Intent intent, ITimelineService timelineService)
    {
        _intent = intent;
        _timelineService = timelineService;

        // Initialize sessions
        RefreshSessions();

        // Subscribe to service events
        _timelineService.SessionsChanged += OnSessionsChanged;
        _timelineService.SessionStatusChanged += OnSessionStatusChanged;
    }

    // Properties from intent
    public string Id => _intent.Id;
    public string Name => _intent.Name;
    public string BranchName => _intent.BranchName;
    public string WorktreePath => _intent.WorktreePath;
    public IntentStatus Status => _intent.Status;
    public string StatusIcon => _intent.StatusIcon;
    public string StatusColorHex => _intent.StatusColorHex;
    public string FocusTimeDisplay => _intent.FocusTimeDisplay;
    public string ShortBranchName => _intent.ShortBranchName;
    public bool HasContext => _intent.HasContext;

    // UI state
    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    // Sessions collection
    public ObservableCollection<SessionBlockViewModel> Sessions { get; } = [];

    // Computed properties
    public bool HasRunningSession => Sessions.Any(s => s.IsRunning);
    public int SessionCount => Sessions.Count;
    public int CompletedSessionCount => Sessions.Count(s => s.IsCompleted);

    // Event handlers
    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        RefreshSessions();
    }

    private void OnSessionStatusChanged(object? sender, ClaudeSession session)
    {
        if (session.IntentId == Id)
        {
            var vm = Sessions.FirstOrDefault(s => s.Id == session.Id);
            vm?.Refresh();
            OnPropertyChanged(nameof(HasRunningSession));
        }
    }

    // Commands
    [RelayCommand]
    private void Select()
    {
        _timelineService.SetCurrentIntent(Id);
        IsSelected = true;
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void StartNewSession()
    {
        _timelineService.StartSession(Id);
    }

    [RelayCommand]
    private void MarkComplete()
    {
        _timelineService.UpdateIntentStatus(Id, IntentStatus.Completed);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
    }

    [RelayCommand]
    private void MarkPaused()
    {
        _timelineService.UpdateIntentStatus(Id, IntentStatus.Paused);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
    }

    [RelayCommand]
    private void MarkActive()
    {
        _timelineService.UpdateIntentStatus(Id, IntentStatus.Active);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
    }

    [RelayCommand]
    private void Abandon()
    {
        _timelineService.UpdateIntentStatus(Id, IntentStatus.Abandoned);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
    }

    [RelayCommand]
    private async Task Delete()
    {
        await _timelineService.DeleteIntentAsync(Id, removeWorktree: false);
    }

    [RelayCommand]
    private async Task DeleteWithWorktree()
    {
        await _timelineService.DeleteIntentAsync(Id, removeWorktree: true);
    }

    [RelayCommand]
    private void SelectSession(SessionBlockViewModel session)
    {
        // Deselect all others
        foreach (var s in Sessions)
            s.IsSelected = false;

        session.IsSelected = true;
    }

    [RelayCommand]
    private async Task ForkSession(SessionBlockViewModel session)
    {
        await session.ForkCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task CherryPickSession(SessionBlockViewModel session)
    {
        // This would normally show a dialog to select target intent
        // For now, just mark as needing implementation
    }

    /// <summary>
    /// Refreshes the sessions collection from the service.
    /// </summary>
    public void RefreshSessions()
    {
        var sessions = _timelineService.GetSessionsForIntent(Id);

        // Clear and repopulate
        Sessions.Clear();
        foreach (var session in sessions.OrderBy(s => s.StartTime))
        {
            Sessions.Add(new SessionBlockViewModel(session, _timelineService));
        }

        OnPropertyChanged(nameof(HasRunningSession));
        OnPropertyChanged(nameof(SessionCount));
        OnPropertyChanged(nameof(CompletedSessionCount));
    }

    /// <summary>
    /// Refreshes all properties from the underlying intent.
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(BranchName));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
        OnPropertyChanged(nameof(FocusTimeDisplay));
        OnPropertyChanged(nameof(ShortBranchName));
        OnPropertyChanged(nameof(HasContext));
        RefreshSessions();
    }

    /// <summary>
    /// Cleans up event subscriptions.
    /// </summary>
    public void Dispose()
    {
        _timelineService.SessionsChanged -= OnSessionsChanged;
        _timelineService.SessionStatusChanged -= OnSessionStatusChanged;
    }
}
