using System.Collections.ObjectModel;
using System.IO;
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
    private bool _isAutoSortEnabled;

    /// <summary>
    /// Icon for the sort toggle button.
    /// </summary>
    public string SortToggleIcon => IsAutoSortEnabled ? "⇅" : "↕";

    /// <summary>
    /// Tooltip for the sort toggle button.
    /// </summary>
    public string SortToggleToolTip => IsAutoSortEnabled
        ? "Auto-sorted by usage (click for manual)"
        : "Manual order (click for auto-sort)";

    /// <summary>
    /// Event raised when a workspace or worktree should be opened as a terminal tab.
    /// </summary>
    public event EventHandler<string>? OpenTabRequested;

    /// <summary>
    /// Event raised when workspace list changes and should be persisted.
    /// </summary>
    public event EventHandler? WorkspacesChanged;

    public WorkspaceSidebarViewModel(
        IConfigurationService configurationService,
        IGitWorktreeService gitWorktreeService,
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IStatisticsService statisticsService)
    {
        _configurationService = configurationService;
        _gitWorktreeService = gitWorktreeService;
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _statisticsService = statisticsService;
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

            Workspaces.Clear();
            Playgrounds.Clear();

            foreach (var workspace in config.Workspaces.OrderBy(w => w.Order))
            {
                var vm = CreateWorkspaceEntryViewModel(workspace);

                if (workspace.Section == "playground")
                    Playgrounds.Add(vm);
                else
                    Workspaces.Add(vm);

                // Load worktrees in background
                _ = vm.LoadAsync();
            }

            // Set auto-sort after workspaces are loaded so the change handler can sort them
            IsAutoSortEnabled = config.Settings.WorkspaceAutoSort;
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

        // Check if workspace already exists
        var existingWorkspaces = section == "playground" ? Playgrounds : Workspaces;
        var existing = existingWorkspaces.FirstOrDefault(w =>
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
            Order = existingWorkspaces.Count,
            IsExpanded = true
        };

        var vm = CreateWorkspaceEntryViewModel(workspace);

        if (section == "playground")
            Playgrounds.Add(vm);
        else
            Workspaces.Add(vm);

        await vm.LoadAsync();
        SaveWorkspaces();

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
        // Check if workspace exists in either section
        var exists = Workspaces.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                     Playgrounds.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            await AddWorkspaceAsync(path, "main");
        }
    }

    /// <summary>
    /// Updates activity state for a workspace based on terminal activity.
    /// </summary>
    public void UpdateActivity(string path, bool isActive, bool hasUnreadActivity)
    {
        var workspace = FindWorkspaceByPath(path);
        if (workspace != null)
        {
            workspace.IsActive = isActive;
            workspace.HasUnreadActivity = hasUnreadActivity;
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
    /// Refreshes git status for all workspaces.
    /// </summary>
    public async Task RefreshAllGitStatusAsync()
    {
        var tasks = Workspaces.Concat(Playgrounds)
            .Select(w => w.RefreshGitStatusAsync());
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
        return Workspaces.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)) ??
               Playgrounds.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
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

        // Add main workspaces
        var order = 0;
        foreach (var vm in Workspaces)
        {
            vm.Order = order++;
            config.Workspaces.Add(vm.Workspace);
        }

        // Add playgrounds
        foreach (var vm in Playgrounds)
        {
            vm.Order = order++;
            config.Workspaces.Add(vm.Workspace);
        }

        config.Settings.SidebarWidth = Width;
        config.Settings.SidebarCollapsed = IsCollapsed;

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

        var collection = workspace.Section == "playground" ? Playgrounds : Workspaces;
        var index = collection.IndexOf(workspace);
        if (index > 0)
        {
            collection.Move(index, index - 1);
            SaveWorkspaces();
        }
    }

    /// <summary>
    /// Moves a workspace down in the list.
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceDown(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var collection = workspace.Section == "playground" ? Playgrounds : Workspaces;
        var index = collection.IndexOf(workspace);
        if (index >= 0 && index < collection.Count - 1)
        {
            collection.Move(index, index + 1);
            SaveWorkspaces();
        }
    }

    /// <summary>
    /// Moves a workspace to the top of its section.
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceToTop(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var collection = workspace.Section == "playground" ? Playgrounds : Workspaces;
        var index = collection.IndexOf(workspace);
        if (index > 0)
        {
            collection.Move(index, 0);
            SaveWorkspaces();
        }
    }

    /// <summary>
    /// Moves a workspace to the bottom of its section.
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceToBottom(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var collection = workspace.Section == "playground" ? Playgrounds : Workspaces;
        var index = collection.IndexOf(workspace);
        if (index >= 0 && index < collection.Count - 1)
        {
            collection.Move(index, collection.Count - 1);
            SaveWorkspaces();
        }
    }

    /// <summary>
    /// Moves a workspace to the other section (main to playground or vice versa).
    /// </summary>
    [RelayCommand]
    private void MoveWorkspaceToOtherSection(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var fromCollection = workspace.Section == "playground" ? Playgrounds : Workspaces;
        var toCollection = workspace.Section == "playground" ? Workspaces : Playgrounds;
        var newSection = workspace.Section == "playground" ? "main" : "playground";

        fromCollection.Remove(workspace);
        workspace.Workspace.Section = newSection;
        toCollection.Add(workspace);
        SaveWorkspaces();
    }

    /// <summary>
    /// Creates a new worktree for the workspace.
    /// </summary>
    [RelayCommand]
    private async Task CreateWorktree(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        // Prompt for branch name
        var branchName = _dialogService.ShowInput(
            "Enter branch name for the new worktree:",
            "Create Worktree",
            "");

        if (string.IsNullOrWhiteSpace(branchName))
            return;

        // Determine worktree path (sibling directory with branch name)
        var parentDir = Path.GetDirectoryName(workspace.Path);
        if (string.IsNullOrEmpty(parentDir))
        {
            _dialogService.ShowError("Cannot determine parent directory.");
            return;
        }

        var worktreePath = Path.Combine(parentDir, $"{Path.GetFileName(workspace.Path)}.{branchName}");

        // Check if directory already exists
        if (_fileSystem.DirectoryExists(worktreePath))
        {
            _dialogService.ShowError($"Directory already exists: {worktreePath}");
            return;
        }

        var result = await _gitWorktreeService.CreateWorktreeAsync(workspace.Path, branchName, worktreePath, createBranch: true);
        if (result.Success)
        {
            // Refresh the workspace to show new worktree
            await workspace.LoadAsync();

            // Open the new worktree as a tab
            OpenTabRequested?.Invoke(this, worktreePath);
        }
        else
        {
            _dialogService.ShowError($"Failed to create worktree: {result.Error}");
        }
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

        var result = await _gitStatusService.FetchAllAsync(workspace.Path);
        if (result.Success)
        {
            await workspace.RefreshGitStatusAsync();
        }
        else
        {
            _dialogService.ShowWarning($"Git fetch failed:\n{result.Error}", "Git Fetch");
        }
    }

    /// <summary>
    /// Runs git pull --rebase for the workspace.
    /// </summary>
    [RelayCommand]
    private async Task GitPullRebase(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var result = await _gitStatusService.PullRebaseAsync(workspace.Path);
        if (result.Success)
        {
            await workspace.RefreshGitStatusAsync();
        }
        else
        {
            _dialogService.ShowWarning($"Git pull failed:\n{result.Error}", "Git Pull");
        }
    }

    /// <summary>
    /// Runs git push for the workspace.
    /// </summary>
    [RelayCommand]
    private async Task GitPush(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;

        var result = await _gitStatusService.PushAsync(workspace.Path);
        if (result.Success)
        {
            await workspace.RefreshGitStatusAsync();
        }
        else
        {
            _dialogService.ShowWarning($"Git push failed:\n{result.Error}", "Git Push");
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
    /// Toggles the auto-sort by usage feature.
    /// </summary>
    [RelayCommand]
    private void ToggleAutoSort()
    {
        IsAutoSortEnabled = !IsAutoSortEnabled;
    }

    partial void OnIsAutoSortEnabledChanged(bool value)
    {
        // Don't save during initial load
        if (!IsLoading)
        {
            var config = _configurationService.Load();
            config.Settings.WorkspaceAutoSort = value;
            _configurationService.Save(config);
        }

        // Apply appropriate sort
        if (value)
        {
            ApplyUsageSort();
        }
        else
        {
            ApplyManualSort();
        }
    }

    /// <summary>
    /// Sorts workspaces by usage score (focus time + char count).
    /// </summary>
    private void ApplyUsageSort()
    {
        // Sort main workspaces
        var sortedWorkspaces = Workspaces
            .OrderByDescending(w => CalculateUsageScore(w.Path))
            .ToList();

        Workspaces.Clear();
        foreach (var w in sortedWorkspaces)
            Workspaces.Add(w);

        // Sort playgrounds
        var sortedPlaygrounds = Playgrounds
            .OrderByDescending(w => CalculateUsageScore(w.Path))
            .ToList();

        Playgrounds.Clear();
        foreach (var w in sortedPlaygrounds)
            Playgrounds.Add(w);
    }

    /// <summary>
    /// Sorts workspaces by manual order.
    /// </summary>
    private void ApplyManualSort()
    {
        var sortedWorkspaces = Workspaces.OrderBy(w => w.Order).ToList();
        Workspaces.Clear();
        foreach (var w in sortedWorkspaces)
            Workspaces.Add(w);

        var sortedPlaygrounds = Playgrounds.OrderBy(w => w.Order).ToList();
        Playgrounds.Clear();
        foreach (var w in sortedPlaygrounds)
            Playgrounds.Add(w);
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
