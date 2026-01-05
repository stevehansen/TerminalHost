using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the workspace sidebar.
/// Shows recent workspaces, git worktrees, and open projects.
/// STUB: Many features disabled until Core interfaces are aligned.
/// </summary>
public partial class WorkspaceSidebarViewModel : ObservableObject
{
    private readonly IGitWorktreeService _worktreeService;
    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly IGitStatusService _gitStatusService;
    private readonly IProcessService _processService;

    // Stub workspace class for compilation
    public class WorkspaceItem
    {
        public string Path { get; set; } = "";
        public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        public string DisplayPath => Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            ? "~" + Path[Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Length..]
            : Path;
        public string GitBranch { get; set; } = "";
        public DateTime LastOpened { get; set; } = DateTime.UtcNow;
        public string RelativeLastOpened => GetRelativeTime(LastOpened);
        public bool IsPlayground { get; set; }
        public static WorkspaceItem FromPath(string path) => new() { Path = path };

        private static string GetRelativeTime(DateTime dt)
        {
            var span = DateTime.UtcNow - dt;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("MMM d");
        }
    }

    // Stub worktree class for compilation
    public class WorktreeItem
    {
        public string Path { get; set; } = "";
        public string Branch { get; set; } = "";
        public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
    }

    private List<WorkspaceItem> _allRecentWorkspaces = [];
    private List<WorktreeItem> _allWorktrees = [];

    /// <summary>
    /// Reference to MainViewModel - set after construction to avoid circular dependency.
    /// </summary>
    public MainViewModel? MainViewModel { get; set; }

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<WorkspaceItem> _recentWorkspaces = [];

