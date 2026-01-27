using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for the workspace-scoped tasks panel.
/// Shows Claude tasks for the current workspace in 3 sections: Completed, In Progress, TODO.
/// </summary>
public partial class WorkspaceTasksPanelViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly IClaudeTaskDetectionService? _claudeTaskDetectionService;
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
        IClaudeTaskDetectionService? claudeTaskDetectionService = null)
    {
        _taskService = taskService;
        _claudeTaskDetectionService = claudeTaskDetectionService;

        // Subscribe to task changes
        _taskService.TasksChanged += (s, e) => RefreshTasks();

        // Subscribe to Claude task detection events
        if (_claudeTaskDetectionService != null)
        {
            _claudeTaskDetectionService.ClaudeTaskChanged += (s, e) => RefreshTasks();
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

        // Get all tasks that match the workspace
        var allTasks = _taskService.GetAllTasks()
            .Where(t => t.ProjectPaths.Any(p => NormalizePath(p) == normalizedWorkspace))
            .ToList();

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
    /// Opens the main task panel for full task management.
    /// </summary>
    [RelayCommand]
    private void OpenTaskPanel()
    {
        // This will be handled by the parent ViewModel (MainViewModel)
        // by raising an event or calling a delegate
        TaskPanelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event raised when user wants to open the full task panel.
    /// </summary>
    public event EventHandler? TaskPanelRequested;
}
