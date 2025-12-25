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
        IFileSystem fileSystem)
    {
        _configurationService = configurationService;
        _gitWorktreeService = gitWorktreeService;
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
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
    public void UpdateActivity(string path, bool hasActivity)
    {
        var workspace = FindWorkspaceByPath(path);
        if (workspace != null)
        {
            workspace.HasActivity = hasActivity;
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
    /// Closes the tab for a workspace (if open).
    /// </summary>
    [RelayCommand]
    private void CloseWorkspaceTab(WorkspaceEntryViewModel? workspace)
    {
        if (workspace == null) return;
        CloseTabRequested?.Invoke(this, workspace.Path);
    }
}
