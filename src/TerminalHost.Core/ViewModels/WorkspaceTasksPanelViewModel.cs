using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for the workspace-scoped tasks panel.
/// Shows Claude tasks for the current workspace in 3 sections: Completed, In Progress, TODO.
/// Reads tasks from both ITaskService and IClaudeTaskFileService (~/.claude/tasks/).
/// </summary>
public partial class WorkspaceTasksPanelViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly IClaudeTaskDetectionService? _claudeTaskDetectionService;
    private readonly IClaudeTaskFileService? _claudeTaskFileService;
    private readonly IDispatcherService? _dispatcherService;
    private string _workspacePath = string.Empty;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _hasAnyTasks;

    // Task collections by status
    public ObservableCollection<FocusTask> CompletedTasks { get; } = [];
    public ObservableCollection<FocusTask> InProgressTasks { get; } = [];
    public ObservableCollection<FocusTask> TodoTasks { get; } = [];

    // Section visibility
    [ObservableProperty]
    private bool _hasCompletedTasks;

    [ObservableProperty]
    private bool _hasInProgressTasks;

    [ObservableProperty]
    private bool _hasTodoTasks;

    // Section counts
    public int CompletedCount => CompletedTasks.Count;
    public int InProgressCount => InProgressTasks.Count;
    public int TodoCount => TodoTasks.Count;

    /// <summary>
    /// Current workspace path being displayed.
    /// </summary>
    public string WorkspacePath
    {
        get => _workspacePath;
        set
        {
            if (SetProperty(ref _workspacePath, value))
            {
                RefreshTasks();
            }
        }
    }

    public WorkspaceTasksPanelViewModel(
        ITaskService taskService,
        IClaudeTaskDetectionService? claudeTaskDetectionService = null,
        IClaudeTaskFileService? claudeTaskFileService = null,
        IDispatcherService? dispatcherService = null)
    {
        _taskService = taskService;
        _claudeTaskDetectionService = claudeTaskDetectionService;
        _claudeTaskFileService = claudeTaskFileService;
        _dispatcherService = dispatcherService;

        // Subscribe to task changes
        _taskService.TasksChanged += (s, e) => SafeRefreshTasks();

        // Subscribe to Claude task detection events
        if (_claudeTaskDetectionService != null)
        {
            _claudeTaskDetectionService.ClaudeTaskChanged += (s, e) => SafeRefreshTasks();
        }

        // Subscribe to file-based Claude task changes
        if (_claudeTaskFileService != null)
        {
            _claudeTaskFileService.TasksChanged += (s, e) => SafeRefreshTasks();
        }
    }

    /// <summary>
    /// Thread-safe refresh that marshals to UI thread if dispatcher is available.
    /// </summary>
    private void SafeRefreshTasks()
    {
        if (_dispatcherService != null)
        {
            _dispatcherService.BeginInvoke(RefreshTasks);
        }
        else
        {
            RefreshTasks();
        }
    }

    /// <summary>
    /// Shows the panel for a specific workspace.
    /// </summary>
    public void ShowForWorkspace(string workspacePath)
    {
        WorkspacePath = workspacePath;
        IsVisible = true;
    }

    /// <summary>
    /// Hides the panel.
    /// </summary>
    [RelayCommand]
    private void Hide()
    {
        IsVisible = false;
    }

    /// <summary>
    /// Refreshes the task lists filtered by the current workspace.
    /// Combines tasks from ITaskService, IClaudeTaskDetectionService, and IClaudeTaskFileService.
    /// </summary>
    public void RefreshTasks()
    {
        if (string.IsNullOrEmpty(WorkspacePath))
        {
            ClearAll();
            return;
        }

        // Normalize the workspace path for comparison
        var normalizedWorkspace = NormalizePath(WorkspacePath);

        // Collect all tasks from different sources
        var allTasks = new List<FocusTask>();
        var seenTaskIds = new HashSet<string>();

        // 1. Get tasks from TaskService (manually created tasks)
        var serviceTasks = _taskService.GetAllTasks()
            .Where(t => t.ProjectPaths.Any(p => NormalizePath(p) == normalizedWorkspace));
        foreach (var task in serviceTasks)
        {
            var taskId = task.ClaudeTaskId ?? task.Id;
            if (seenTaskIds.Add(taskId))
            {
                allTasks.Add(task);
            }
        }

        // 2. Get tasks from ClaudeTaskFileService (~/.claude/tasks/)
        if (_claudeTaskFileService != null)
        {
            var fileTasks = _claudeTaskFileService.GetAllTasks()
                .Where(t => IsTaskForWorkspace(t, normalizedWorkspace));
            foreach (var task in fileTasks)
            {
                // Use session ID + task ID as unique key (same ClaudeTaskId can exist in different sessions)
                var taskId = task.ClaudeSessionId != null
                    ? $"{task.ClaudeSessionId}:{task.ClaudeTaskId}"
                    : task.ClaudeTaskId ?? task.Id;
                if (seenTaskIds.Add(taskId))
                {
                    allTasks.Add(task);
                }
            }
        }

        // 3. Get tasks from ClaudeTaskDetectionService (terminal output detection)
        if (_claudeTaskDetectionService != null)
        {
            var detectedTasks = _claudeTaskDetectionService.GetAllClaudeTasks()
                .Where(t => IsTaskForWorkspace(t, normalizedWorkspace));
            foreach (var task in detectedTasks)
            {
                // Use session ID + task ID as unique key (same ClaudeTaskId can exist in different sessions)
                var taskId = task.ClaudeSessionId != null
                    ? $"{task.ClaudeSessionId}:{task.ClaudeTaskId}"
                    : task.ClaudeTaskId ?? task.Id;
                if (seenTaskIds.Add(taskId))
                {
                    allTasks.Add(task);
                }
            }
        }

        // Group by status
        var completed = allTasks
            .Where(t => t.Status == FocusTaskStatus.Completed)
            .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
            .ToList();

        var inProgress = allTasks
            .Where(t => t.Status == FocusTaskStatus.InProgress)
            .OrderByDescending(t => t.StartedAt ?? t.CreatedAt)
            .ToList();

        var todo = allTasks
            .Where(t => t.Status == FocusTaskStatus.NotStarted)
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        // Update collections
        UpdateCollection(CompletedTasks, completed);
        UpdateCollection(InProgressTasks, inProgress);
        UpdateCollection(TodoTasks, todo);

        // Update visibility flags
        HasCompletedTasks = CompletedTasks.Count > 0;
        HasInProgressTasks = InProgressTasks.Count > 0;
        HasTodoTasks = TodoTasks.Count > 0;
        HasAnyTasks = HasCompletedTasks || HasInProgressTasks || HasTodoTasks;

        // Notify count changes
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(InProgressCount));
        OnPropertyChanged(nameof(TodoCount));
    }

    /// <summary>
    /// Checks if a task belongs to the specified workspace.
    /// Tasks without project paths are shown in all workspaces.
    /// </summary>
    private static bool IsTaskForWorkspace(FocusTask task, string normalizedWorkspace)
    {
        // Tasks without project paths are shown everywhere
        if (task.ProjectPaths == null || task.ProjectPaths.Count == 0)
            return true;

        return task.ProjectPaths.Any(p => NormalizePath(p) == normalizedWorkspace);
    }

    private void ClearAll()
    {
        CompletedTasks.Clear();
        InProgressTasks.Clear();
        TodoTasks.Clear();
        HasCompletedTasks = false;
        HasInProgressTasks = false;
        HasTodoTasks = false;
        HasAnyTasks = false;
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(InProgressCount));
        OnPropertyChanged(nameof(TodoCount));
    }

    private static void UpdateCollection(ObservableCollection<FocusTask> collection, List<FocusTask> newItems)
    {
        // Clear and add new items (simple approach, could be optimized with diffing)
        collection.Clear();
        foreach (var item in newItems)
        {
            collection.Add(item);
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        try
        {
            // Normalize the path and lowercase for case-insensitive comparison
            // Both Windows and macOS (default APFS) use case-insensitive filesystems
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();
        }
        catch
        {
            return path.ToLowerInvariant();
        }
    }

    /// <summary>
    /// Opens the Claude Tasks panel (Cmd+Shift+K) for full task viewing.
    /// </summary>
    [RelayCommand]
    private void OpenTaskPanel()
    {
        // This will be handled by the parent ViewModel (MainViewModel)
        // by raising an event that opens the Claude Tasks panel
        ClaudeTasksPanelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event raised when user wants to open the Claude Tasks panel (Cmd+Shift+K).
    /// </summary>
    public event EventHandler? ClaudeTasksPanelRequested;

    /// <summary>
    /// Legacy event for backwards compatibility.
    /// </summary>
    [Obsolete("Use ClaudeTasksPanelRequested instead")]
    public event EventHandler? TaskPanelRequested;
}
