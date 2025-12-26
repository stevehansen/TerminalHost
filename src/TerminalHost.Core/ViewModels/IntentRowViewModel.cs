using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for an intent row (swimlane) in the Timeline IDE.
/// </summary>
public partial class IntentRowViewModel : ObservableObject
{
    private readonly TimelineTabViewModel _parent;
    private readonly Intent _intent;

    public string Id => _intent.Id;
    public string Name => _intent.Name;
    public string BranchName => _intent.BranchName;
    public string WorktreePath => _intent.WorktreePath;
    public IntentStatus Status => _intent.Status;

    public string StatusIcon => _intent.StatusIcon;
    public string StatusColorHex => _intent.StatusColorHex;
    public string FocusTimeDisplay => _intent.FocusTimeDisplay;

    public bool HasContext => !string.IsNullOrEmpty(_intent.ContextContent);

    /// <summary>
    /// Short display name for branch (e.g., "#123" for "issues/123").
    /// </summary>
    public string ShortBranchName
    {
        get
        {
            if (BranchName.StartsWith("issues/"))
                return $"#{BranchName[7..]}";
            if (BranchName.StartsWith("feature/"))
                return BranchName[8..];
            if (BranchName.StartsWith("hotfix/"))
                return BranchName[7..];
            if (BranchName.StartsWith("experiment/"))
                return BranchName[11..];
            return BranchName;
        }
    }

    [ObservableProperty]
    private ObservableCollection<SessionBlockViewModel> _sessions = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _hasRunningSession;

    public IntentRowViewModel(Intent intent, IReadOnlyList<ClaudeSession> sessions, TimelineTabViewModel parent)
    {
        _intent = intent;
        _parent = parent;

        // Build session view models
        foreach (var session in sessions.OrderBy(s => s.StartTime))
        {
            Sessions.Add(new SessionBlockViewModel(session, this));
        }

        HasRunningSession = sessions.Any(s => s.Status == ClaudeSessionStatus.Running);
    }

    [RelayCommand]
    private void StartNewSession()
    {
        _parent.StartSession(Id);
    }

    [RelayCommand]
    private void SetStatus(IntentStatus status)
    {
        _parent.UpdateIntentStatus(Id, status);
    }

    [RelayCommand]
    private void MarkComplete()
    {
        _parent.UpdateIntentStatus(Id, IntentStatus.Completed);
    }

    [RelayCommand]
    private void MarkPaused()
    {
        _parent.UpdateIntentStatus(Id, IntentStatus.Paused);
    }

    [RelayCommand]
    private void MarkActive()
    {
        _parent.UpdateIntentStatus(Id, IntentStatus.Active);
    }

    [RelayCommand]
    private void Abandon()
    {
        _parent.UpdateIntentStatus(Id, IntentStatus.Abandoned);
    }

    [RelayCommand]
    private async Task Delete()
    {
        await _parent.DeleteIntent(Id);
    }

    [RelayCommand]
    private void Select()
    {
        _parent.SetCurrentIntent(Id);
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    // Called by SessionBlockViewModel
    public void SelectSession(SessionBlockViewModel session)
    {
        _parent.SelectSession(session);
    }

    public Task ForkSession(string sessionId)
    {
        return _parent.ForkSession(sessionId);
    }

    public Task CherryPickSession(string sessionId)
    {
        return _parent.CherryPickSession(sessionId);
    }

    public void MarkSessionSuccess(string sessionId)
    {
        _parent.MarkSessionSuccess(sessionId);
    }

    public void MarkSessionFailed(string sessionId)
    {
        _parent.MarkSessionFailed(sessionId);
    }

    public void MarkSessionAbandoned(string sessionId)
    {
        _parent.MarkSessionAbandoned(sessionId);
    }
}
