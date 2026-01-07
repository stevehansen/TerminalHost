using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for a workspace (project) entry in the workspace sidebar.
/// </summary>
public partial class WorkspaceEntryViewModel : ObservableObject
{
    private readonly Workspace _workspace;
    private readonly IGitWorktreeService _gitWorktreeService;
    private readonly IGitStatusService _gitStatusService;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCurrentTab;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _hasUnreadActivity;

    [ObservableProperty]
    private bool _isWaitingForInput;

    [ObservableProperty]
    private GitStatus? _gitStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<WorktreeEntryViewModel> _worktrees = [];

    /// <summary>
    /// Event raised when the workspace requests to be opened (terminal created).
    /// </summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>
    /// Event raised when a worktree requests to be opened.
    /// </summary>
    public event EventHandler<string>? WorktreeOpenRequested;

    public WorkspaceEntryViewModel(
        Workspace workspace,
        IGitWorktreeService gitWorktreeService,
        IGitStatusService gitStatusService)
    {
        _workspace = workspace;
        _gitWorktreeService = gitWorktreeService;
        _gitStatusService = gitStatusService;
        _isExpanded = workspace.IsExpanded;
    }

    /// <summary>
    /// Unique identifier.
    /// </summary>
    public string Id => _workspace.Id;

    /// <summary>
    /// Display name for the workspace.
    /// </summary>
    public string Name => _workspace.Name;

    /// <summary>
    /// Full path to the workspace directory.
    /// </summary>
    public string Path => _workspace.Path;

    /// <summary>
    /// Section the workspace belongs to (main or playground).
    /// </summary>
    public string Section => _workspace.Section;

    /// <summary>
    /// Custom icon for the workspace.
    /// </summary>
    public string Icon => _workspace.CustomIcon ?? "📁";

    /// <summary>
    /// Sort order for the workspace.
    /// </summary>
    public int Order
    {
        get => _workspace.Order;
        set
        {
            if (_workspace.Order != value)
            {
                _workspace.Order = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the underlying workspace model.
    /// </summary>
    public Workspace Workspace => _workspace;

    /// <summary>
    /// Text for context menu to move workspace to other section.
    /// </summary>
    public string MoveToSectionHeader => Section == "playground" ? "Move to Workspaces" : "Move to Playground";

    /// <summary>
    /// Current branch name from git status (full name).
    /// </summary>
    public string? CurrentBranch => GitStatus?.BranchName;

    /// <summary>
    /// Short branch name for display (e.g., "#123" for "issues/123").
    /// </summary>
    public string? BranchNameShort => GitStatus?.BranchNameShort;

    /// <summary>
    /// Whether the repository has uncommitted changes.
    /// </summary>
    public bool IsDirty => GitStatus?.IsDirty ?? false;

    /// <summary>
    /// Number of commits ahead of remote.
    /// </summary>
    public int AheadCount => GitStatus?.AheadCount ?? 0;

    /// <summary>
    /// Whether there are commits that can be pushed.
    /// </summary>
    public bool CanPush => AheadCount > 0;

    /// <summary>
    /// Ahead/behind display string.
    /// </summary>
    public string? AheadBehindDisplay
    {
        get
        {
            if (GitStatus == null) return null;
            var parts = new List<string>();
            if (GitStatus.AheadCount > 0) parts.Add($"↑{GitStatus.AheadCount}");
            if (GitStatus.BehindCount > 0) parts.Add($"↓{GitStatus.BehindCount}");
            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        _workspace.IsExpanded = value;
    }

    partial void OnGitStatusChanged(GitStatus? value)
    {
        OnPropertyChanged(nameof(CurrentBranch));
        OnPropertyChanged(nameof(BranchNameShort));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(AheadCount));
        OnPropertyChanged(nameof(CanPush));
        OnPropertyChanged(nameof(AheadBehindDisplay));
    }

    /// <summary>
    /// Loads worktrees and git status for this workspace.
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            // Load git status
            GitStatus = await _gitStatusService.GetGitStatusAsync(Path);

            // Load worktrees (only linked worktrees, not the main one which is the workspace itself)
            var worktreeInfos = await _gitWorktreeService.ListWorktreesAsync(Path);
            Worktrees.Clear();
            foreach (var info in worktreeInfos.Where(w => !w.IsMain))
            {
                var vm = new WorktreeEntryViewModel(info);
                Worktrees.Add(vm);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes git status for this workspace.
    /// </summary>
    public async Task RefreshGitStatusAsync()
    {
        GitStatus = await _gitStatusService.GetGitStatusAsync(Path);
    }

    [RelayCommand]
    private void Open()
    {
        OpenRequested?.Invoke(this, Path);
    }

    [RelayCommand]
    private void OpenWorktree(WorktreeEntryViewModel? worktree)
    {
        if (worktree != null)
        {
            WorktreeOpenRequested?.Invoke(this, worktree.Path);
        }
    }
}
