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
    private async Task Refresh()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }
}
