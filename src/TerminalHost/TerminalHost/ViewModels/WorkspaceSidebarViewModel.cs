using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the workspace sidebar.
/// Shows recent workspaces, git worktrees, and open projects.
/// </summary>
public partial class WorkspaceSidebarViewModel : ObservableObject
{
    private readonly IGitWorktreeService _worktreeService;
    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly IGitStatusService _gitStatusService;
    private readonly IProcessService _processService;

    private List<Workspace> _allRecentWorkspaces = [];
    private List<WorktreeInfo> _allWorktrees = [];

    /// <summary>
    /// Reference to MainViewModel - set after construction to avoid circular dependency.
    /// </summary>
    public MainViewModel? MainViewModel { get; set; }

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Workspace> _recentWorkspaces = [];

    [ObservableProperty]
    private ObservableCollection<WorktreeInfo> _worktrees = [];

    [ObservableProperty]
    private bool _isRecentExpanded = true;

    [ObservableProperty]
    private bool _isWorktreesExpanded = true;

    [ObservableProperty]
    private bool _isOpenProjectsExpanded = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasWorktrees;

    [ObservableProperty]
    private string _currentRepositoryName = string.Empty;

    /// <summary>
    /// Event raised when user wants to open the Manage Worktrees popup.
    /// </summary>
    public event EventHandler? ManageWorktreesRequested;

    public WorkspaceSidebarViewModel(
        IGitWorktreeService worktreeService,
        IConfigurationService configService,
        IDialogService dialogService,
        IToastService toastService,
        IGitStatusService gitStatusService,
        IProcessService processService)
    {
        _worktreeService = worktreeService;
        _configService = configService;
        _dialogService = dialogService;
        _toastService = toastService;
        _gitStatusService = gitStatusService;
        _processService = processService;
    }

    /// <summary>
    /// Initializes the sidebar with data from configuration.
    /// </summary>
    public void Initialize()
    {
        var config = _configService.Load();
        _allRecentWorkspaces = config.Settings.RecentWorkspaces.ToList();
        ApplyFilter();
    }