    [ObservableProperty]
    private ObservableCollection<WorktreeItem> _worktrees = [];

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
        // Note: RecentWorkspaces is not persisted in Core config yet.
        // For now, use OpenFolders as a source of recent workspaces.
        var config = _configService.Load();
        _allRecentWorkspaces = config.OpenFolders
            .Select(path => WorkspaceItem.FromPath(path))
            .ToList();
        ApplyFilter();
    }

    /// <summary>
    /// Refreshes the worktrees list for the currently selected tab.
    /// </summary>
    public async Task RefreshWorktreesAsync()
    {
        // STUB: GetRepositoryRootAsync and GetWorktreesAsync are not in Core IGitWorktreeService
        _allWorktrees = [];
        Worktrees.Clear();
        HasWorktrees = false;
        CurrentRepositoryName = string.Empty;
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Applies the current filter to both recent workspaces and worktrees.
    /// </summary>
    public void ApplyFilter()
    {
        var filter = FilterText?.ToLowerInvariant() ?? "";

        // Filter recent workspaces
        var filteredRecent = string.IsNullOrEmpty(filter)
            ? _allRecentWorkspaces
            : _allRecentWorkspaces.Where(w =>
                w.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.GitBranch?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

        RecentWorkspaces = new ObservableCollection<WorkspaceItem>(filteredRecent);

        // Filter worktrees
        var filteredWorktrees = string.IsNullOrEmpty(filter)
            ? _allWorktrees
            : _allWorktrees.Where(w =>
                w.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Branch?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

        Worktrees = new ObservableCollection<WorktreeItem>(filteredWorktrees);
    }

    /// <summary>
    /// Adds or updates a workspace in the recent list.
    /// </summary>
    public async Task TrackWorkspaceOpenedAsync(string path)
    {
        // GetCurrentBranchAsync is not in Core IGitStatusService - use GetGitStatusAsync instead
        var status = await _gitStatusService.GetGitStatusAsync(path);
        var gitBranch = status?.BranchName ?? "";

        var workspace = _allRecentWorkspaces.FirstOrDefault(w =>
            w.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (workspace != null)
        {
            // Update existing
            workspace.LastOpened = DateTime.UtcNow;
            workspace.GitBranch = gitBranch;
        }
        else
        {
            // Add new
            workspace = WorkspaceItem.FromPath(path);
            workspace.GitBranch = gitBranch;
            _allRecentWorkspaces.Add(workspace);
        }

        // Trim to max count
        const int maxItems = 20;
        if (_allRecentWorkspaces.Count > maxItems)
        {
            _allRecentWorkspaces = _allRecentWorkspaces
                .OrderByDescending(w => w.LastOpened)
                .Take(maxItems)
                .ToList();
        }

        ApplyFilter();
    }

    /// <summary>
    /// Updates the git branch for a workspace.
    /// </summary>
    public void UpdateWorkspaceGitBranch(string path, string branch)
    {
        var workspace = _allRecentWorkspaces.FirstOrDefault(w =>
            w.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (workspace != null)
        {
            workspace.LastOpened = DateTime.UtcNow;
            workspace.GitBranch = branch ?? "";
        }
        else
        {
            workspace = WorkspaceItem.FromPath(path);
            workspace.GitBranch = branch ?? "";
            _allRecentWorkspaces.Add(workspace);
        }

        ApplyFilter();
    }

    // Commands - stubbed for compilation

    [RelayCommand]
    private void OpenWorkspace(WorkspaceItem workspace)
    {
        MainViewModel?.OpenProjectTab(workspace.Path);
    }

    [RelayCommand]
    private void OpenWorktree(WorktreeItem worktree)
    {
        MainViewModel?.OpenProjectTab(worktree.Path);
    }

    [RelayCommand]
    private async Task CreateWorktreeAsync()
    {
        // STUB: GetBranchesForWorktreeAsync and ShowCreateWorktreeDialogAsync not available
        _toastService.Show("Worktree creation not available in this version", ToastType.Warning);
    }

    [RelayCommand]
    private async Task RemoveWorktreeAsync(WorktreeItem worktree)
    {
        // STUB: Core IGitWorktreeService has different signature
        _toastService.Show("Worktree removal not available in this version", ToastType.Warning);
    }

    [RelayCommand]
    private async Task CreateBranchFromWorktreeAsync(WorktreeItem worktree)
    {
        // STUB: Core IGitWorktreeService has different signature
        _toastService.Show("Branch creation not available in this version", ToastType.Warning);
    }

    [RelayCommand]
    private void RemoveFromRecent(WorkspaceItem workspace)
    {
        _allRecentWorkspaces.Remove(workspace);
        ApplyFilter();
    }

    [RelayCommand]
    private void TogglePlayground(WorkspaceItem workspace)
    {
        workspace.IsPlayground = !workspace.IsPlayground;
    }

    #region Git Operations for Open Projects

    /// <summary>
    /// Exposes open projects from MainViewModel.
    /// </summary>
    public IReadOnlyList<ITabViewModel> OpenProjects =>
        MainViewModel?.Tabs
            .OfType<ITabViewModel>()
            .Where(t => t is TerminalPairTabViewModel)
            .ToList() ?? [];

    [RelayCommand]
    private void SelectOpenProject(ITabViewModel tab)
    {
        if (MainViewModel != null)
        {
            MainViewModel.SelectedTab = tab;
        }
    }

    [RelayCommand]
    private void CloseTab(ITabViewModel tab)
    {
        // MainViewModel.CloseTab may not be public - use RemoveTab instead
        if (MainViewModel != null)
        {
            MainViewModel.Tabs.Remove(tab);
        }
    }

    [RelayCommand]
    private async Task GitFetchAsync(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        var workingDir = terminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.FetchAllAsync(workingDir);
        if (!result.Success)
        {
            _toastService.Show($"Fetch failed: {result.Error}", ToastType.Error);
        }
        else
        {
            _toastService.Show("Fetched successfully", ToastType.Success);
        }
    }

    [RelayCommand]
    private async Task GitPullRebaseAsync(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        var workingDir = terminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.PullRebaseAsync(workingDir);
        if (!result.Success)
        {
            _toastService.Show($"Pull failed: {result.Error}", ToastType.Error);
        }
        else
        {
            _toastService.Show("Pulled successfully", ToastType.Success);
        }
    }

    [RelayCommand]
    private async Task GitPushAsync(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        var workingDir = terminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.PushAsync(workingDir);
        if (!result.Success)
        {
            _toastService.Show($"Push failed: {result.Error}", ToastType.Error);
        }
        else
        {
            _toastService.Show("Pushed successfully", ToastType.Success);
        }
    }

    [RelayCommand]
    private void OpenInFinder(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        var workingDir = terminalTab.Pair.WorkingDirectory;
        // Cast to local IProcessService which has OpenFolder
        if (_processService is Services.ProcessService ps)
        {
            ps.OpenFolder(workingDir);
        }
    }

    #endregion

    [RelayCommand]
    private void ManageWorktrees()
    {
        ManageWorktreesRequested?.Invoke(this, EventArgs.Empty);
    }
}
