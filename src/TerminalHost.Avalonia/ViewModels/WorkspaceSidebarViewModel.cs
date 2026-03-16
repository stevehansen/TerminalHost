using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for a workspace (project) entry in the workspace sidebar.
/// Ported from WPF WorkspaceEntryViewModel with full git status and activity tracking.
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
    private bool _showStashCount = true;

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
    /// Shortened path for display (uses ~ for home directory on macOS).
    /// </summary>
    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.StartsWith(home) ? "~" + Path[home.Length..] : Path;
        }
    }

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
    /// Whether the workspace is pinned to the top of its section.
    /// </summary>
    public bool IsPinned
    {
        get => _workspace.IsPinned;
        set
        {
            if (_workspace.IsPinned != value)
            {
                _workspace.IsPinned = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PinToggleHeader));
            }
        }
    }

    /// <summary>
    /// Text for context menu to toggle pin status.
    /// </summary>
    public string PinToggleHeader => IsPinned ? "Unpin" : "Pin to Top";

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
    /// Display string for the branch (short name with fallback to full name).
    /// </summary>
    public string? BranchDisplay => BranchNameShort ?? CurrentBranch;

    /// <summary>
    /// Whether the repository has uncommitted changes.
    /// </summary>
    public bool IsDirty => GitStatus?.IsDirty ?? false;

    /// <summary>
    /// Number of commits ahead of remote.
    /// </summary>
    public int AheadCount => GitStatus?.AheadCount ?? 0;

    /// <summary>
    /// Number of commits behind remote.
    /// </summary>
    public int BehindCount => GitStatus?.BehindCount ?? 0;

    /// <summary>
    /// Whether there are commits that can be pushed.
    /// </summary>
    public bool CanPush => AheadCount > 0;

    /// <summary>
    /// Header text for Git Push menu item, includes count when available.
    /// </summary>
    public string GitPushHeader => AheadCount > 0 ? $"Git Push (↑{AheadCount})" : "Git Push";

    /// <summary>
    /// Number of stashed change sets (returns 0 when ShowStashCount is disabled).
    /// </summary>
    public int StashCount => ShowStashCount ? (GitStatus?.StashCount ?? 0) : 0;

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

    /// <summary>
    /// Status display string combining dirty state and ahead/behind.
    /// </summary>
    public string? StatusDisplay
    {
        get
        {
            var parts = new List<string>();
            if (IsDirty) parts.Add("*");
            if (AheadBehindDisplay != null) parts.Add(AheadBehindDisplay);
            if (StashCount > 0) parts.Add($"📦{StashCount}");
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
        OnPropertyChanged(nameof(BranchDisplay));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(AheadCount));
        OnPropertyChanged(nameof(BehindCount));
        OnPropertyChanged(nameof(CanPush));
        OnPropertyChanged(nameof(GitPushHeader));
        OnPropertyChanged(nameof(AheadBehindDisplay));
        OnPropertyChanged(nameof(StashCount));
        OnPropertyChanged(nameof(StatusDisplay));
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
            // Run git commands on the thread pool so Process.Start() doesn't block the UI thread.
            var path = Path;
            var (status, worktreeInfos) = await Task.Run(async () =>
            {
                var s = await _gitStatusService.GetGitStatusAsync(path);
                var w = await _gitWorktreeService.ListWorktreesAsync(path);
                return (s, w);
            });

            // Update UI-bound properties on the calling (UI) thread
            GitStatus = status;
            Worktrees.Clear();
            foreach (var info in worktreeInfos.Where(w => !w.IsMain))
            {
                Worktrees.Add(new WorktreeEntryViewModel(info));
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

/// <summary>
/// ViewModel for a git worktree entry in the workspace sidebar.
/// Ported from WPF WorktreeEntryViewModel.
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

/// <summary>
/// ViewModel for the workspace sidebar.
/// Shows workspaces with git status, worktrees, and open projects.
/// Ported from WPF WorkspaceSidebarViewModel with full git status, activity tracking, and workspace management.
/// </summary>
/// <summary>
/// Simple data class for recent workspace entries (not currently open as tabs).
/// </summary>
public class RecentWorkspaceItem
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

public partial class WorkspaceSidebarViewModel : ObservableObject
{
    private readonly IGitWorktreeService _worktreeService;
    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly IGitStatusService _gitStatusService;
    private readonly IProcessService _processService;
    private readonly IFileSystem _fileSystem;
    private readonly IStatisticsService _statisticsService;

    // Internal unfiltered collections
    private List<WorkspaceEntryViewModel> _allWorkspaces = [];
    private List<WorkspaceEntryViewModel> _allPlaygrounds = [];
    private List<RecentWorkspaceItem> _allRecentWorkspaces = [];

    /// <summary>
    /// Reference to MainViewModel - set after construction to avoid circular dependency.
    /// </summary>
    public MainViewModel? MainViewModel { get; set; }

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
    private ObservableCollection<WorktreeInfo> _worktrees = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilterText))]
    private string _filterText = "";

    [ObservableProperty]
    private bool _isRecentExpanded = true;

    [ObservableProperty]
    private bool _isWorktreesExpanded = true;

    [ObservableProperty]
    private bool _isOpenProjectsExpanded = true;

    [ObservableProperty]
    private bool _isPlaygroundsExpanded = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasWorktrees;

    [ObservableProperty]
    private string _currentRepositoryName = string.Empty;

    [ObservableProperty]
    private double _width = 250;

    [ObservableProperty]
    private bool _isCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortToggleIcon))]
    [NotifyPropertyChangedFor(nameof(SortToggleToolTip))]
    private WorkspaceSortMode _sortMode = WorkspaceSortMode.Manual;

    [ObservableProperty]
    private bool _isPullAllInProgress;

    [ObservableProperty]
    private ObservableCollection<RecentWorkspaceItem> _recentWorkspaces = [];

    [ObservableProperty]
    private ObservableCollection<ITabViewModel> _sortedTabs = [];

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
    /// </summary>
    public event EventHandler<string>? GitStatusRefreshed;

    /// <summary>
    /// Event raised when user wants to open the Manage Worktrees popup.
    /// </summary>
    public event EventHandler? ManageWorktreesRequested;

    /// <summary>
    /// Event raised when a workspace tab should be closed.
    /// </summary>
    public event EventHandler<string>? CloseTabRequested;

    /// <summary>
    /// Event raised when a workspace should be duplicated (new tab for same directory).
    /// </summary>
    public event EventHandler<string>? DuplicateTabRequested;

    public WorkspaceSidebarViewModel(
        IGitWorktreeService worktreeService,
        IConfigurationService configService,
        IDialogService dialogService,
        IToastService toastService,
        IGitStatusService gitStatusService,
        IProcessService processService,
        IFileSystem fileSystem,
        IStatisticsService statisticsService)
    {
        _worktreeService = worktreeService;
        _configService = configService;
        _dialogService = dialogService;
        _toastService = toastService;
        _gitStatusService = gitStatusService;
        _processService = processService;
        _fileSystem = fileSystem;
        _statisticsService = statisticsService;
    }

    /// <summary>
    /// Initializes the sidebar with data from configuration.
    /// Creates workspace entries immediately, then loads git status in background.
    /// </summary>
    public void Initialize()
    {
        _ = LoadAsync();
    }

    /// <summary>
    /// Loads workspaces from configuration.
    /// Shows workspace entries immediately, then loads git status in background.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var config = _configService.Load();
            Width = config.Settings.SidebarWidth;
            IsCollapsed = config.Settings.SidebarCollapsed;
            FilterText = config.Settings.WorkspaceFilterText;

            _allWorkspaces.Clear();
            _allPlaygrounds.Clear();
            Workspaces.Clear();
            Playgrounds.Clear();

            // Create all VMs first (cheap, no I/O)
            foreach (var workspace in config.Workspaces.OrderBy(w => w.Order))
            {
                var vm = CreateWorkspaceEntryViewModel(workspace);

                if (workspace.Section == "playground")
                    _allPlaygrounds.Add(vm);
                else
                    _allWorkspaces.Add(vm);
            }

            // Also add open folders that aren't in workspaces yet
            foreach (var path in config.OpenFolders)
            {
                var existsInWorkspaces = _allWorkspaces.Any(w =>
                    string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
                var existsInPlaygrounds = _allPlaygrounds.Any(w =>
                    string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));

                if (!existsInWorkspaces && !existsInPlaygrounds && _fileSystem.DirectoryExists(path))
                {
                    var workspace = new Workspace
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = System.IO.Path.GetFileName(path) ?? path,
                        Path = path,
                        Section = "main",
                        Order = _allWorkspaces.Count,
                        IsExpanded = true
                    };
                    var vm = CreateWorkspaceEntryViewModel(workspace);
                    _allWorkspaces.Add(vm);
                }
            }

            // Set sort mode after workspaces are added so the change handler can sort them
            SortMode = config.Settings.WorkspaceSortMode;

            // Build recent workspaces list from open folders
            _allRecentWorkspaces = config.OpenFolders
                .Where(path => _fileSystem.DirectoryExists(path))
                .Select(path => new RecentWorkspaceItem { Path = path })
                .ToList();
            RefreshRecentWorkspaces();

            // Apply filter and sort - workspace entries are now visible and interactive
            ApplyFilterAndSort();
            RefreshSortedTabs();
        }
        finally
        {
            IsLoading = false;
        }

        // Load git status/worktrees in background after the overlay is removed,
        // unless git tracking is disabled.
        var trackingMode = _configService.Load().Settings.GitTrackingMode;
        if (trackingMode != GitTrackingMode.Disabled)
        {
            _ = LoadWorkspaceGitStatusAsync();
        }
    }

    /// <summary>
    /// Loads git status and worktrees for all workspaces in the background.
    /// Runs in batches of 5 for good throughput without flooding the UI.
    /// </summary>
    private async Task LoadWorkspaceGitStatusAsync()
    {
        const int batchSize = 5;
        var vms = _allWorkspaces.Concat(_allPlaygrounds).ToList();

        for (var i = 0; i < vms.Count; i += batchSize)
        {
            var batch = vms.Skip(i).Take(batchSize);
            await Task.WhenAll(batch.Select(async vm =>
            {
                try
                {
                    await vm.LoadAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Workspace load failed for {vm.Name}: {ex.Message}");
                }
            }));
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
            Name = System.IO.Path.GetFileName(path) ?? path,
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
    public async Task<(int added, List<string> skipped)> AddWorkspacesAsync(IEnumerable<string> paths, string section = "main")
    {
        int added = 0;
        var skipped = new List<string>();

        foreach (var path in paths)
        {
            var workspace = await AddWorkspaceAsync(path, section);
            if (workspace != null)
                added++;
            else
                skipped.Add(System.IO.Path.GetFileName(path) ?? path);
        }

        return (added, skipped);
    }

    /// <summary>
    /// Removes a workspace from the sidebar.
    /// </summary>
    public void RemoveWorkspace(WorkspaceEntryViewModel workspace)
    {
        if (workspace.Section == "playground")
            _allPlaygrounds.Remove(workspace);
        else
            _allWorkspaces.Remove(workspace);

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
        var exists = _allWorkspaces.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                     _allPlaygrounds.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            var isWorktree = await _worktreeService.IsWorktreeAsync(path);
            if (isWorktree)
            {
                var mainPath = await _worktreeService.GetMainWorktreePathAsync(path);
                if (mainPath != null)
                {
                    var mainWorkspace = FindWorkspaceByPath(mainPath);
                    if (mainWorkspace != null)
                    {
                        await mainWorkspace.LoadAsync();
                        return;
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
    /// Refreshes git status for all workspaces in batches.
    /// </summary>
    public async Task RefreshAllGitStatusAsync()
    {
        const int batchSize = 5;
        var all = _allWorkspaces.Concat(_allPlaygrounds).ToList();

        for (var i = 0; i < all.Count; i += batchSize)
        {
            var batch = all.Skip(i).Take(batchSize).ToList();

            var results = new ConcurrentBag<(WorkspaceEntryViewModel vm, GitStatus? status)>();
            await Task.WhenAll(batch.Select(async w =>
            {
                try
                {
                    var status = await Task.Run(() => _gitStatusService.GetGitStatusAsync(w.Path));
                    results.Add((w, status));
                }
                catch { /* Silently ignore git status errors */ }
            }));

            foreach (var (vm, status) in results)
                vm.GitStatus = status;
        }
    }

    /// <summary>
    /// Fetches from git remotes for all workspaces and refreshes their status.
    /// Runs git I/O in batches of 5 on the thread pool.
    /// </summary>
    public async Task FetchAllAsync()
    {
        const int batchSize = 5;
        var all = _allWorkspaces.Concat(_allPlaygrounds).ToList();

        for (var i = 0; i < all.Count; i += batchSize)
        {
            var batch = all.Skip(i).Take(batchSize).ToList();

            var results = new ConcurrentBag<(WorkspaceEntryViewModel vm, GitStatus? status)>();
            await Task.WhenAll(batch.Select(async w =>
            {
                try
                {
                    await Task.Run(async () =>
                    {
                        await _gitStatusService.FetchAllAsync(w.Path);
                        var status = await _gitStatusService.GetGitStatusAsync(w.Path);
                        results.Add((w, status));
                    });
                }
                catch
                {
                    // Silently ignore fetch errors (network issues, etc.)
                }
            }));

            foreach (var (vm, status) in results)
                vm.GitStatus = status;
        }
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

    /// <summary>
    /// Returns all workspace domain objects (for API server use without config reload).
    /// </summary>
    public List<Workspace> GetAllWorkspaces()
    {
        return _allWorkspaces.Select(w => w.Workspace)
            .Concat(_allPlaygrounds.Select(w => w.Workspace))
            .ToList();
    }

    /// <summary>
    /// Returns all workspace entry ViewModels (for AutoFetchAll to coordinate UI updates).
    /// </summary>
    public List<WorkspaceEntryViewModel> GetAllWorkspaceEntries()
    {
        return _allWorkspaces.Concat(_allPlaygrounds).ToList();
    }

    /// <summary>
    /// Adds or updates a workspace in the sidebar when a tab is opened.
    /// Backward-compatible wrapper around SyncWithOpenTabAsync.
    /// </summary>
    public async Task TrackWorkspaceOpenedAsync(string path)
    {
        await SyncWithOpenTabAsync(path);
    }

    /// <summary>
    /// Updates the git branch for a workspace (refreshes its git status).
    /// </summary>
    public void UpdateWorkspaceGitBranch(string path, string branch)
    {
        var workspace = FindWorkspaceByPath(path);
        if (workspace != null)
        {
            // Trigger a background refresh to get full status
            _ = workspace.RefreshGitStatusAsync();
        }
    }

    private WorkspaceEntryViewModel? FindWorkspaceByPath(string path)
    {
        return _allWorkspaces.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)) ??
               _allPlaygrounds.FirstOrDefault(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    // Cached to avoid loading config per workspace entry
    private bool? _cachedShowStashCount;

    private WorkspaceEntryViewModel CreateWorkspaceEntryViewModel(Workspace workspace)
    {
        _cachedShowStashCount ??= _configService.Load().Settings.ShowStashCount;
        var vm = new WorkspaceEntryViewModel(workspace, _worktreeService, _gitStatusService);
        vm.ShowStashCount = _cachedShowStashCount.Value;
        vm.OpenRequested += OnWorkspaceOpenRequested;
        vm.WorktreeOpenRequested += OnWorktreeOpenRequested;
        return vm;
    }

    private void OnWorkspaceOpenRequested(object? sender, string path)
    {
        OpenTabRequested?.Invoke(this, path);
        MainViewModel?.OpenProjectTab(path);
    }

    private void OnWorktreeOpenRequested(object? sender, string path)
    {
        OpenTabRequested?.Invoke(this, path);
        MainViewModel?.OpenProjectTab(path);
    }

    private void SaveWorkspaces()
    {
        var config = _configService.Load();
        config.Workspaces.Clear();

        var order = 0;
        foreach (var vm in _allWorkspaces)
        {
            vm.Order = order++;
            config.Workspaces.Add(vm.Workspace);
        }

        foreach (var vm in _allPlaygrounds)
        {
            vm.Order = order++;
            config.Workspaces.Add(vm.Workspace);
        }

        config.Settings.SidebarWidth = Width;
        config.Settings.SidebarCollapsed = IsCollapsed;
        config.Settings.WorkspaceSortMode = SortMode;
        config.Settings.WorkspaceFilterText = FilterText;

        _configService.Save(config);
        WorkspacesChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnWidthChanged(double value)
    {
        if (!IsLoading) SaveWorkspaces();
    }

    partial void OnIsCollapsedChanged(bool value)
    {
        if (!IsLoading) SaveWorkspaces();
    }

    partial void OnSortModeChanged(WorkspaceSortMode value)
    {
        if (!IsLoading)
        {
            var config = _configService.Load();
            config.Settings.WorkspaceSortMode = value;
            _configService.Save(config);
        }
        ApplyFilterAndSort();
        RefreshSortedTabs();
    }

    partial void OnFilterTextChanged(string value)
    {
        if (!IsLoading)
        {
            var config = _configService.Load();
            config.Settings.WorkspaceFilterText = value;
            _configService.Save(config);
        }
        ApplyFilterAndSort();
        RefreshRecentWorkspaces();
    }

    /// <summary>
    /// Applies both filtering and sorting to workspaces.
    /// </summary>
    public void ApplyFilterAndSort()
    {
        var filteredWorkspaces = FilterWorkspaces(_allWorkspaces);
        var filteredPlaygrounds = FilterWorkspaces(_allPlaygrounds);

        var sortedWorkspaces = ApplySortWithPinned(filteredWorkspaces);
        var sortedPlaygrounds = ApplySortWithPinned(filteredPlaygrounds);

        Workspaces.Clear();
        foreach (var w in sortedWorkspaces)
            Workspaces.Add(w);

        Playgrounds.Clear();
        foreach (var w in sortedPlaygrounds)
            Playgrounds.Add(w);
    }

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

    private List<WorkspaceEntryViewModel> ApplySortWithPinned(List<WorkspaceEntryViewModel> workspaces)
    {
        var pinned = workspaces.Where(w => w.IsPinned);
        var unpinned = workspaces.Where(w => !w.IsPinned);

        var sortedPinned = ApplyCurrentSort(pinned);
        var sortedUnpinned = ApplyCurrentSort(unpinned);

        return sortedPinned.Concat(sortedUnpinned).ToList();
    }

    private IEnumerable<WorkspaceEntryViewModel> ApplyCurrentSort(IEnumerable<WorkspaceEntryViewModel> workspaces)
    {
        return SortMode switch
        {
            WorkspaceSortMode.Usage => workspaces.OrderByDescending(w => CalculateUsageScore(w.Path)),
            WorkspaceSortMode.Alphabetical => workspaces.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase),
            _ => workspaces.OrderBy(w => w.Order) // Manual
        };
    }

    private double CalculateUsageScore(string path)
    {
        const int DAYS = 7;
        const double FOCUS_WEIGHT = 0.6;
        const double CHAR_WEIGHT = 0.4;

        var focusSeconds = _statisticsService.GetFocusTimeForPeriod(path, DAYS);
        var charCount = _statisticsService.GetCharCountForPeriod(path, DAYS);

        var focusMinutes = focusSeconds / 60.0;
        var charThousands = charCount / 1000.0;

        return (focusMinutes * FOCUS_WEIGHT) + (charThousands * CHAR_WEIGHT);
    }

    /// <summary>
    /// Rebuilds the SortedTabs collection from MainViewModel.Tabs, applying the current sort mode.
    /// </summary>
    public void RefreshSortedTabs()
    {
        if (MainViewModel == null) return;

        var tabs = MainViewModel.Tabs.ToList();

        IEnumerable<ITabViewModel> sorted = SortMode switch
        {
            WorkspaceSortMode.Alphabetical => tabs.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            WorkspaceSortMode.Usage => tabs.OrderByDescending(t =>
            {
                if (t is TerminalPairTabViewModel tpt)
                    return CalculateUsageScore(tpt.Pair.WorkingDirectory);
                return 0;
            }),
            _ => tabs // Manual = original tab order
        };

        SortedTabs = new ObservableCollection<ITabViewModel>(sorted);
    }

    /// <summary>
    /// Rebuilds the RecentWorkspaces collection, filtering by current search text.
    /// </summary>
    private void RefreshRecentWorkspaces()
    {
        var filter = FilterText;
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _allRecentWorkspaces
            : _allRecentWorkspaces.Where(w =>
                w.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        RecentWorkspaces = new ObservableCollection<RecentWorkspaceItem>(filtered);
    }

    #region Commands - Workspace Management

    [RelayCommand]
    private async Task AddWorkspace()
    {
        // Connected via MainViewModel's folder picker
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
            RemoveWorkspace(workspace);
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

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = "";
    }

    [RelayCommand]
    private void TogglePin(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        workspace.IsPinned = !workspace.IsPinned;
        SaveWorkspaces();
        ApplyFilterAndSort();
    }

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

    [RelayCommand]
    private void ClearAllIndicators()
    {
        foreach (var workspace in _allWorkspaces.Concat(_allPlaygrounds))
        {
            workspace.HasUnreadActivity = false;
        }
    }

    #endregion

    #region Commands - Workspace Git Operations

    [RelayCommand]
    private async Task GitFetchWorkspace(WorkspaceEntryViewModel? workspace)
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

    [RelayCommand]
    private async Task GitPullRebaseWorkspace(WorkspaceEntryViewModel? workspace)
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

    [RelayCommand]
    private async Task GitPushWorkspace(WorkspaceEntryViewModel? workspace)
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

    [RelayCommand]
    private async Task PullAll()
    {
        if (IsPullAllInProgress) return;

        IsPullAllInProgress = true;
        var allWorkspaces = _allWorkspaces.Concat(_allPlaygrounds).ToList();

        using var toast = _toastService.ShowProgress($"Pulling {allWorkspaces.Count} repos...");

        var successCount = 0;
        var failCount = 0;
        const int batchSize = 5;

        for (var i = 0; i < allWorkspaces.Count; i += batchSize)
        {
            var batch = allWorkspaces.Skip(i).Take(batchSize).ToList();

            var results = new ConcurrentBag<(WorkspaceEntryViewModel vm, GitStatus? status, bool success)>();
            await Task.WhenAll(batch.Select(async w =>
            {
                try
                {
                    await Task.Run(async () =>
                    {
                        var result = await _gitStatusService.PullRebaseAsync(w.Path);
                        if (result.Success)
                        {
                            var status = await _gitStatusService.GetGitStatusAsync(w.Path);
                            results.Add((w, status, true));
                        }
                        else
                        {
                            results.Add((w, null, false));
                        }
                    });
                }
                catch
                {
                    Interlocked.Increment(ref failCount);
                }
            }));

            foreach (var (vm, status, success) in results)
            {
                if (success)
                {
                    vm.GitStatus = status;
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    Interlocked.Increment(ref failCount);
                }
            }
        }

        IsPullAllInProgress = false;

        if (failCount == 0)
            toast.Complete($"Pulled {successCount} repos");
        else
            toast.Fail($"Pulled {successCount}, failed {failCount}");
    }

    #endregion

    #region Commands - Worktree Operations

    [RelayCommand]
    private async Task CreateWorktreeAsync()
    {
        await CreateWorktreeForPathAsync(null);
    }

    [RelayCommand]
    private async Task CreateWorktreeForWorkspace(WorkspaceEntryViewModel? workspace)
    {
        if (workspace != null)
            await CreateWorktreeForPathAsync(workspace.Path);
    }

    private async Task CreateWorktreeForPathAsync(string? repoPath)
    {
        if (string.IsNullOrEmpty(repoPath))
        {
            if (MainViewModel?.SelectedTab is TerminalPairTabViewModel terminalTab)
                repoPath = terminalTab.Pair.WorkingDirectory;
        }

        if (string.IsNullOrEmpty(repoPath)) return;

        if (_worktreeService is not GitWorktreeService concreteService)
        {
            _toastService.Show("Worktree creation not available", ToastType.Warning);
            return;
        }

        var branches = await concreteService.GetBranchesForWorktreeAsync(repoPath);
        var result = _dialogService.ShowCreateWorktreeDialog(repoPath, branches, repoPath);
        if (result == null) return;

        var createResult = await _worktreeService.CreateWorktreeAsync(
            repoPath,
            result.BranchName,
            result.WorktreePath,
            result.CreateNewBranch);

        if (createResult.Success)
        {
            _toastService.Show($"Worktree created: {result.BranchName}", ToastType.Success);

            // Refresh the workspace to show new worktree
            var workspace = FindWorkspaceByPath(repoPath);
            if (workspace != null)
                await workspace.LoadAsync();

            if (result.OpenAfterCreation)
            {
                OpenTabRequested?.Invoke(this, result.WorktreePath);
                MainViewModel?.OpenProjectTab(result.WorktreePath);
            }
        }
        else
        {
            _toastService.Show($"Failed to create worktree: {createResult.Error}", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task ShowCreateWorktreeDialogAsync()
    {
        await CreateWorktreeForPathAsync(null);
    }

    [RelayCommand]
    private async Task RemoveWorktreeAsync(object? worktreeObj)
    {
        // Accept both WorktreeEntryViewModel and WorktreeInfo from AXAML bindings
        string? path = null;
        string? displayName = null;

        if (worktreeObj is WorktreeEntryViewModel wvm)
        {
            path = wvm.Path;
            displayName = wvm.DisplayName;
        }
        else if (worktreeObj is WorktreeInfo wi)
        {
            path = wi.Path;
            displayName = wi.DisplayName;
        }

        if (path == null) return;

        var confirmed = _dialogService.ShowConfirmation(
            $"Remove worktree '{displayName}'?\n\nThis will delete the worktree directory:\n{path}",
            "Remove Worktree");

        if (!confirmed) return;

        var result = await _worktreeService.RemoveWorktreeAsync(path, force: false);
        if (result.Success)
        {
            await RefreshAllGitStatusAsync();
            await LoadAsync();
        }
        else
        {
            var forceConfirmed = _dialogService.ShowConfirmation(
                $"Worktree has uncommitted changes.\n\nForce remove anyway?\n\nError: {result.Error}",
                "Force Remove Worktree");

            if (forceConfirmed)
            {
                result = await _worktreeService.RemoveWorktreeAsync(path, force: true);
                if (result.Success)
                    await LoadAsync();
                else
                    _dialogService.ShowError($"Failed to remove worktree: {result.Error}");
            }
        }
    }

    [RelayCommand]
    private async Task CreateBranchFromWorktreeAsync(object? worktreeObj)
    {
        // Stub: requires additional git operations not yet available
        _toastService.Show("Branch creation not available in this version", ToastType.Warning);
    }

    [RelayCommand]
    private void OpenManageWorktrees()
    {
        ManageWorktreesRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Alias to keep the ManageWorktrees command name compatible with existing bindings.
    /// </summary>
    [RelayCommand]
    private void ManageWorktrees()
    {
        ManageWorktreesRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Commands - Open Workspace/Worktree

    [RelayCommand]
    private void OpenWorkspace(object? item)
    {
        var path = item switch
        {
            WorkspaceEntryViewModel w => w.Path,
            RecentWorkspaceItem r => r.Path,
            _ => null
        };
        if (path == null) return;
        OpenTabRequested?.Invoke(this, path);
        MainViewModel?.OpenProjectTab(path);
    }

    [RelayCommand]
    private void OpenWorktree(object? worktreeObj)
    {
        // Accept both WorktreeEntryViewModel and WorktreeInfo from AXAML bindings
        string? path = null;
        if (worktreeObj is WorktreeEntryViewModel wvm)
            path = wvm.Path;
        else if (worktreeObj is WorktreeInfo wi)
            path = wi.Path;

        if (path == null) return;
        OpenTabRequested?.Invoke(this, path);
        MainViewModel?.OpenProjectTab(path);
    }

    [RelayCommand]
    private void RemoveFromRecent(object? item)
    {
        if (item is WorkspaceEntryViewModel workspace)
            RemoveWorkspace(workspace);
        else if (item is RecentWorkspaceItem recent)
        {
            _allRecentWorkspaces.Remove(recent);
            RefreshRecentWorkspaces();
        }
    }

    [RelayCommand]
    private void TogglePlayground(object? item)
    {
        if (item is WorkspaceEntryViewModel workspace)
            MoveWorkspaceToOtherSection(workspace);
        else if (item is RecentWorkspaceItem recent)
            recent.IsPlayground = !recent.IsPlayground;
    }

    [RelayCommand]
    private void OpenInFinder(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        _processService.OpenFolder(workspace.Path);
    }

    [RelayCommand]
    private void DuplicateWorkspace(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        DuplicateTabRequested?.Invoke(this, workspace.Path);
        MainViewModel?.OpenProjectTab(workspace.Path, forceNew: true);
    }

    [RelayCommand]
    private void CloseWorkspace(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        CloseTabRequested?.Invoke(this, workspace.Path);
        RemoveWorkspace(workspace);
    }

    [RelayCommand]
    private void CloseTabOnly(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        workspace.HasUnreadActivity = false;
        workspace.IsActive = false;
        workspace.IsWaitingForInput = false;
        CloseTabRequested?.Invoke(this, workspace.Path);
    }

    #endregion

    #region Tab Operations for Open Projects

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
            MainViewModel.SelectedTab = tab;
    }

    [RelayCommand]
    private void OpenTab(ITabViewModel? tab)
    {
        if (tab != null && MainViewModel != null)
            MainViewModel.SelectedTab = tab;
    }

    [RelayCommand]
    private void DuplicateTab(ITabViewModel? tab)
    {
        if (tab is TerminalPairTabViewModel terminalTab && MainViewModel != null)
            MainViewModel.OpenProjectTab(terminalTab.Pair.WorkingDirectory, forceNew: true);
    }

    [RelayCommand]
    private void MoveTabUp(ITabViewModel? tab)
    {
        if (tab == null || MainViewModel == null) return;
        var index = MainViewModel.Tabs.IndexOf(tab);
        if (index > 0)
            MainViewModel.Tabs.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveTabDown(ITabViewModel? tab)
    {
        if (tab == null || MainViewModel == null) return;
        var index = MainViewModel.Tabs.IndexOf(tab);
        if (index >= 0 && index < MainViewModel.Tabs.Count - 1)
            MainViewModel.Tabs.Move(index, index + 1);
    }

    [RelayCommand]
    private void MoveTabToTop(ITabViewModel? tab)
    {
        if (tab == null || MainViewModel == null) return;
        var index = MainViewModel.Tabs.IndexOf(tab);
        if (index > 0)
            MainViewModel.Tabs.Move(index, 0);
    }

    [RelayCommand]
    private void MoveTabToBottom(ITabViewModel? tab)
    {
        if (tab == null || MainViewModel == null) return;
        var index = MainViewModel.Tabs.IndexOf(tab);
        if (index >= 0 && index < MainViewModel.Tabs.Count - 1)
            MainViewModel.Tabs.Move(index, MainViewModel.Tabs.Count - 1);
    }

    [RelayCommand]
    private async Task CreateWorktreeForTabAsync(ITabViewModel? tab)
    {
        if (tab is TerminalPairTabViewModel terminalTab)
            await CreateWorktreeForPathAsync(terminalTab.Pair.WorkingDirectory);
    }

    [RelayCommand]
    private void CloseTab(ITabViewModel? tab)
    {
        if (tab != null && MainViewModel != null)
            MainViewModel.Tabs.Remove(tab);
    }

    [RelayCommand]
    private async Task GitFetchAsync(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        var workingDir = terminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.FetchAllAsync(workingDir);
        if (!result.Success)
            _toastService.Show($"Fetch failed: {result.Error}", ToastType.Error);
        else
            _toastService.Show("Fetched successfully", ToastType.Success);
    }

    [RelayCommand]
    private async Task GitPullRebaseAsync(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        var workingDir = terminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.PullRebaseAsync(workingDir);
        if (!result.Success)
            _toastService.Show($"Pull failed: {result.Error}", ToastType.Error);
        else
            _toastService.Show("Pulled successfully", ToastType.Success);
    }

    [RelayCommand]
    private async Task GitPushAsync(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        var workingDir = terminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.PushAsync(workingDir);
        if (!result.Success)
            _toastService.Show($"Push failed: {result.Error}", ToastType.Error);
        else
            _toastService.Show("Pushed successfully", ToastType.Success);
    }

    [RelayCommand]
    private void OpenInFinderTab(ITabViewModel? tab)
    {
        if (tab is not TerminalPairTabViewModel terminalTab) return;
        _processService.OpenFolder(terminalTab.Pair.WorkingDirectory);
    }

    #endregion

    /// <summary>
    /// Refreshes the worktrees list for the currently selected tab.
    /// </summary>
    public async Task RefreshWorktreesAsync()
    {
        // Get current tab's working directory
        string? workingDir = null;
        if (MainViewModel?.SelectedTab is TerminalPairTabViewModel terminalTab)
            workingDir = terminalTab.Pair.WorkingDirectory;

        if (string.IsNullOrEmpty(workingDir))
        {
            Worktrees.Clear();
            HasWorktrees = false;
            CurrentRepositoryName = string.Empty;
            return;
        }

        try
        {
            var worktrees = await _worktreeService.ListWorktreesAsync(workingDir);
            Worktrees = new ObservableCollection<WorktreeInfo>(worktrees);
            HasWorktrees = worktrees.Count > 1; // More than just the main worktree
            CurrentRepositoryName = System.IO.Path.GetFileName(workingDir) ?? "";
        }
        catch
        {
            Worktrees.Clear();
            HasWorktrees = false;
            CurrentRepositoryName = string.Empty;
        }
    }
}