    /// <summary>
    /// Refreshes the worktrees list for the currently selected tab.
    /// </summary>
    public async Task RefreshWorktreesAsync()
    {
        if (MainViewModel?.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            _allWorktrees = [];
            Worktrees.Clear();
            HasWorktrees = false;
            CurrentRepositoryName = string.Empty;
            return;
        }

        var workingDir = terminalTab.Pair.WorkingDirectory;
        var repoRoot = await _worktreeService.GetRepositoryRootAsync(workingDir);

        if (string.IsNullOrEmpty(repoRoot))
        {
            _allWorktrees = [];
            Worktrees.Clear();
            HasWorktrees = false;
            CurrentRepositoryName = string.Empty;
            return;
        }

        CurrentRepositoryName = System.IO.Path.GetFileName(repoRoot);

        IsLoading = true;
        try
        {
            _allWorktrees = await _worktreeService.GetWorktreesAsync(repoRoot);
            HasWorktrees = _allWorktrees.Count > 0;
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim().ToLowerInvariant();

        // Filter recent workspaces
        var filteredWorkspaces = string.IsNullOrEmpty(filter)
            ? _allRecentWorkspaces
            : _allRecentWorkspaces.Where(w =>
                w.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (w.GitBranch?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

        RecentWorkspaces = new ObservableCollection<Workspace>(
            filteredWorkspaces.OrderByDescending(w => w.LastOpened).Take(20));

        // Filter worktrees
        var filteredWorktrees = string.IsNullOrEmpty(filter)
            ? _allWorktrees
            : _allWorktrees.Where(w =>
                w.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Branch.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));

        Worktrees = new ObservableCollection<WorktreeInfo>(filteredWorktrees);
    }

    /// <summary>
    /// Adds or updates a workspace in the recent list.
    /// </summary>
    public void TrackWorkspace(string path, string? gitBranch = null)
    {
        var existing = _allRecentWorkspaces.FirstOrDefault(w =>
            w.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.LastOpened = DateTime.Now;
            if (gitBranch != null)
                existing.GitBranch = gitBranch;
        }
        else
        {
            var workspace = Workspace.FromPath(path);
            workspace.GitBranch = gitBranch;
            _allRecentWorkspaces.Add(workspace);
        }

        // Trim to max count
        var config = _configService.Load();
        var maxItems = config.Settings.MaxRecentWorkspaces;
        if (_allRecentWorkspaces.Count > maxItems)
        {
            _allRecentWorkspaces = _allRecentWorkspaces
                .OrderByDescending(w => w.LastOpened)
                .Take(maxItems)
                .ToList();
        }

        // Save to config
        config.Settings.RecentWorkspaces = _allRecentWorkspaces.ToList();
        _configService.Save(config);

        ApplyFilter();
    }

    [RelayCommand]
    private void OpenWorkspace(Workspace workspace)
    {
        if (MainViewModel == null) return;

        // Update last opened time
        workspace.LastOpened = DateTime.Now;
        TrackWorkspace(workspace.Path, workspace.GitBranch);

        // Open or focus the project tab
        MainViewModel.OpenProjectTab(workspace.Path);
    }

    [RelayCommand]
    private void OpenWorktree(WorktreeInfo worktree)
    {
        if (MainViewModel == null) return;

        // Track as workspace
        TrackWorkspace(worktree.Path, worktree.Branch);

        // Open or focus the project tab
        MainViewModel.OpenProjectTab(worktree.Path);
    }

    [RelayCommand]
    private void SelectOpenProject(ITabViewModel tab)
    {
        if (MainViewModel == null) return;
        MainViewModel.SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(ITabViewModel tab)
    {
        if (MainViewModel == null) return;
        MainViewModel.CloseTabCommand.Execute(tab);
    }

    [RelayCommand]
    private async Task ShowCreateWorktreeDialogAsync()
    {
        if (MainViewModel?.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            _toastService.Show("Please select a project tab first.", ToastType.Warning);
            return;
        }

        var workingDir = terminalTab.Pair.WorkingDirectory;
        var repoRoot = await _worktreeService.GetRepositoryRootAsync(workingDir);

        if (string.IsNullOrEmpty(repoRoot))
        {
            _toastService.Show("Not a git repository.", ToastType.Warning);
            return;
        }

        // Get branches for the dialog
        var branches = await _worktreeService.GetBranchesForWorktreeAsync(repoRoot);

        // Show the create worktree dialog
        var result = await _dialogService.ShowCreateWorktreeDialogAsync(
            repoRoot,
            branches,
            repoRoot);

        if (result == null) return;

        IsLoading = true;
        try
        {
            var (success, error) = await _worktreeService.CreateWorktreeAsync(
                repoRoot,
                result.WorktreePath,
                result.BranchName,
                result.CreateNewBranch);

            if (success)
            {
                _toastService.Show($"Worktree created: {result.BranchName}", ToastType.Success);
                await RefreshWorktreesAsync();

                if (result.OpenAfterCreation)
                {
                    // Open the worktree as a new tab
                    MainViewModel?.OpenProjectTab(result.WorktreePath);
                }
            }
            else
            {
                _toastService.Show(error ?? "Failed to create worktree.", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenManageWorktrees()
    {
        ManageWorktreesRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RemoveWorktreeAsync(WorktreeInfo worktree)
    {
        if (worktree.IsMain)
        {
            _toastService.Show("Cannot remove the main worktree.", ToastType.Warning);
            return;
        }

        var confirmed = _dialogService.ShowConfirmation(
            $"Remove worktree '{worktree.DisplayName}'?\n\nThis will delete the worktree directory. Uncommitted changes will be lost.",
            "Remove Worktree");

        if (!confirmed)
            return;

        if (MainViewModel?.SelectedTab is not TerminalPairTabViewModel terminalTab)
            return;

        var workingDir = terminalTab.Pair.WorkingDirectory;
        var repoRoot = await _worktreeService.GetRepositoryRootAsync(workingDir);

        if (string.IsNullOrEmpty(repoRoot))
            return;

        IsLoading = true;
        try
        {
            var (success, error) = await _worktreeService.RemoveWorktreeAsync(
                repoRoot,
                worktree.Path,
                force: false);

            if (success)
            {
                _toastService.Show($"Worktree removed: {worktree.DisplayName}", ToastType.Success);
                await RefreshWorktreesAsync();
            }
            else
            {
                // Try with force if there are uncommitted changes
                if (error?.Contains("contains modified") == true || error?.Contains("uncommitted") == true)
                {
                    var forceConfirmed = _dialogService.ShowConfirmation(
                        "Force remove the worktree? All changes will be lost.",
                        "Worktree has uncommitted changes");

                    if (forceConfirmed)
                    {
                        var (forceSuccess, forceError) = await _worktreeService.RemoveWorktreeAsync(
                            repoRoot,
                            worktree.Path,
                            force: true);

                        if (forceSuccess)
                        {
                            _toastService.Show($"Worktree removed: {worktree.DisplayName}", ToastType.Success);
                            await RefreshWorktreesAsync();
                        }
                        else
                        {
                            _toastService.Show(forceError ?? "Failed to remove worktree.", ToastType.Error);
                        }
                    }
                }
                else
                {
                    _toastService.Show(error ?? "Failed to remove worktree.", ToastType.Error);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateBranchFromWorktreeAsync(WorktreeInfo worktree)
    {
        if (worktree.IsDetached)
        {
            // Prompt for new branch name
            var branchName = _dialogService.ShowInput(
                "Enter a name for the new branch:",
                "Create Branch",
                "");

            if (string.IsNullOrWhiteSpace(branchName))
                return;

            if (MainViewModel?.SelectedTab is not TerminalPairTabViewModel terminalTab)
                return;

            var workingDir = terminalTab.Pair.WorkingDirectory;
            var repoRoot = await _worktreeService.GetRepositoryRootAsync(workingDir);

            if (string.IsNullOrEmpty(repoRoot))
                return;

            IsLoading = true;
            try
            {
                var result = await _worktreeService.CreateBranchFromWorktreeAsync(
                    repoRoot,
                    worktree.Path,
                    branchName);

                if (result.Success)
                {
                    _toastService.Show($"Branch created: {branchName}", ToastType.Success);
                    await RefreshWorktreesAsync();
                }
                else
                {
                    _toastService.Show(result.Error ?? "Failed to create branch.", ToastType.Error);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private void RemoveFromRecent(Workspace workspace)
    {
        _allRecentWorkspaces.Remove(workspace);

        // Save to config
        var config = _configService.Load();
        config.Settings.RecentWorkspaces = _allRecentWorkspaces.ToList();
        _configService.Save(config);

        ApplyFilter();
    }

    [RelayCommand]
    private void TogglePlayground(Workspace workspace)
    {
        workspace.IsPlayground = !workspace.IsPlayground;

        // Save to config
        var config = _configService.Load();
        config.Settings.RecentWorkspaces = _allRecentWorkspaces.ToList();
        _configService.Save(config);
    }

    #region Git Operations for Open Projects

    /// <summary>
    /// Gets the working directory from a tab if it's a terminal pair tab.
    /// </summary>
    private string? GetWorkingDirectory(ITabViewModel? tab)
    {
        return tab is TerminalPairTabViewModel terminalTab
            ? terminalTab.Pair.WorkingDirectory
            : null;
    }

    /// <summary>
    /// Runs git fetch for an open project tab.
    /// </summary>
    [RelayCommand]
    private async Task GitFetchAsync(ITabViewModel? tab)
    {
        var workingDir = GetWorkingDirectory(tab);
        if (string.IsNullOrEmpty(workingDir)) return;

        using var toast = _toastService.ShowProgress("Fetching from remotes...");
        var result = await _gitStatusService.FetchAllAsync(workingDir);
        if (result.Success)
        {
            toast.Complete("Fetch completed");
        }
        else
        {
            toast.Fail("Fetch failed");
            _dialogService.ShowWarning($"Git fetch failed:\n{result.Error}", "Git Fetch");
        }
    }

    /// <summary>
    /// Runs git pull --rebase for an open project tab.
    /// </summary>
    [RelayCommand]
    private async Task GitPullRebaseAsync(ITabViewModel? tab)
    {
        var workingDir = GetWorkingDirectory(tab);
        if (string.IsNullOrEmpty(workingDir)) return;

        using var toast = _toastService.ShowProgress("Pulling with rebase...");
        var result = await _gitStatusService.PullRebaseAsync(workingDir);
        if (result.Success)
        {
            toast.Complete("Pull completed");
        }
        else
        {
            toast.Fail("Pull failed");
            _dialogService.ShowWarning($"Git pull failed:\n{result.Error}", "Git Pull");
        }
    }

    /// <summary>
    /// Runs git push for an open project tab.
    /// </summary>
    [RelayCommand]
    private async Task GitPushAsync(ITabViewModel? tab)
    {
        var workingDir = GetWorkingDirectory(tab);
        if (string.IsNullOrEmpty(workingDir)) return;

        using var toast = _toastService.ShowProgress("Pushing to remote...");
        var result = await _gitStatusService.PushAsync(workingDir);
        if (result.Success)
        {
            toast.Complete("Push completed");
        }
        else
        {
            toast.Fail("Push failed");
            _dialogService.ShowWarning($"Git push failed:\n{result.Error}", "Git Push");
        }
    }

    /// <summary>
    /// Opens the project folder in Finder.
    /// </summary>
    [RelayCommand]
    private void OpenInFinder(ITabViewModel? tab)
    {
        var workingDir = GetWorkingDirectory(tab);
        if (string.IsNullOrEmpty(workingDir)) return;

        try
        {
            _processService.Start(new System.Diagnostics.ProcessStartInfo("open", workingDir)
            {
                UseShellExecute = false
            });
        }
        catch
        {
            // Silently ignore if Finder fails to open
        }
    }

    #endregion
}
