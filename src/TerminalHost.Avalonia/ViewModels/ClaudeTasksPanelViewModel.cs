using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Claude Tasks panel - shows Claude Code task activity in real-time.
/// Supports Panel, Popup, and Window display states.
/// Provides manual task creation and real-time progress monitoring.
/// </summary>
public partial class ClaudeTasksPanelViewModel : BasePanelViewModel
{
    private readonly IClaudeTaskDetectionService? _claudeTaskDetectionService;
    private readonly ITaskService _taskService;

    #region IPanelableViewModel Implementation

    public override string PanelId => "claudeTasks";
    public override string PanelTitle => "Claude Tasks";
    public override string PanelIcon => "🤖";
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "➕",
            Tooltip = "Create manual task",
            Command = CreateManualTaskCommand
        },
        new PanelHeaderCommand
        {
            Icon = "↻",
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
    /// Text for manual task creation.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateManualTaskCommand))]
    private string _manualTaskTitle = "";

    /// <summary>
    /// Whether task creation form is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isCreateFormVisible;

    #endregion

    private readonly MainViewModel _mainViewModel;

    public ClaudeTasksPanelViewModel(
        IClaudeTaskDetectionService? claudeTaskDetectionService,
        ITaskService taskService,
        MainViewModel mainViewModel)
    {
        _claudeTaskDetectionService = claudeTaskDetectionService;
        _taskService = taskService;
        _mainViewModel = mainViewModel;

        // Subscribe to Claude task events if service is available
        if (_claudeTaskDetectionService != null)
        {
            _claudeTaskDetectionService.ClaudeTaskChanged += OnClaudeTaskChanged;
        }

        // Subscribe to open panel requests
        _mainViewModel.ClaudeTasksPanelRequested += (s, e) => Open();
    }

    #region Event Handlers

    /// <summary>
    /// Handles Claude task changes (created, updated, completed, deleted).
    /// </summary>
    private void OnClaudeTaskChanged(object? sender, ClaudeTaskEventArgs e)
    {
        // Marshal to UI thread if needed (platform-specific dispatcher will be used by views)
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
                ActiveTasks.Remove(e.Task);
                CompletedTasks.Insert(0, e.Task); // Add to top of completed list

                // Update current task to next active task
                if (CurrentTask?.Id == e.Task.Id)
                {
                    CurrentTask = ActiveTasks.FirstOrDefault();
                }
                break;

            case ClaudeTaskEventType.Deleted:
                ClaudeTasks.Remove(e.Task);
                ActiveTasks.Remove(e.Task);
                CompletedTasks.Remove(e.Task);

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
    }

    #endregion

    #region Commands

    /// <summary>
    /// Refreshes the task list from the detection service.
    /// </summary>
    [RelayCommand]
    private void RefreshTasks()
    {
        if (_claudeTaskDetectionService == null)
            return;

        // Clear and reload all tasks
        ClaudeTasks.Clear();
        ActiveTasks.Clear();
        CompletedTasks.Clear();

        var allTasks = _claudeTaskDetectionService.GetAllClaudeTasks();
        foreach (var task in allTasks)
        {
            ClaudeTasks.Add(task);

            if (task.Status == FocusTaskStatus.InProgress)
            {
                ActiveTasks.Add(task);
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
        ActiveTasks.Remove(task);
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

        ClaudeTasks.Remove(task);
        ActiveTasks.Remove(task);
        CompletedTasks.Remove(task);

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
    /// Opens the Claude Tasks panel.
    /// </summary>
    public void Open()
    {
        IsOpen = true;
        OnOpened();
    }

    /// <summary>
    /// Called when panel is opened.
    /// </summary>
    public void OnOpened()
    {
        RefreshTasks();
    }

    /// <summary>
    /// Called when panel is closed.
    /// </summary>
    public void OnClosed()
    {
        // Cleanup if needed
    }

    #endregion
}
