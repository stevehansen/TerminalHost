using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for a session block on the timeline.
/// </summary>
public partial class SessionBlockViewModel : ObservableObject
{
    private readonly ITimelineService _timelineService;
    private readonly ClaudeCodeSession _session;

    public SessionBlockViewModel(ClaudeCodeSession session, ITimelineService timelineService)
    {
        _session = session;
        _timelineService = timelineService;

        // Initialize collections
        FilesChanged = new ObservableCollection<FileChangeViewModel>(
            session.FilesChanged.Select(f => new FileChangeViewModel(f)));
        Commands = new ObservableCollection<string>(session.CommandsExecuted);
    }

    // Properties from session
    public string Id => _session.Id;
    public string IntentId => _session.IntentId;
    public ClaudeSessionStatus Status => _session.Status;
    public DateTime StartTime => _session.StartTime;
    public DateTime? EndTime => _session.EndTime;
    public string? CommitHash => _session.CommitHash;
    public string? CommitMessage => _session.CommitMessage;
    public string? AgentNotes => _session.AgentNotes;
    public bool IsFork => _session.IsFork;
    public bool HasCommit => _session.HasCommit;
    public bool HasNotes => _session.HasNotes;
    public bool HasFilesChanged => _session.HasFilesChanged;
    public bool HasCommands => _session.HasCommands;
    public bool IsRunning => _session.IsRunning;
    public bool IsCompleted => _session.IsCompleted;

    // Display properties
    public string StatusIcon => _session.StatusIcon;
    public string StatusColorHex => _session.StatusColorHex;
    public string DurationDisplay => _session.ShortDuration;
    public string TimeRangeDisplay => _session.TimeRangeDisplay;
    public string DisplayTitle => _session.DisplayTitle;
    public string ShortCommitHash => _session.ShortCommitHash;
    public string FilesSummary => _session.FilesSummary;
    public int FileCount => _session.FileCount;

    // Collections
    public ObservableCollection<FileChangeViewModel> FilesChanged { get; }
    public ObservableCollection<string> Commands { get; }

    // Selection state
    [ObservableProperty]
    private bool _isSelected;

    // Commands
    [RelayCommand]
    private void Select()
    {
        IsSelected = true;
    }

    [RelayCommand]
    private async Task Fork()
    {
        await _timelineService.ForkSessionAsync(_session.Id);
    }

    [RelayCommand]
    private async Task CherryPick(string targetIntentId)
    {
        if (!string.IsNullOrEmpty(targetIntentId))
        {
            await _timelineService.CherryPickAsync(_session.Id, targetIntentId);
        }
    }

    [RelayCommand]
    private void MarkSuccess()
    {
        _timelineService.MarkSessionSuccess(_session.Id);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsCompleted));
    }

    [RelayCommand]
    private void MarkFailed()
    {
        _timelineService.MarkSessionFailed(_session.Id);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsCompleted));
    }

    [RelayCommand]
    private void MarkAbandoned()
    {
        _timelineService.MarkSessionAbandoned(_session.Id);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsCompleted));
    }

    /// <summary>
    /// Refreshes the view model from the underlying session data.
    /// </summary>
    public void Refresh()
    {
        var updated = _timelineService.GetSession(_session.Id);
        if (updated == null) return;

        // Update collections
        FilesChanged.Clear();
        foreach (var f in updated.FilesChanged)
            FilesChanged.Add(new FileChangeViewModel(f));

        Commands.Clear();
        foreach (var c in updated.CommandsExecuted)
            Commands.Add(c);

        // Notify all property changes
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColorHex));
        OnPropertyChanged(nameof(EndTime));
        OnPropertyChanged(nameof(CommitHash));
        OnPropertyChanged(nameof(CommitMessage));
        OnPropertyChanged(nameof(AgentNotes));
        OnPropertyChanged(nameof(HasCommit));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasFilesChanged));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(TimeRangeDisplay));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ShortCommitHash));
        OnPropertyChanged(nameof(FilesSummary));
        OnPropertyChanged(nameof(FileCount));
    }
}
