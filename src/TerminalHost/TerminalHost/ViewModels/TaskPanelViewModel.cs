using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Task Panel popup.
/// </summary>
public partial class TaskPanelViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private FocusTask? _selectedTask;

    [ObservableProperty]
    private QuickNote? _selectedNote;

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private bool _isAddingTask;

    // Collections
    public ObservableCollection<FocusTask> BacklogTasks { get; } = [];
    public ObservableCollection<FocusTask> CompletedTodayTasks { get; } = [];
    public ObservableCollection<QuickNote> QuickNotes { get; } = [];

    // Focus mode state
    public FocusTask? CurrentTask => _taskService.GetCurrentTask();
    public bool IsFocusModeEnabled => _taskService.IsFocusModeEnabled;

    public string FocusModeButtonText => IsFocusModeEnabled ? "Exit Focus" : "Focus Mode";
    public string FocusModeIcon => IsFocusModeEnabled ? "🎯" : "○";

    // Current task display
    public bool HasCurrentTask => CurrentTask != null;
    public string CurrentTaskTitle => CurrentTask?.Title ?? "No active task";
    public string CurrentTaskElapsed => CurrentTask?.ElapsedTimeDisplay ?? "";
    public string CurrentTaskProjects => CurrentTask != null && CurrentTask.ProjectPaths.Count > 0
        ? string.Join(", ", CurrentTask.ProjectPaths.Select(p => System.IO.Path.GetFileName(p)))
        : "No projects linked";
    public string CurrentTaskNotes => CurrentTask?.Notes ?? "";
    public bool CurrentTaskHasNotes => !string.IsNullOrWhiteSpace(CurrentTask?.Notes);
    public bool CurrentTaskHasPrLink => CurrentTask?.HasPrLink ?? false;
    public string CurrentTaskPrDisplay => CurrentTask?.PrDetails != null
        ? $"PR #{CurrentTask.LinkedPrNumber}: {CurrentTask.PrDetails.Title}"
        : CurrentTask?.LinkedPrNumber != null
            ? $"PR #{CurrentTask.LinkedPrNumber}"
            : CurrentTask?.LinkedBranch ?? "";

    public TaskPanelViewModel(ITaskService taskService, MainViewModel mainViewModel)
    {
        _taskService = taskService;
        _mainViewModel = mainViewModel;

        // Subscribe to task service events
        _taskService.TasksChanged += (s, e) => RefreshTasks();
        _taskService.NotesChanged += (s, e) => RefreshNotes();
        _taskService.CurrentTaskChanged += (s, e) => RefreshCurrentTask();
        _taskService.FocusModeChanged += (s, e) => RefreshFocusMode();

        // Subscribe to main view model events
        _mainViewModel.TaskPanelRequested += (s, e) => Open();
    }

    public void Open()
    {
        RefreshAll();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        IsAddingTask = false;
        NewTaskTitle = string.Empty;
    }

    #region Task Operations

    [RelayCommand]
    private void StartTask(FocusTask task)
    {
        _taskService.StartTask(task.Id);
    }

    [RelayCommand]
    private void CompleteCurrentTask()
    {
        if (CurrentTask != null)
        {
            _taskService.CompleteTask(CurrentTask.Id);
        }
    }

    [RelayCommand]
    private void PauseCurrentTask()
    {
        if (CurrentTask != null)
        {
            _taskService.PauseTask(CurrentTask.Id);
        }
    }

    [RelayCommand]
    private async Task RefreshPrInfo()
    {
        if (CurrentTask == null) return;

        // Use first associated project, or current project if none
        var projectPath = CurrentTask.ProjectPaths.FirstOrDefault();
        if (string.IsNullOrEmpty(projectPath) && _mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            projectPath = terminalTab.Pair.WorkingDirectory;
        }

        if (!string.IsNullOrEmpty(projectPath))
        {
            await _taskService.RefreshTaskPrInfoAsync(CurrentTask.Id, projectPath);
            RefreshCurrentTask();
        }
    }

    [RelayCommand]
    private void AddSubtask()
    {
        if (CurrentTask == null) return;

        IsAddingTask = true;
        // The actual task will be created with CurrentTask.Id as parent
    }

    [RelayCommand]
    private void ShowAddTask()
    {
        IsAddingTask = true;
    }

    [RelayCommand]
    private void CancelAddTask()
    {
        IsAddingTask = false;
        NewTaskTitle = string.Empty;
    }

    [RelayCommand]
    private void CreateTask()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

        var parentId = IsAddingTask && CurrentTask != null ? CurrentTask.Id : null;
        var task = _taskService.CreateTask(NewTaskTitle.Trim(), parentTaskId: parentId);

        // If we have a current project, associate it
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            _taskService.AddProjectToTask(task.Id, terminalTab.Pair.WorkingDirectory);
        }

        NewTaskTitle = string.Empty;
        IsAddingTask = false;
    }

    [RelayCommand]
    private void CreateAndStartTask()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

        var parentId = IsAddingTask && CurrentTask != null ? CurrentTask.Id : null;
        var task = _taskService.CreateTask(NewTaskTitle.Trim(), parentTaskId: parentId);

        // If we have a current project, associate it
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            _taskService.AddProjectToTask(task.Id, terminalTab.Pair.WorkingDirectory);
        }

        _taskService.StartTask(task.Id);

        NewTaskTitle = string.Empty;
        IsAddingTask = false;
    }

    [RelayCommand]
    private void DeleteTask(FocusTask task)
    {
        _taskService.DeleteTask(task.Id);
    }

    [RelayCommand]
    private void DeferTask(FocusTask task)
    {
        _taskService.DeferTask(task.Id);
    }

    #endregion

    #region Quick Note Operations

    [RelayCommand]
    private void ConvertNoteToTask(QuickNote note)
    {
        var task = _taskService.ConvertNoteToTask(note.Id);
        SelectedTask = task;
    }

    [RelayCommand]
    private void DeleteNote(QuickNote note)
    {
        _taskService.DeleteNote(note.Id);
    }

    #endregion

    #region Focus Mode

    [RelayCommand]
    private void ToggleFocusMode()
    {
        if (IsFocusModeEnabled)
        {
            _taskService.DisableFocusMode();
        }
        else if (CurrentTask != null)
        {
            _taskService.EnableFocusMode(CurrentTask.Id);
        }
        else if (SelectedTask != null)
        {
            _taskService.EnableFocusMode(SelectedTask.Id);
        }
    }

    [RelayCommand]
    private void StartTaskWithFocus(FocusTask task)
    {
        _taskService.EnableFocusMode(task.Id);
    }

    #endregion

    #region Project Association

    [RelayCommand]
    private void AddCurrentProjectToTask()
    {
        if (CurrentTask == null) return;
        if (_mainViewModel.SelectedTab is not TerminalPairTabViewModel terminalTab) return;

        _taskService.AddProjectToTask(CurrentTask.Id, terminalTab.Pair.WorkingDirectory);
    }

    [RelayCommand]
    private void RemoveProjectFromTask(string projectPath)
    {
        if (CurrentTask == null) return;
        _taskService.RemoveProjectFromTask(CurrentTask.Id, projectPath);
    }

    #endregion

    #region Refresh Methods

    private void RefreshAll()
    {
        RefreshTasks();
        RefreshNotes();
        RefreshCurrentTask();
        RefreshFocusMode();
    }

    private void RefreshTasks()
    {
        BacklogTasks.Clear();
        foreach (var task in _taskService.GetBacklogTasks())
        {
            BacklogTasks.Add(task);
        }

        CompletedTodayTasks.Clear();
        var today = DateTime.UtcNow.Date;
        foreach (var task in _taskService.GetCompletedTasks(today))
        {
            CompletedTodayTasks.Add(task);
        }

        OnPropertyChanged(nameof(BacklogTasks));
        OnPropertyChanged(nameof(CompletedTodayTasks));
    }

    private void RefreshNotes()
    {
        QuickNotes.Clear();
        foreach (var note in _taskService.GetQuickNotes())
        {
            QuickNotes.Add(note);
        }

        OnPropertyChanged(nameof(QuickNotes));
    }

    private void RefreshCurrentTask()
    {
        OnPropertyChanged(nameof(CurrentTask));
        OnPropertyChanged(nameof(HasCurrentTask));
        OnPropertyChanged(nameof(CurrentTaskTitle));
        OnPropertyChanged(nameof(CurrentTaskElapsed));
        OnPropertyChanged(nameof(CurrentTaskProjects));
        OnPropertyChanged(nameof(CurrentTaskNotes));
        OnPropertyChanged(nameof(CurrentTaskHasNotes));
        OnPropertyChanged(nameof(CurrentTaskHasPrLink));
        OnPropertyChanged(nameof(CurrentTaskPrDisplay));
    }

    private void RefreshFocusMode()
    {
        OnPropertyChanged(nameof(IsFocusModeEnabled));
        OnPropertyChanged(nameof(FocusModeButtonText));
        OnPropertyChanged(nameof(FocusModeIcon));
    }

    #endregion

    #region Search/Filter

    partial void OnSearchTextChanged(string value)
    {
        // Filter tasks based on search text
        RefreshTasks();
    }

    public IEnumerable<FocusTask> FilteredBacklogTasks
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return BacklogTasks;

            var search = SearchText.ToLowerInvariant();
            return BacklogTasks.Where(t =>
                t.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
    }

    #endregion
}
