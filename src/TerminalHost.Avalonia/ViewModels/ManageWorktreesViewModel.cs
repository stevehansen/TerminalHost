using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Manage Worktrees popup.
/// </summary>
public partial class ManageWorktreesViewModel : ObservableObject
{
    private readonly IGitWorktreeService _gitWorktreeService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _repoName = "";

    [ObservableProperty]
    private string _repoPath = "";

    [ObservableProperty]
    private ObservableCollection<WorktreeInfo> _worktrees = [];

    [ObservableProperty]
    private WorktreeInfo? _selectedWorktree;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private double _width = 600;

    [ObservableProperty]
    private double _height = 450;

    /// <summary>
    /// Event raised when user wants to open a worktree path.
    /// </summary>
    public event EventHandler<string>? OpenWorktreeRequested;

    /// <summary>
    /// Event raised when user wants to create a new worktree.
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<string>? CreateWorktreeRequested;
#pragma warning restore CS0067

    public ManageWorktreesViewModel(
        IGitWorktreeService gitWorktreeService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        MainViewModel mainViewModel)
    {
        _gitWorktreeService = gitWorktreeService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _mainViewModel = mainViewModel;
    }

    /// <summary>
    /// Opens the popup for the current project's repository.
    /// </summary>
    [RelayCommand]
    public async Task OpenAsync()
    {
        if (_mainViewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            StatusMessage = "Please select a project tab first.";
            IsOpen = true;
            return;
        }

        var workingDir = terminalTab.Pair.WorkingDirectory;
        // GetRepositoryRootAsync not in Core interface - use workingDir as fallback
        var repoRoot = workingDir;

        if (string.IsNullOrEmpty(repoRoot))
        {
            StatusMessage = "Not a git repository.";
            IsOpen = true;
            return;
        }

        RepoPath = repoRoot;
        RepoName = Path.GetFileName(repoRoot.TrimEnd(Path.DirectorySeparatorChar));
        StatusMessage = "";

        await RefreshAsync();
        IsOpen = true;
    }

    /// <summary>
    /// Opens the popup for a specific repository path.
    /// </summary>
    public async Task OpenForRepositoryAsync(string repoPath, string repoName)
    {
        RepoPath = repoPath;
        RepoName = repoName;
        StatusMessage = "";
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
        if (string.IsNullOrEmpty(RepoPath))
            return;

        IsLoading = true;
        try
        {
            var worktreeInfos = await _gitWorktreeService.ListWorktreesAsync(RepoPath);
            Worktrees.Clear();

            foreach (var info in worktreeInfos.OrderByDescending(w => w.IsMain).ThenBy(w => w.Branch))
            {
                Worktrees.Add(info);
            }

            StatusMessage = Worktrees.Count == 0 ? "No worktrees found." : "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading worktrees: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenWorktree(WorktreeInfo? worktree)
    {
        if (worktree == null) return;
        OpenWorktreeRequested?.Invoke(this, worktree.Path);
        Close();
    }

    [RelayCommand]
    private async Task RemoveWorktreeAsync(WorktreeInfo? worktree)
    {
        if (worktree == null || worktree.IsMain) return;

        var confirmed = _dialogService.ShowConfirmation(
            $"Remove worktree '{worktree.DisplayName}'?\n\nThis will delete the worktree directory:\n{worktree.Path}",
            "Remove Worktree");

        if (!confirmed) return;

        IsLoading = true;
        try
        {
            var result = await _gitWorktreeService.RemoveWorktreeAsync(worktree.Path, false);
            if (result.Success)
            {
                await RefreshAsync();
            }
            else
            {
                var forceConfirmed = _dialogService.ShowConfirmation(
                    $"Worktree may have uncommitted changes.\n\nForce remove anyway?\n\nError: {result.Error}",
                    "Force Remove Worktree");

                if (forceConfirmed)
                {
                    result = await _gitWorktreeService.RemoveWorktreeAsync(worktree.Path, true);
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
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleLockAsync(WorktreeInfo? worktree)
    {
        if (worktree == null || worktree.IsMain) return;

        IsLoading = true;
        try
        {
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

                if (reason == null) return; // Cancelled

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
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CopyPathAsync(WorktreeInfo? worktree)
    {
        if (worktree == null) return;

        await _clipboardService.SetTextAsync(worktree.Path);
    }

    [RelayCommand]
    private async Task NewWorktreeAsync()
    {
        if (string.IsNullOrEmpty(RepoPath)) return;

        // Get branches via the concrete Avalonia service (has GetBranchesForWorktreeAsync)
        if (_gitWorktreeService is not GitWorktreeService concreteService)
        {
            _dialogService.ShowWarning("Create Worktree feature is not available.", "Not Available");
            return;
        }

        var branches = await concreteService.GetBranchesForWorktreeAsync(RepoPath);

        var result = _dialogService.ShowCreateWorktreeDialog(
            RepoPath,
            branches,
            RepoPath);

        if (result == null) return;

        IsLoading = true;
        try
        {
            var createResult = await _gitWorktreeService.CreateWorktreeAsync(
                RepoPath,
                result.BranchName,
                result.WorktreePath,
                result.CreateNewBranch);

            if (createResult.Success)
            {
                await RefreshAsync();

                if (result.OpenAfterCreation)
                {
                    OpenWorktreeRequested?.Invoke(this, result.WorktreePath);
                    Close();
                }
            }
            else
            {
                _dialogService.ShowError($"Failed to create worktree: {createResult.Error}");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PruneAsync()
    {
        IsLoading = true;
        try
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
        finally
        {
            IsLoading = false;
        }
    }
}
