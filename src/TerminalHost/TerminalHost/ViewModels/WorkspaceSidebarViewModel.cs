using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the workspace sidebar, managing the list of workspaces and worktrees.
/// </summary>
public partial class WorkspaceSidebarViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IGitWorktreeService _gitWorktreeService;
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IStatisticsService _statisticsService;
    private readonly IToastService _toastService;

    // Internal unfiltered collections
    private List<WorkspaceEntryViewModel> _allWorkspaces = [];
    private List<WorkspaceEntryViewModel> _allPlaygrounds = [];

    // Filtered/sorted collections for display
    [ObservableProperty]
    private ObservableCollection<WorkspaceEntryViewModel> _workspaces = [];

    [ObservableProperty]
    private ObservableCollection<WorkspaceEntryViewModel> _playgrounds = [];

    [ObservableProperty]
    private WorkspaceEntryViewModel? _selectedWorkspace;

    [ObservableProperty]
    private WorktreeEntryViewModel? _selectedWorktree;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _width = 250;

    [ObservableProperty]
    private bool _isCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortToggleIcon))]
    [NotifyPropertyChangedFor(nameof(SortToggleToolTip))]
    private WorkspaceSortMode _sortMode = WorkspaceSortMode.Manual;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilterText))]
    private string _filterText = "";

    [ObservableProperty]
    private bool _isPullAllInProgress;

    /// <summary>
    /// Whether there is filter text entered.
    /// </summary>
    public bool HasFilterText => !string.IsNullOrEmpty(FilterText);

    /// <summary>
    /// Icon for the sort toggle button.
    /// </summary>
    public string SortToggleIcon => SortMode switch
    {
        WorkspaceSortMode.Manual => "↕",
        WorkspaceSortMode.Usage => "⇅",
        WorkspaceSortMode.Alphabetical => "AZ",
        _ => "↕"
    };

    /// <summary>
    /// Tooltip for the sort toggle button.
    /// </summary>
    public string SortToggleToolTip => SortMode switch
    {
        WorkspaceSortMode.Manual => "Manual order (click to sort by usage)",
        WorkspaceSortMode.Usage => "Sorted by usage (click to sort alphabetically)",
        WorkspaceSortMode.Alphabetical => "Sorted alphabetically (click for manual order)",
        _ => "Click to change sort mode"
    };

    /// <summary>
    /// Event raised when a workspace or worktree should be opened as a terminal tab.
    /// </summary>
    public event EventHandler<string>? OpenTabRequested;

    /// <summary>
    /// Event raised when workspace list changes and should be persisted.
    /// </summary>
    public event EventHandler? WorkspacesChanged;

    /// <summary>
    /// Event raised when git status is refreshed for a workspace (after git operations).
    /// Used to sync tab git status with sidebar.
    /// </summary>
    public event EventHandler<string>? GitStatusRefreshed;

    public WorkspaceSidebarViewModel(
        IConfigurationService configurationService,
        IGitWorktreeService gitWorktreeService,
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IStatisticsService statisticsService,
        IToastService toastService)
    {
        _configurationService = configurationService;
        _gitWorktreeService = gitWorktreeService;
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _statisticsService = statisticsService;
        _toastService = toastService;
    }

    /// <summary>
    /// Loads workspaces from configuration.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var config = _configurationService.Load();
            Width = config.Settings.SidebarWidth;
            IsCollapsed = config.Settings.SidebarCollapsed;
            FilterText = config.Settings.WorkspaceFilterText;

            _allWorkspaces.Clear();
            _allPlaygrounds.Clear();
            Workspaces.Clear();
            Playgrounds.Clear();

            foreach (var workspace in config.Workspaces.OrderBy(w => w.Order))
            {
                var vm = CreateWorkspaceEntryViewModel(workspace);

                if (workspace.Section == "playground")
                    _allPlaygrounds.Add(vm);
                else
                    _allWorkspaces.Add(vm);

                // Load worktrees in background
                _ = vm.LoadAsync();
            }

            // Set sort mode after workspaces are loaded so the change handler can sort them
            SortMode = config.Settings.WorkspaceSortMode;

            // Apply filter and sort
            ApplyFilterAndSort();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Adds a workspace for the given directory path.
    /// </summary>
    public async Task<WorkspaceEntryViewModel?> AddWorkspaceAsync(string path, string section = "main")
    {
        if (!_fileSystem.DirectoryExists(path))
            return null;

        // Check if workspace already exists in internal collections
        var existingInternal = section == "playground" ? _allPlaygrounds : _allWorkspaces;
        var existing = existingInternal.FirstOrDefault(w =>
            string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return existing;

        // Create new workspace
        var workspace = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = Path.GetFileName(path) ?? path,
            Path = path,
            Section = section,
            Order = existingInternal.Count,
            IsExpanded = true
        };

        var vm = CreateWorkspaceEntryViewModel(workspace);

        if (section == "playground")
            _allPlaygrounds.Add(vm);
        else
            _allWorkspaces.Add(vm);

        await vm.LoadAsync();
        SaveWorkspaces();
        ApplyFilterAndSort();

        return vm;
    }

    /// <summary>
    /// Adds multiple workspaces at once.
    /// </summary>
    /// <returns>Tuple with count of added workspaces and list of skipped folder names.</returns>
    public async Task<(int added, List<string> skipped)> AddWorkspacesAsync(IEnumerable<string> paths, string section = "main")
    {
        int added = 0;
        var skipped = new List<string>();

        foreach (var path in paths)
        {
            var workspace = await AddWorkspaceAsync(path, section);
            if (workspace != null)
            {
                added++;
            }
            else
            {
                skipped.Add(Path.GetFileName(path) ?? path);
            }
        }

        return (added, skipped);
    }

    /// <summary>
    /// Removes a workspace from the sidebar.
    /// </summary>
    public void RemoveWorkspace(WorkspaceEntryViewModel workspace)
    {
        // Remove from internal collections
        if (workspace.Section == "playground")
            _allPlaygrounds.Remove(workspace);
        else
            _allWorkspaces.Remove(workspace);

        // Remove from display collections
        if (workspace.Section == "playground")
            Playgrounds.Remove(workspace);
        else
            Workspaces.Remove(workspace);

        SaveWorkspaces();
    }

    /// <summary>
    /// Syncs the workspace list with open tabs.
    /// When a tab is opened, ensure corresponding workspace exists.
    /// </summary>
    public async Task SyncWithOpenTabAsync(string path)
    {
        // Check if workspace exists in internal collections
        var exists = _allWorkspaces.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                     _allPlaygrounds.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            // Check if this path is a worktree of an existing workspace
            var isWorktree = await _gitWorktreeService.IsWorktreeAsync(path);
            if (isWorktree)
            {
                // Get the main worktree path and check if we already have it as a workspace
                var mainPath = await _gitWorktreeService.GetMainWorktreePathAsync(path);
                if (mainPath != null)
                {
                    var mainWorkspace = FindWorkspaceByPath(mainPath);
                    if (mainWorkspace != null)
                    {
                        // Refresh the main workspace to ensure worktrees list is up to date
                        await mainWorkspace.LoadAsync();
                        return; // Don't add worktree as separate workspace
                    }
                }
            }

            await AddWorkspaceAsync(path, "main");
        }
    }

    /// <summary>
    /// Updates activity state for a workspace based on terminal activity.
    /// </summary>
    public void UpdateActivity(string path, bool isActive, bool hasUnreadActivity, bool isWaitingForInput = false)
    {
        var workspace = FindWorkspaceByPath(path);
        if (workspace != null)
        {
            workspace.IsActive = isActive;
            workspace.HasUnreadActivity = hasUnreadActivity;
            workspace.IsWaitingForInput = isWaitingForInput;
        }
    }

    /// <summary>
    /// Clears unread activity for a workspace when its tab is selected.
    /// </summary>
    public void ClearUnreadActivity(string path)
    {
        var workspace = FindWorkspaceByPath(path);
        if (workspace != null)
        {
            workspace.HasUnreadActivity = false;
        }
    }

    /// <summary>
    /// Updates IsCurrentTab for all workspaces based on the selected tab's working directory.
    /// </summary>
    public void UpdateCurrentTab(string? currentPath)
    {
        foreach (var workspace in _allWorkspaces.Concat(_allPlaygrounds))
        {
            workspace.IsCurrentTab = !string.IsNullOrEmpty(currentPath) &&
                string.Equals(workspace.Path, currentPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Refreshes git status for all workspaces.
    /// </summary>
    public async Task RefreshAllGitStatusAsync()
    {
        var tasks = _allWorkspaces.Concat(_allPlaygrounds)
            .Select(w => w.RefreshGitStatusAsync());
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Fetches from git remotes for all workspaces and refreshes their status.
    /// This is called by the auto-fetch timer to keep behind counts up to date.
    /// </summary>
    public async Task FetchAllAsync()
    {
        var tasks = _allWorkspaces.Concat(_allPlaygrounds)
            .Select(async w =>
            {
                try
                {
                    await _gitStatusService.FetchAllAsync(w.Path);
                    await w.RefreshGitStatusAsync();
                }
                catch
                {
                    // Silently ignore fetch errors (network issues, etc.)
                }
            });
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Refreshes git status for a specific workspace.
    /// </summary>
    public async Task RefreshGitStatusAsync(string path)
    {
        var workspace = FindWorkspaceByPath(path);
        if (workspace != null)
        {
            await workspace.RefreshGitStatusAsync();
        }
    }

    private WorkspaceEntryViewModel? FindWorkspaceByPath(string path)
    {
        return _allWorkspaces.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)) ??
               _allPlaygrounds.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private WorkspaceEntryViewModel CreateWorkspaceEntryViewModel(Workspace workspace)
    {
        var vm = new WorkspaceEntryViewModel(workspace, _gitWorktreeService, _gitStatusService);
        vm.OpenRequested += OnWorkspaceOpenRequested;
        vm.WorktreeOpenRequested += OnWorktreeOpenRequested;
        return vm;
    }

    private void OnWorkspaceOpenRequested(object? sender, string path)
    {
        OpenTabRequested?.Invoke(this, path);
    }

    private void OnWorktreeOpenRequested(object? sender, string path)
    {
        OpenTabRequested?.Invoke(this, path);
    }

    private void SaveWorkspaces()
    {
        var config = _configurationService.Load();
        config.Workspaces.Clear();

        // Add main workspaces from internal collection
        var order = 0;
        foreach (var vm in _allWorkspaces)
        {
            vm.Order = order++;
            config.Workspaces.Add(vm.Workspace);
        }

        // Add playgrounds from internal collection
        foreach (var vm in _allPlaygrounds)
        {
            vm.Order = order++;
            config.Workspaces.Add(vm.Workspace);
        }

        config.Settings.SidebarWidth = Width;
        config.Settings.SidebarCollapsed = IsCollapsed;
        config.Settings.WorkspaceSortMode = SortMode;
        config.Settings.WorkspaceFilterText = FilterText;

        _configurationService.Save(config);
        WorkspacesChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnWidthChanged(double value)
    {
        // Debounce saving
        SaveWorkspaces();
    }

    partial void OnIsCollapsedChanged(bool value)
    {
        SaveWorkspaces();
    }

    [RelayCommand]
    private async Task AddWorkspace()
    {
        // This will be connected to a folder picker in the view
        // For now, just raise an event that MainViewModel can handle
    }

    [RelayCommand]
    private void RemoveSelectedWorkspace()
    {
        if (SelectedWorkspace != null)
        {
            RemoveWorkspace(SelectedWorkspace);
            SelectedWorkspace = null;
        }
    }

    [RelayCommand]
    private void RemoveWorkspaceFromSidebar(WorkspaceEntryViewModel? workspace)
    {
        if (workspace != null)
        {
            RemoveWorkspace(workspace);
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }

    /// <summary>
    /// Moves a workspace up in the list.
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceUp(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var collection = workspace.Section == "playground" ? _allPlaygrounds : _allWorkspaces;
        var index = collection.IndexOf(workspace);
        if (index > 0)
        {
            collection.RemoveAt(index);
            collection.Insert(index - 1, workspace);
            SaveWorkspaces();
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// Moves a workspace down in the list.
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceDown(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var collection = workspace.Section == "playground" ? _allPlaygrounds : _allWorkspaces;
        var index = collection.IndexOf(workspace);
        if (index >= 0 && index < collection.Count - 1)
        {
            collection.RemoveAt(index);
            collection.Insert(index + 1, workspace);
            SaveWorkspaces();
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// Moves a workspace to the top of its section.
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceToTop(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var collection = workspace.Section == "playground" ? _allPlaygrounds : _allWorkspaces;
        var index = collection.IndexOf(workspace);
        if (index > 0)
        {
            collection.RemoveAt(index);
            collection.Insert(0, workspace);
            SaveWorkspaces();
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// Moves a workspace to the bottom of its section.
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceToBottom(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var collection = workspace.Section == "playground" ? _allPlaygrounds : _allWorkspaces;
        var index = collection.IndexOf(workspace);
        if (index >= 0 && index < collection.Count - 1)
        {
            collection.RemoveAt(index);
            collection.Add(workspace);
            SaveWorkspaces();
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// Moves a workspace to the other section (main to playground or vice versa).
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceToOtherSection(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var fromCollection = workspace.Section == "playground" ? _allPlaygrounds : _allWorkspaces;
        var toCollection = workspace.Section == "playground" ? _allWorkspaces : _allPlaygrounds;
        var newSection = workspace.Section == "playground" ? "main" : "playground";

        fromCollection.Remove(workspace);
        workspace.Workspace.Section = newSection;
        toCollection.Add(workspace);
        SaveWorkspaces();
        ApplyFilterAndSort();
    }

    /// <summary>
    /// Creates a new worktree for the workspace.
    /// </summary>
    [RelayCommand]
    private async Task CreateWorktree(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        await CreateWorktreeForPathAsync(workspace.Path, workspace);
    }

    /// <summary>
    /// Helper to create a worktree, used by both CreateWorktree command and ManageWorktrees dialog.
    /// </summary>
    private async Task CreateWorktreeForPathAsync(string repoPath, WorkspaceEntryViewModel? workspace)
    {
        // Determine base path for suggestions
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            _dialogService.ShowError("Cannot determine parent directory.");
            return;
        }

        // Load branches for the dialog
        var branches = await _gitStatusService.GetBranchesAsync(repoPath);

        // Show the Create Worktree dialog
        var dialogResult = _dialogService.ShowCreateWorktreeDialog(
            repoPath,
            branches,
            repoPath);

        if (dialogResult == null)
            return;

        // Check if directory already exists (double-check, dialog also validates)
        if (_fileSystem.DirectoryExists(dialogResult.WorktreePath))
        {
            _dialogService.ShowError($"Directory already exists: {dialogResult.WorktreePath}");
            return;
        }

        var result = await _gitWorktreeService.CreateWorktreeAsync(
            repoPath,
            dialogResult.BranchName,
            dialogResult.WorktreePath,
            createBranch: dialogResult.CreateNewBranch);

        if (result.Success)
        {
            // Refresh the workspace to show new worktree
            if (workspace != null)
            {
                await workspace.LoadAsync();
            }

            // Open the new worktree as a tab if requested
            if (dialogResult.OpenAfterCreation)
            {
                OpenTabRequested?.Invoke(this, dialogResult.WorktreePath);
            }
        }
        else
        {
            _dialogService.ShowError($"Failed to create worktree: {result.Error}");
        }
    }

    /// <summary>
    /// Event raised when user wants to open the Manage Worktrees popup.
    /// </summary>
    public event EventHandler<(string RepoPath, string RepoName)>? ManageWorktreesRequested;

    /// <summary>
    /// Opens the Manage Worktrees popup for a workspace.
    /// </summary>
    [RelayCommand]
    private void ManageWorktrees(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        ManageWorktreesRequested?.Invoke(this, (workspace.Path, workspace.Name));
    }

    /// <summary>
    /// Removes a worktree.
    /// </summary>
    [RelayCommand]
    private async Task RemoveWorktree(WorktreeEntryViewModel? worktree)
    {
        if (worktree == null) return;

        var confirmed = _dialogService.ShowConfirmation(
            $"Remove worktree '{worktree.DisplayName}'?\n\nThis will delete the worktree directory:\n{worktree.Path}",
            "Remove Worktree");

        if (!confirmed)
            return;

        var result = await _gitWorktreeService.RemoveWorktreeAsync(worktree.Path, force: false);
        if (result.Success)
        {
            // Refresh all workspaces to update worktree lists
            await RefreshAllGitStatusAsync();
            await LoadAsync();
        }
        else
        {
            // Try with force if there are uncommitted changes
            var forceConfirmed = _dialogService.ShowConfirmation(
                $"Worktree has uncommitted changes.\n\nForce remove anyway?\n\nError: {result.Error}",
                "Force Remove Worktree");

            if (forceConfirmed)
            {
                result = await _gitWorktreeService.RemoveWorktreeAsync(worktree.Path, force: true);
                if (result.Success)
                {
                    await LoadAsync();
                }
                else
                {
                    _dialogService.ShowError($"Failed to remove worktree: {result.Error}");
                }
            }
        }
    }

    /// <summary>
    /// Event raised when a workspace should be duplicated (new tab for same directory).
    /// </summary>
    public event EventHandler<string>? DuplicateTabRequested;

    /// <summary>
    /// Event raised when a workspace tab should be closed.
    /// </summary>
    public event EventHandler<string>? CloseTabRequested;

    /// <summary>
    /// Duplicates a workspace (opens new tab for the same directory).
    /// </summary>
    [RelayCommand]
    private void DuplicateWorkspace(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        DuplicateTabRequested?.Invoke(this, workspace.Path);
    }

    /// <summary>
    /// Closes a workspace - closes the tab if open and removes from sidebar.
    /// </summary>
    [RelayCommand]
    private void CloseWorkspace(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        // Close the tab first (if open)
        CloseTabRequested?.Invoke(this, workspace.Path);

        // Then remove from sidebar
        RemoveWorkspace(workspace);
    }

    /// <summary>
    /// Runs git fetch for the workspace.
    /// </summary>
    [RelayCommand]
    private async Task GitFetch(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        using var toast = _toastService.ShowProgress($"Fetching {workspace.Name}...");
        var result = await _gitStatusService.FetchAllAsync(workspace.Path);
        if (result.Success)
        {
            await workspace.RefreshGitStatusAsync();
            GitStatusRefreshed?.Invoke(this, workspace.Path);
            toast.Complete("Fetch complete");
        }
        else
        {
            toast.Fail($"Fetch failed: {result.Error}");
        }
    }

    /// <summary>
    /// Runs git pull --rebase for the workspace.
    /// </summary>
    [RelayCommand]
    private async Task GitPullRebase(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        using var toast = _toastService.ShowProgress($"Pulling {workspace.Name}...");
        var result = await _gitStatusService.PullRebaseAsync(workspace.Path);
        if (result.Success)
        {
            await workspace.RefreshGitStatusAsync();
            GitStatusRefreshed?.Invoke(this, workspace.Path);
            toast.Complete("Pull complete");
        }
        else
        {
            toast.Fail($"Pull failed: {result.Error}");
        }
    }

    /// <summary>
    /// Runs git push for the workspace.
    /// </summary>
    [RelayCommand]
    private async Task GitPush(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        using var toast = _toastService.ShowProgress($"Pushing {workspace.Name}...");
        var result = await _gitStatusService.PushAsync(workspace.Path);
        if (result.Success)
        {
            await workspace.RefreshGitStatusAsync();
            GitStatusRefreshed?.Invoke(this, workspace.Path);
            toast.Complete("Push complete");
        }
        else
        {
            toast.Fail($"Push failed: {result.Error}");
        }
    }

    /// <summary>
    /// Opens the workspace folder in Windows Explorer.
    /// </summary>
    [RelayCommand]
    private void OpenInExplorer(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", workspace.Path);
        }
        catch
        {
            // Silently ignore if explorer fails to open
        }
    }

    /// <summary>
    /// Cycles through sort modes: Manual -> Usage -> Alphabetical -> Manual.
    /// </summary>
    [RelayCommand]
    private void CycleSortMode()
    {
        SortMode = SortMode switch
        {
            WorkspaceSortMode.Manual => WorkspaceSortMode.Usage,
            WorkspaceSortMode.Usage => WorkspaceSortMode.Alphabetical,
            WorkspaceSortMode.Alphabetical => WorkspaceSortMode.Manual,
            _ => WorkspaceSortMode.Manual
        };
    }

    /// <summary>
    /// Clears the filter text.
    /// </summary>
    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = "";
    }

    /// <summary>
    /// Toggles pin status for a workspace.
    /// </summary>
    [RelayCommand]
    private void TogglePin(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        workspace.IsPinned = !workspace.IsPinned;
        SaveWorkspaces();
        ApplyFilterAndSort();
    }

    /// <summary>
    /// Pulls all repositories with git pull --rebase.
    /// </summary>
    [RelayCommand]
    private async Task PullAll()
    {
        if (IsPullAllInProgress) return;

        IsPullAllInProgress = true;
        var allWorkspaces = _allWorkspaces.Concat(_allPlaygrounds).ToList();

        using var toast = _toastService.ShowProgress($"Pulling {allWorkspaces.Count} repos...");

        var successCount = 0;
        var failCount = 0;

        var tasks = allWorkspaces.Select(async w =>
        {
            try
            {
                var result = await _gitStatusService.PullRebaseAsync(w.Path);
                if (result.Success)
                {
                    await w.RefreshGitStatusAsync();
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    Interlocked.Increment(ref failCount);
                }
            }
            catch
            {
                Interlocked.Increment(ref failCount);
            }
        });

        await Task.WhenAll(tasks);

        IsPullAllInProgress = false;

        if (failCount == 0)
            toast.Complete($"Pulled {successCount} repos");
        else
            toast.Fail($"Pulled {successCount}, failed {failCount}");
    }

    /// <summary>
    /// Closes the tab for a workspace but keeps the workspace in the sidebar.
    /// </summary>
    [RelayCommand]
    private void CloseTabOnly(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        // Clear activity indicators
        workspace.HasUnreadActivity = false;
        workspace.IsActive = false;
        workspace.IsWaitingForInput = false;

        // Close the tab (but don't remove from sidebar)
        CloseTabRequested?.Invoke(this, workspace.Path);
    }

    /// <summary>
    /// Clears all activity indicators (green dots) across all workspaces.
    /// </summary>
    [RelayCommand]
    private void ClearAllIndicators()
    {
        foreach (var workspace in _allWorkspaces.Concat(_allPlaygrounds))
        {
            workspace.HasUnreadActivity = false;
        }
    }

    partial void OnSortModeChanged(WorkspaceSortMode value)
    {
        // Don't save during initial load
        if (!IsLoading)
        {
            var config = _configurationService.Load();
            config.Settings.WorkspaceSortMode = value;
            _configurationService.Save(config);
        }

        ApplyFilterAndSort();
    }

    partial void OnFilterTextChanged(string value)
    {
        // Don't save during initial load
        if (!IsLoading)
        {
            var config = _configurationService.Load();
            config.Settings.WorkspaceFilterText = value;
            _configurationService.Save(config);
        }

        ApplyFilterAndSort();
    }

    /// <summary>
    /// Applies both filtering and sorting to workspaces.
    /// </summary>
    private void ApplyFilterAndSort()
    {
        // Filter workspaces
        var filteredWorkspaces = FilterWorkspaces(_allWorkspaces);
        var filteredPlaygrounds = FilterWorkspaces(_allPlaygrounds);

        // Sort with pinned items first
        var sortedWorkspaces = ApplySortWithPinned(filteredWorkspaces);
        var sortedPlaygrounds = ApplySortWithPinned(filteredPlaygrounds);

        // Update display collections
        Workspaces.Clear();
        foreach (var w in sortedWorkspaces)
            Workspaces.Add(w);

        Playgrounds.Clear();
        foreach (var w in sortedPlaygrounds)
            Playgrounds.Add(w);
    }

    /// <summary>
    /// Filters workspaces by name or branch name (case-insensitive).
    /// </summary>
    private List<WorkspaceEntryViewModel> FilterWorkspaces(List<WorkspaceEntryViewModel> workspaces)
    {
        if (string.IsNullOrWhiteSpace(FilterText))
            return workspaces.ToList();

        var filter = FilterText.Trim();
        return workspaces.Where(w =>
            w.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (w.CurrentBranch?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    /// <summary>
    /// Sorts workspaces with pinned items first, then applies current sort mode.
    /// </summary>
    private List<WorkspaceEntryViewModel> ApplySortWithPinned(List<WorkspaceEntryViewModel> workspaces)
    {
        var pinned = workspaces.Where(w => w.IsPinned);
        var unpinned = workspaces.Where(w => !w.IsPinned);

        var sortedPinned = ApplyCurrentSort(pinned);
        var sortedUnpinned = ApplyCurrentSort(unpinned);

        return sortedPinned.Concat(sortedUnpinned).ToList();
    }

    /// <summary>
    /// Applies the current sort mode to a collection of workspaces.
    /// </summary>
    private IEnumerable<WorkspaceEntryViewModel> ApplyCurrentSort(IEnumerable<WorkspaceEntryViewModel> workspaces)
    {
        return SortMode switch
        {
            WorkspaceSortMode.Usage => workspaces.OrderByDescending(w => CalculateUsageScore(w.Path)),
            WorkspaceSortMode.Alphabetical => workspaces.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase),
            _ => workspaces.OrderBy(w => w.Order) // Manual
        };
    }

    /// <summary>
    /// Calculates a usage score for a workspace based on recent focus time and output activity.
    /// </summary>
    private double CalculateUsageScore(string path)
    {
        const int DAYS = 7;
        const double FOCUS_WEIGHT = 0.6;
        const double CHAR_WEIGHT = 0.4;

        var focusSeconds = _statisticsService.GetFocusTimeForPeriod(path, DAYS);
        var charCount = _statisticsService.GetCharCountForPeriod(path, DAYS);

        // Normalize: convert to minutes for focus, thousands for chars
        var focusMinutes = focusSeconds / 60.0;
        var charThousands = charCount / 1000.0;

        return (focusMinutes * FOCUS_WEIGHT) + (charThousands * CHAR_WEIGHT);
    }
}
