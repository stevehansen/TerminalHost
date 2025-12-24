using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for a git worktree entry in the workspace sidebar.
/// </summary>
public partial class WorktreeEntryViewModel : ObservableObject
{
    private readonly WorktreeInfo _worktreeInfo;

    [ObservableProperty]
    private bool _hasActivity;

    [ObservableProperty]
    private bool _isSelected;

    public WorktreeEntryViewModel(WorktreeInfo worktreeInfo)
    {
        _worktreeInfo = worktreeInfo;
    }

    /// <summary>
    /// Full path to the worktree directory.
    /// </summary>
    public string Path => _worktreeInfo.Path;

    /// <summary>
    /// The branch checked out in this worktree.
    /// </summary>
    public string Branch => _worktreeInfo.Branch;

    /// <summary>
    /// Display name for the worktree.
    /// </summary>
    public string DisplayName => _worktreeInfo.DisplayName;

    /// <summary>
    /// Whether this is the main worktree (original repository).
    /// </summary>
    public bool IsMain => _worktreeInfo.IsMain;

    /// <summary>
    /// Whether the worktree is locked.
    /// </summary>
    public bool IsLocked => _worktreeInfo.IsLocked;

    /// <summary>
    /// Whether HEAD is detached.
    /// </summary>
    public bool IsDetached => _worktreeInfo.IsDetached;

    /// <summary>
    /// Icon to display based on worktree state.
    /// </summary>
    public string Icon => IsMain ? "📁" : (IsDetached ? "🔗" : "🔀");

    /// <summary>
    /// Gets the underlying worktree info.
    /// </summary>
    public WorktreeInfo WorktreeInfo => _worktreeInfo;
}
