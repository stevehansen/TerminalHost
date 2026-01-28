using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Claude Tasks panel (Ctrl+Shift+K) - shows Claude Code task activity in real-time.
/// Supports Panel, Popup, and Window display states.
/// Provides manual task creation and real-time progress monitoring.
/// Reads tasks from Claude Code's ~/.claude/tasks/ folder.
/// </summary>
public partial class ClaudeTasksPanelViewModel : BasePanelViewModel
{
    private readonly IClaudeTaskDetectionService? _claudeTaskDetectionService;
    private readonly IClaudeTaskFileService? _claudeTaskFileService;
    private readonly ITaskService _taskService;
    private readonly IDispatcherService _dispatcherService;

    #region IPanelableViewModel Implementation

    public override string PanelId => "claudeTasks";
    public override string PanelTitle => "Claude Tasks";
    public override string PanelIcon => "\U0001F916"; // Robot emoji
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "\u2795", // Plus sign
            Tooltip = "Create manual task",
            Command = CreateManualTaskCommand
        },
        new PanelHeaderCommand
        {
            Icon = "\u21BB", // Refresh symbol
            Tooltip = "Refresh task list",
            Command = RefreshTasksCommand
        }
    ];

    public override string? StatusText => ActiveTasksCount > 0
        ? $"{ActiveTasksCount} active task{(ActiveTasksCount == 1 ? "" : "s")}"
        : "No active tasks";

    #endregion

    #region Properties

    /// <summary>
    /// All Claude tasks detected or created.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FocusTask> _claudeTasks = [];

    /// <summary>
    /// Active (in-progress) Claude tasks.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FocusTask> _activeTasks = [];

    /// <summary>
    /// Pending (not started) Claude tasks.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FocusTask> _pendingTasks = [];

    /// <summary>
    /// Completed Claude tasks (today).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FocusTask> _completedTasks = [];

    /// <summary>
    /// The currently active Claude task (most recent).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTask))]
    [NotifyPropertyChangedFor(nameof(CurrentTaskProgress))]
    [NotifyPropertyChangedFor(nameof(CurrentTaskElapsed))]
    private FocusTask? _currentTask;

    /// <summary>
    /// Selected task in the list.
    /// </summary>
    [ObservableProperty]
    private FocusTask? _selectedTask;

    /// <summary>
    /// Whether we have an active task.
    /// </summary>
    public bool HasCurrentTask => CurrentTask != null;

    /// <summary>
    /// Current task progress text (e.g., "Installing dependencies...").
    /// </summary>
    public string CurrentTaskProgress =>
        CurrentTask?.ActiveForm ?? CurrentTask?.Title ?? "No active task";

    /// <summary>
    /// Current task elapsed time display.
    /// </summary>
    public string CurrentTaskElapsed
    {
        get
        {
            if (CurrentTask?.StartedAt == null)
                return "";

            var elapsed = DateTime.UtcNow - CurrentTask.StartedAt.Value;
            return elapsed.TotalHours >= 1
                ? $"{elapsed.Hours}h {elapsed.Minutes}m"
                : elapsed.TotalMinutes >= 1
                    ? $"{elapsed.Minutes}m {elapsed.Seconds}s"
                    : $"{elapsed.Seconds}s";
        }
    }

    /// <summary>
    /// Count of active tasks.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _activeTasksCount;

    /// <summary>
    /// Whether the empty state should be shown.
    /// </summary>
    [ObservableProperty]
    private bool _isEmptyStateVisible = true;

    /// <summary>
    /// Whether the popup should be visible (IsOpen AND DisplayState is Popup).
    /// Used by the popup view to avoid showing when panel is docked.
    /// </summary>
    public bool IsPopupVisible => IsOpen && DisplayState == PanelDisplayState.Popup;

    /// <summary>
    /// Text for manual task creation.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitManualTaskCommand))]
    private string _manualTaskTitle = "";

    /// <summary>
    /// Whether task creation form is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isCreateFormVisible;

    /// <summary>
    /// Whether to show all tasks globally or just the current workspace.
    /// When true, shows all Claude tasks. When false, filters by current workspace.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterModeText))]
    private bool _showGlobalTasks;

    /// <summary>
    /// Display text for the current filter mode.
    /// </summary>
    public string FilterModeText => ShowGlobalTasks ? "All Workspaces" : "Current Workspace";

    /// <summary>
    /// Current workspace/project path to filter tasks by.
    /// </summary>
    private string? _currentWorkspacePath;

    #endregion

    public ClaudeTasksPanelViewModel(
        IClaudeTaskDetectionService? claudeTaskDetectionService,
        IClaudeTaskFileService? claudeTaskFileService,
        ITaskService taskService,
        IDispatcherService dispatcherService)
    {
        _claudeTaskDetectionService = claudeTaskDetectionService;
        _claudeTaskFileService = claudeTaskFileService;
        _taskService = taskService;
        _dispatcherService = dispatcherService;

        // Set defaults - defaults to docked Panel
        DisplayState = PanelDisplayState.Panel;
        Width = 600;
        Height = 500;

        // Subscribe to Claude task events if service is available
        if (_claudeTaskDetectionService != null)
        {
            _claudeTaskDetectionService.ClaudeTaskChanged += OnClaudeTaskChanged;
        }

        // Subscribe to file-based task changes
        if (_claudeTaskFileService != null)
        {
            _claudeTaskFileService.TasksChanged += OnFileTasksChanged;
        }

        // Subscribe to property changes to update IsPopupVisible
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsOpen) or nameof(DisplayState))
            {
                OnPropertyChanged(nameof(IsPopupVisible));
            }
        };
    }

    /// <summary>
    /// Handles file-based task changes (tasks read from ~/.claude/tasks/).
    /// </summary>
    private void OnFileTasksChanged(object? sender, EventArgs e)
    {
        _dispatcherService.BeginInvoke(RefreshTasks);
    }

    #region Event Handlers

    /// <summary>
    /// Handles Claude task changes (created, updated, completed, deleted).
    /// </summary>
    private void OnClaudeTaskChanged(object? sender, ClaudeTaskEventArgs e)
    {
        // Marshal to UI thread
        _dispatcherService.BeginInvoke(() =>
        {
            // Check if task is relevant to current view (workspace filter or global)
            if (!ShouldShowTask(e.Task))
                return;

            switch (e.EventType)
            {
                case ClaudeTaskEventType.Created:
                    ClaudeTasks.Add(e.Task);
                    if (e.Task.Status == FocusTaskStatus.InProgress)
                    {
                        ActiveTasks.Add(e.Task);
                        CurrentTask = e.Task;
                    }
                    break;

                case ClaudeTaskEventType.Updated:
                    // Task already in collection, just update CurrentTask if it's this one
                    if (e.Task.Status == FocusTaskStatus.InProgress && CurrentTask?.Id == e.Task.Id)
                    {
                        CurrentTask = e.Task;
                    }
                    break;

                case ClaudeTaskEventType.Completed:
                    // Move from active to completed
                    var activeTask = ActiveTasks.FirstOrDefault(t => t.Id == e.Task.Id);
                    if (activeTask != null)
                    {
                        ActiveTasks.Remove(activeTask);
                    }
                    CompletedTasks.Insert(0, e.Task); // Add to top of completed list

                    // Update current task to next active task
                    if (CurrentTask?.Id == e.Task.Id)
                    {
                        CurrentTask = ActiveTasks.FirstOrDefault();
                    }
                    break;

                case ClaudeTaskEventType.Deleted:
                    var taskToRemove = ClaudeTasks.FirstOrDefault(t => t.Id == e.Task.Id);
                    if (taskToRemove != null)
                    {
                        ClaudeTasks.Remove(taskToRemove);
                    }

                    var activeToRemove = ActiveTasks.FirstOrDefault(t => t.Id == e.Task.Id);
                    if (activeToRemove != null)
                    {
                        ActiveTasks.Remove(activeToRemove);
                    }

                    var completedToRemove = CompletedTasks.FirstOrDefault(t => t.Id == e.Task.Id);
                    if (completedToRemove != null)
                    {
                        CompletedTasks.Remove(completedToRemove);
                    }

                    if (CurrentTask?.Id == e.Task.Id)
                    {
                        CurrentTask = ActiveTasks.FirstOrDefault();
                    }
                    break;
            }

            // Update counts and visibility
            ActiveTasksCount = ActiveTasks.Count;
            IsEmptyStateVisible = ClaudeTasks.Count == 0;

            // Notify computed properties
            OnPropertyChanged(nameof(CurrentTaskProgress));
            OnPropertyChanged(nameof(CurrentTaskElapsed));
            OnPropertyChanged(nameof(StatusText));
        });
    }

    #endregion

    #region Commands

    /// <summary>
    /// Refreshes the task list from the file service and detection service.
    /// Filters by current workspace if set.
    /// </summary>
    [RelayCommand]
    private void RefreshTasks()
    {
        // Clear and reload all tasks
        ClaudeTasks.Clear();
        ActiveTasks.Clear();
        PendingTasks.Clear();
        CompletedTasks.Clear();

        var allTasks = new List<FocusTask>();

        // Primarily load from file service (reads ~/.claude/tasks/)
        if (_claudeTaskFileService != null)
        {
            allTasks.AddRange(_claudeTaskFileService.GetAllTasks());
        }

        // Also include detection-based tasks (from terminal output)
        if (_claudeTaskDetectionService != null)
        {
            var detectedTasks = _claudeTaskDetectionService.GetAllClaudeTasks();
            // Only add detected tasks that aren't already loaded from files
            var existingIds = new HashSet<string>(allTasks.Select(t => t.ClaudeTaskId ?? t.Id));
            foreach (var task in detectedTasks)
            {
                var taskId = task.ClaudeTaskId ?? task.Id;
                if (!existingIds.Contains(taskId))
                {
                    allTasks.Add(task);
                    existingIds.Add(taskId);
                }
            }
        }

        // Filter based on current view settings
        var filteredTasks = allTasks.Where(ShouldShowTask).ToList();

        foreach (var task in filteredTasks)
        {
            ClaudeTasks.Add(task);

            if (task.Status == FocusTaskStatus.InProgress)
            {
                ActiveTasks.Add(task);
            }
            else if (task.Status == FocusTaskStatus.NotStarted)
            {
                PendingTasks.Add(task);
            }
            else if (task.Status == FocusTaskStatus.Completed &&
                     task.CompletedAt?.Date == DateTime.Today)
            {
                CompletedTasks.Add(task);
            }
        }

        CurrentTask = ActiveTasks.FirstOrDefault();
        ActiveTasksCount = ActiveTasks.Count;
        IsEmptyStateVisible = ClaudeTasks.Count == 0;
    }

    /// <summary>
    /// Shows the manual task creation form.
    /// </summary>
    [RelayCommand]
    private void CreateManualTask()
    {
        IsCreateFormVisible = true;
        ManualTaskTitle = "";
    }

    /// <summary>
    /// Creates a manual Claude task.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateTask))]
    private void SubmitManualTask()
    {
        if (string.IsNullOrWhiteSpace(ManualTaskTitle))
            return;

        // Create a new FocusTask and mark it as a Claude task
        var task = _taskService.CreateTask(ManualTaskTitle.Trim());
        task.IsClaudeTask = true;
        task.ClaudeSessionId = Guid.NewGuid().ToString(); // Manual tasks get unique session ID
        task.ClaudeTaskId = $"manual-{Guid.NewGuid()}";
        task.Status = FocusTaskStatus.InProgress;
        task.Start();

        _taskService.UpdateTask(task);

        // Add to collections
        ClaudeTasks.Add(task);
        ActiveTasks.Add(task);
        CurrentTask = task;

        // Reset form
        IsCreateFormVisible = false;
        ManualTaskTitle = "";

        // Update counts
        ActiveTasksCount = ActiveTasks.Count;
        IsEmptyStateVisible = false;
    }

    private bool CanCreateTask() => !string.IsNullOrWhiteSpace(ManualTaskTitle);

    /// <summary>
    /// Cancels manual task creation.
    /// </summary>
    [RelayCommand]
    private void CancelManualTask()
    {
        IsCreateFormVisible = false;
        ManualTaskTitle = "";
    }

    /// <summary>
    /// Completes the selected task.
    /// </summary>
    [RelayCommand]
    private void CompleteTask(FocusTask? task)
    {
        if (task == null)
            return;

        task.Complete();
        _taskService.UpdateTask(task);

        // Move to completed
        var activeTask = ActiveTasks.FirstOrDefault(t => t.Id == task.Id);
        if (activeTask != null)
        {
            ActiveTasks.Remove(activeTask);
        }
        CompletedTasks.Insert(0, task);

        if (CurrentTask?.Id == task.Id)
        {
            CurrentTask = ActiveTasks.FirstOrDefault();
        }

        ActiveTasksCount = ActiveTasks.Count;
    }

    /// <summary>
    /// Deletes the selected task.
    /// </summary>
    [RelayCommand]
    private void DeleteTask(FocusTask? task)
    {
        if (task == null)
            return;

        _taskService.DeleteTask(task.Id);

        var taskToRemove = ClaudeTasks.FirstOrDefault(t => t.Id == task.Id);
        if (taskToRemove != null)
        {
            ClaudeTasks.Remove(taskToRemove);
        }

        var activeToRemove = ActiveTasks.FirstOrDefault(t => t.Id == task.Id);
        if (activeToRemove != null)
        {
            ActiveTasks.Remove(activeToRemove);
        }

        var completedToRemove = CompletedTasks.FirstOrDefault(t => t.Id == task.Id);
        if (completedToRemove != null)
        {
            CompletedTasks.Remove(completedToRemove);
        }

        if (CurrentTask?.Id == task.Id)
        {
            CurrentTask = ActiveTasks.FirstOrDefault();
        }

        ActiveTasksCount = ActiveTasks.Count;
        IsEmptyStateVisible = ClaudeTasks.Count == 0;
    }

    /// <summary>
    /// Views task details.
    /// </summary>
    [RelayCommand]
    private void ViewTaskDetails(FocusTask? task)
    {
        if (task == null)
            return;

        SelectedTask = task;
        // Could open a detail view or expand inline details
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Sets the workspace path to filter tasks by.
    /// </summary>
    public void SetWorkspace(string? workspacePath)
    {
        _currentWorkspacePath = workspacePath;
    }

    /// <summary>
    /// Opens the Claude Tasks panel for a specific workspace.
    /// </summary>
    public void Open(string? workspacePath = null)
    {
        _currentWorkspacePath = workspacePath;
        IsOpen = true;
        OnOpened();
        RequestShow();
    }

    /// <summary>
    /// Called when panel is opened.
    /// </summary>
    public void OnOpened()
    {
        RefreshTasks();
    }

    /// <summary>
    /// Checks if a task should be shown based on current filter settings.
    /// </summary>
    private bool ShouldShowTask(FocusTask task)
    {
        if (ShowGlobalTasks)
            return true;

        if (string.IsNullOrEmpty(_currentWorkspacePath))
            return true;

        return IsTaskForWorkspace(task, _currentWorkspacePath);
    }

    /// <summary>
    /// Checks if a task belongs to the specified workspace.
    /// </summary>
    private static bool IsTaskForWorkspace(FocusTask task, string workspacePath)
    {
        if (task.ProjectPaths == null || task.ProjectPaths.Count == 0)
            return true; // Tasks without project paths are shown everywhere

        var normalizedWorkspace = System.IO.Path.GetFullPath(workspacePath).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        return task.ProjectPaths.Any(p =>
        {
            var normalizedPath = System.IO.Path.GetFullPath(p).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            return string.Equals(normalizedPath, normalizedWorkspace, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Called when ShowGlobalTasks changes - refresh the task list.
    /// </summary>
    partial void OnShowGlobalTasksChanged(bool value)
    {
        RefreshTasks();
    }

    /// <summary>
    /// Toggles between global and workspace-filtered view.
    /// </summary>
    [RelayCommand]
    private void ToggleFilterMode()
    {
        ShowGlobalTasks = !ShowGlobalTasks;
    }

    /// <summary>
    /// Closes the panel.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        OnClose();
    }

    protected override void OnClose()
    {
        base.OnClose();
    }

    #endregion
}
