using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for a Claude Code session block in the timeline.
/// </summary>
public partial class SessionBlockViewModel : ObservableObject
{
    private readonly IntentRowViewModel _parent;
    private readonly ClaudeSession _session;

    public string Id => _session.Id;
    public string IntentId => _session.IntentId;
    public ClaudeSessionStatus Status => _session.Status;
    public DateTime StartTime => _session.StartTime;
    public DateTime? EndTime => _session.EndTime;
    public string? CommitHash => _session.CommitHash;
    public string? CommitMessage => _session.CommitMessage;
    public string? AgentNotes => _session.AgentNotes;
    public string? InitialPrompt => _session.InitialPrompt;
    public bool IsFork => !string.IsNullOrEmpty(_session.ParentSessionId);

    public string StatusIcon => _session.StatusIcon;
    public string StatusColorHex => _session.StatusColorHex;
    public string DurationDisplay => _session.DurationDisplay;

    public string ShortCommitHash => CommitHash?.Length > 7 ? CommitHash[..7] : CommitHash ?? "";

    public string TimeRangeDisplay
    {
        get
        {
            var start = StartTime.ToString("HH:mm");
            var end = EndTime?.ToString("HH:mm") ?? "...";
            return $"{start} → {end}";
        }
    }

    public string StatusText => Status switch
    {
        ClaudeSessionStatus.Running => "RUNNING",
        ClaudeSessionStatus.Success => "SUCCESS",
        ClaudeSessionStatus.Failed => "FAILED",
        ClaudeSessionStatus.Abandoned => "ABANDONED",
        _ => "UNKNOWN"
    };

    public bool HasCommit => !string.IsNullOrEmpty(CommitHash);
    public bool HasNotes => !string.IsNullOrEmpty(AgentNotes);
    public bool HasFilesChanged => FilesChanged.Any();
    public bool HasCommands => Commands.Any();
    public bool IsRunning => Status == ClaudeSessionStatus.Running;
    public bool IsCompleted => Status != ClaudeSessionStatus.Running;

    [ObservableProperty]
    private ObservableCollection<FileChangeViewModel> _filesChanged = [];

    [ObservableProperty]
    private ObservableCollection<string> _commands = [];

    public SessionBlockViewModel(ClaudeSession session, IntentRowViewModel parent)
    {
        _session = session;
        _parent = parent;

        // Load file changes
        foreach (var change in session.FilesChanged)
        {
            FilesChanged.Add(new FileChangeViewModel(change));
        }

        // Load commands
        foreach (var cmd in session.CommandsExecuted)
        {
            Commands.Add(cmd);
        }
    }

    [RelayCommand]
    private void Select()
    {
        _parent.SelectSession(this);
    }

    [RelayCommand]
    private async Task Fork()
    {
        await _parent.ForkSession(Id);
    }

    [RelayCommand]
    private async Task CherryPick()
    {
        await _parent.CherryPickSession(Id);
    }

    [RelayCommand]
    private void MarkSuccess()
    {
        _parent.MarkSessionSuccess(Id);
    }

    [RelayCommand]
    private void MarkFailed()
    {
        _parent.MarkSessionFailed(Id);
    }

    [RelayCommand]
    private void MarkAbandoned()
    {
        _parent.MarkSessionAbandoned(Id);
    }
}

/// <summary>
/// ViewModel for a file change in a session.
/// </summary>
public class FileChangeViewModel
{
    public string Path { get; }
    public string FileName { get; }
    public int Additions { get; }
    public int Deletions { get; }
    public string Summary { get; }

    public FileChangeViewModel(FileChange change)
    {
        Path = change.Path;
        FileName = System.IO.Path.GetFileName(change.Path);
        Additions = change.Additions;
        Deletions = change.Deletions;
        Summary = change.ChangeSummary;
    }
}
