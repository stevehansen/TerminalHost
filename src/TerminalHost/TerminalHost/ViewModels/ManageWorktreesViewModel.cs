using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.ViewModels;

/// <summary>
/// View model for worktree items in the Manage Worktrees popup.
/// </summary>
public partial class WorktreeItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _branch = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private bool _isMain;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string? _lockReason;

    [ObservableProperty]
    private bool _isPrunable;

    public string Icon => IsMain ? "📁" : "🔀";
    public bool CanRemove => !IsMain;
    public string LockButtonText => IsLocked ? "Unlock" : "Lock";

    public string StatusBadge
    {
        get
        {
            var parts = new List<string>();
            if (IsMain) parts.Add("Main Worktree");
            if (IsLocked)
            {
                parts.Add(string.IsNullOrEmpty(LockReason) ? "🔒 Locked" : $"🔒 {LockReason}");
            }
            if (IsPrunable) parts.Add("⚠️ Missing");
            return string.Join("  •  ", parts);
        }
    }

    public string StatusColor
    {
        get
        {
            if (IsPrunable) return "#F44336"; // Red
            if (IsLocked) return "#FF9800"; // Orange
            return "#4CAF50"; // Green
        }
    }

    public static WorktreeItemViewModel FromWorktreeInfo(WorktreeInfo info)
    {
        return new WorktreeItemViewModel
        {
            Path = info.Path,
            Branch = info.Branch,
            DisplayName = info.IsMain ? "Main Worktree" : info.DisplayName,
            IsMain = info.IsMain,
            IsLocked = info.IsLocked,
            LockReason = info.LockReason,
            IsPrunable = info.IsPrunable
        };
    }
}

/// <summary>
/// ViewModel for the Manage Worktrees popup.
/// </summary>
public partial class ManageWorktreesViewModel : ObservableObject
{
    private readonly IGitWorktreeService _gitWorktreeService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _repoName = "";

    [ObservableProperty]
    private string _repoPath = "";

    [ObservableProperty]
    private ObservableCollection<WorktreeItemViewModel> _worktrees = [];

    [ObservableProperty]
    private WorktreeItemViewModel? _selectedWorktree;

    [ObservableProperty]
    private double _width = 550;

    [ObservableProperty]
    private double _height = 400;

    /// <summary>
    /// Event raised when user wants to open a worktree path.
    /// </summary>
    public event EventHandler<string>? OpenWorktreeRequested;

    /// <summary>
    /// Event raised when user wants to create a new worktree.
    /// </summary>
    public event EventHandler<string>? CreateWorktreeRequested;

    public ManageWorktreesViewModel(
        IGitWorktreeService gitWorktreeService,
        IDialogService dialogService)
    {
        _gitWorktreeService = gitWorktreeService;
        _dialogService = dialogService;
    }

    /// <summary>
    /// Opens the popup for the specified repository.
    /// </summary>
    public async Task OpenAsync(string repoPath, string repoName)
    {
        RepoPath = repoPath;
        RepoName = repoName;
        await RefreshAsync();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var worktreeInfos = await _gitWorktreeService.ListWorktreesAsync(RepoPath);
            Worktrees.Clear();

            foreach (var info in worktreeInfos.OrderByDescending(w => w.IsMain).ThenBy(w => w.Branch))
            {
                Worktrees.Add(WorktreeItemViewModel.FromWorktreeInfo(info));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenWorktree(WorktreeItemViewModel? worktree)
    {
        if (worktree == null) return;
        OpenWorktreeRequested?.Invoke(this, worktree.Path);
        Close();
    }

    [RelayCommand]
    private async Task RemoveWorktree(WorktreeItemViewModel? worktree)
    {
        if (worktree == null || worktree.IsMain) return;

        var confirmed = _dialogService.ShowConfirmation(
            $"Remove worktree '{worktree.DisplayName}'?\n\nThis will delete the worktree directory:\n{worktree.Path}",
            "Remove Worktree");

        if (!confirmed) return;

        var result = await _gitWorktreeService.RemoveWorktreeAsync(worktree.Path, force: false);
        if (result.Success)
        {
            await RefreshAsync();
        }
        else
        {
            var forceConfirmed = _dialogService.ShowConfirmation(
                $"Worktree has uncommitted changes.\n\nForce remove anyway?\n\nError: {result.Error}",
                "Force Remove Worktree");

            if (forceConfirmed)
            {
                result = await _gitWorktreeService.RemoveWorktreeAsync(worktree.Path, force: true);
                if (result.Success)
                {
                    await RefreshAsync();
                }
                else
                {
                    _dialogService.ShowError($"Failed to remove worktree: {result.Error}");
                }
            }
        }
    }

    [RelayCommand]
    private async Task ToggleLock(WorktreeItemViewModel? worktree)
    {
        if (worktree == null || worktree.IsMain) return;

        GitOperationResult result;
        if (worktree.IsLocked)
        {
            result = await _gitWorktreeService.UnlockWorktreeAsync(worktree.Path);
        }
        else
        {
            var reason = _dialogService.ShowInput(
                "Enter a reason for locking (optional):",
                "Lock Worktree",
                "WIP");

            if (reason == null) return;

            result = await _gitWorktreeService.LockWorktreeAsync(
                worktree.Path,
                string.IsNullOrWhiteSpace(reason) ? null : reason);
        }

        if (result.Success)
        {
            await RefreshAsync();
        }
        else
        {
            _dialogService.ShowError($"Failed to {(worktree.IsLocked ? "unlock" : "lock")} worktree: {result.Error}");
        }
    }

    [RelayCommand]
    private void CopyPath(WorktreeItemViewModel? worktree)
    {
        if (worktree == null) return;
        try
        {
            Clipboard.SetText(worktree.Path);
        }
        catch
        {
            // Clipboard operations can fail
        }
    }

    [RelayCommand]
    private void NewWorktree()
    {
        Close();
        CreateWorktreeRequested?.Invoke(this, RepoPath);
    }

    [RelayCommand]
    private async Task Prune()
    {
        var result = await _gitWorktreeService.PruneWorktreesAsync(RepoPath);
        if (result.Success)
        {
            await RefreshAsync();
        }
        else
        {
            _dialogService.ShowError($"Failed to prune worktrees: {result.Error}");
        }
    }
}
