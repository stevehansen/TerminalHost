using System.IO;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for managing focus tasks and quick notes.
/// Persists data through ConfigurationService.
/// </summary>
internal sealed class TaskService : ITaskService
{
    private readonly IConfigurationService _configService;
    private readonly IGitPrService _gitPrService;
    private readonly object _lock = new();

    // Cached data (loaded from config)
    private List<FocusTask> _tasks = [];
    private List<QuickNote> _notes = [];
    private FocusModeState _focusMode = new();

    public TaskService(IConfigurationService configService, IGitPrService gitPrService)
    {
        _configService = configService;
        _gitPrService = gitPrService;
        LoadFromConfig();
    }

    #region Events

    public event EventHandler<FocusTask?>? CurrentTaskChanged;
    public event EventHandler<bool>? FocusModeChanged;
    public event EventHandler? TasksChanged;
    public event EventHandler? NotesChanged;

    private void OnCurrentTaskChanged(FocusTask? task) =>
        CurrentTaskChanged?.Invoke(this, task);

    private void OnFocusModeChanged(bool enabled) =>
        FocusModeChanged?.Invoke(this, enabled);

    private void OnTasksChanged() =>
        TasksChanged?.Invoke(this, EventArgs.Empty);

    private void OnNotesChanged() =>
        NotesChanged?.Invoke(this, EventArgs.Empty);

    #endregion

    #region Data Loading/Saving

    private void LoadFromConfig()
    {
        var config = _configService.Load();
        _tasks = config.Tasks ?? [];
        _notes = config.QuickNotes ?? [];
        _focusMode = config.FocusMode ?? new FocusModeState();
    }

    private void SaveToConfig()
    {
        var config = _configService.Load();
        config.Tasks = _tasks;
        config.QuickNotes = _notes;
        config.FocusMode = _focusMode;
        _configService.Save(config);
    }

    #endregion

    #region Task CRUD

    public FocusTask CreateTask(string title, string? description = null, string? parentTaskId = null)
    {
        lock (_lock)
        {
            var task = FocusTask.Create(title, description, parentTaskId);
            _tasks.Add(task);
            SaveToConfig();
            OnTasksChanged();
            return task;
        }
    }

    public FocusTask? GetTask(string id)
    {
        lock (_lock)
        {
            return _tasks.FirstOrDefault(t => t.Id == id);
        }
    }

    public IReadOnlyList<FocusTask> GetAllTasks()
    {
        lock (_lock)
        {
            return _tasks.ToList();
        }
    }

    public IReadOnlyList<FocusTask> GetBacklogTasks()
    {
        lock (_lock)
        {
            return _tasks
                .Where(t => t.Status == FocusTaskStatus.NotStarted || t.Status == FocusTaskStatus.Deferred)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAt)
                .ToList();
        }
    }

    public IReadOnlyList<FocusTask> GetCompletedTasks(DateTime? since = null)
    {
        lock (_lock)
        {
            var query = _tasks.Where(t => t.Status == FocusTaskStatus.Completed);

            if (since.HasValue)
            {
                query = query.Where(t => t.CompletedAt >= since.Value);
            }

            return query
                .OrderByDescending(t => t.CompletedAt)
                .ToList();
        }
    }

    public IReadOnlyList<FocusTask> GetChildTasks(string parentTaskId)
    {
        lock (_lock)
        {
            return _tasks
                .Where(t => t.ParentTaskId == parentTaskId)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAt)
                .ToList();
        }
    }

    public void UpdateTask(FocusTask task)
    {
        lock (_lock)
        {
            var index = _tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0)
            {
                // Prevent circular parent references
                if (!string.IsNullOrEmpty(task.ParentTaskId))
                {
                    if (WouldCreateCycle(task.Id, task.ParentTaskId))
                    {
                        throw new InvalidOperationException("Cannot set parent: would create a cycle");
                    }
                }

                _tasks[index] = task;
                SaveToConfig();
                OnTasksChanged();

                // If this is the current task, notify
                if (_focusMode.CurrentTaskId == task.Id)
                {
                    OnCurrentTaskChanged(task);
                }
            }
        }
    }

    public void DeleteTask(string id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            // If deleting current task, clear focus
            if (_focusMode.CurrentTaskId == id)
            {
                _focusMode.CurrentTaskId = null;
                OnCurrentTaskChanged(null);
            }

            // Remove from history
            _focusMode.TaskHistory.Remove(id);

            // Orphan any child tasks (make them root level)
            foreach (var child in _tasks.Where(t => t.ParentTaskId == id))
            {
                child.ParentTaskId = task.ParentTaskId; // Move to grandparent or root
            }

            _tasks.Remove(task);
            SaveToConfig();
            OnTasksChanged();
        }
    }

    private bool WouldCreateCycle(string taskId, string proposedParentId)
    {
        // Walk up the parent chain from proposedParent
        // If we ever reach taskId, it would be a cycle
        var currentId = proposedParentId;
        var visited = new HashSet<string>();

        while (!string.IsNullOrEmpty(currentId))
        {
            if (currentId == taskId) return true;
            if (!visited.Add(currentId)) return true; // Already visited = existing cycle

            var parent = _tasks.FirstOrDefault(t => t.Id == currentId);
            currentId = parent?.ParentTaskId;
        }

        return false;
    }

    #endregion

    #region Task State Transitions

    public void StartTask(string id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            task.Start();

            // Set as current task
            _focusMode.CurrentTaskId = id;
            _focusMode.AddToHistory(id);

            SaveToConfig();
            OnTasksChanged();
            OnCurrentTaskChanged(task);
        }
    }

    public void CompleteTask(string id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            task.Complete();

            // If this was the current task, try to switch to parent or clear
            if (_focusMode.CurrentTaskId == id)
            {
                var nextTaskId = task.ParentTaskId;

                // If parent exists and is not completed, switch to it
                if (!string.IsNullOrEmpty(nextTaskId))
                {
                    var parent = _tasks.FirstOrDefault(t => t.Id == nextTaskId);
                    if (parent != null && parent.Status != FocusTaskStatus.Completed)
                    {
                        _focusMode.CurrentTaskId = nextTaskId;
                        _focusMode.AddToHistory(nextTaskId);
                        SaveToConfig();
                        OnTasksChanged();
                        OnCurrentTaskChanged(parent);
                        return;
                    }
                }

                // Otherwise clear current task
                _focusMode.CurrentTaskId = null;
                OnCurrentTaskChanged(null);
            }

            SaveToConfig();
            OnTasksChanged();
        }
    }

    public void PauseTask(string id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            // Keep status as InProgress but clear as current task
            if (_focusMode.CurrentTaskId == id)
            {
                _focusMode.CurrentTaskId = null;
                SaveToConfig();
                OnCurrentTaskChanged(null);
            }
        }
    }

    public void DeferTask(string id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            task.Defer();

            // If this was current task, clear it
            if (_focusMode.CurrentTaskId == id)
            {
                _focusMode.CurrentTaskId = null;
                OnCurrentTaskChanged(null);
            }

            SaveToConfig();
            OnTasksChanged();
        }
    }

    #endregion

    #region Focus Mode

    public FocusTask? GetCurrentTask()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_focusMode.CurrentTaskId))
                return null;

            return _tasks.FirstOrDefault(t => t.Id == _focusMode.CurrentTaskId);
        }
    }

    public void SetCurrentTask(string? taskId)
    {
        lock (_lock)
        {
            if (taskId == _focusMode.CurrentTaskId)
                return;

            _focusMode.CurrentTaskId = taskId;

            FocusTask? task = null;
            if (!string.IsNullOrEmpty(taskId))
            {
                task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task != null)
                {
                    _focusMode.AddToHistory(taskId);

                    // Auto-start if not started
                    if (task.Status == FocusTaskStatus.NotStarted)
                    {
                        task.Start();
                        OnTasksChanged();
                    }
                }
            }

            SaveToConfig();
            OnCurrentTaskChanged(task);
        }
    }

    public bool IsFocusModeEnabled
    {
        get
        {
            lock (_lock)
            {
                return _focusMode.IsEnabled;
            }
        }
    }

    public void ToggleFocusMode()
    {
        lock (_lock)
        {
            _focusMode.IsEnabled = !_focusMode.IsEnabled;
            SaveToConfig();
            OnFocusModeChanged(_focusMode.IsEnabled);
        }
    }

    public void EnableFocusMode(string taskId)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;

            _focusMode.IsEnabled = true;
            _focusMode.CurrentTaskId = taskId;
            _focusMode.AddToHistory(taskId);

            // Auto-start if not started
            if (task.Status == FocusTaskStatus.NotStarted)
            {
                task.Start();
                OnTasksChanged();
            }

            SaveToConfig();
            OnFocusModeChanged(true);
            OnCurrentTaskChanged(task);
        }
    }

    public void DisableFocusMode()
    {
        lock (_lock)
        {
            if (!_focusMode.IsEnabled) return;

            _focusMode.IsEnabled = false;
            SaveToConfig();
            OnFocusModeChanged(false);
        }
    }

    #endregion

    #region Quick Notes

    public QuickNote CreateNote(string text, string? projectPath = null)
    {
        lock (_lock)
        {
            var note = QuickNote.Create(text, projectPath);
            _notes.Add(note);
            SaveToConfig();
            OnNotesChanged();
            return note;
        }
    }

    public IReadOnlyList<QuickNote> GetQuickNotes()
    {
        lock (_lock)
        {
            return _notes
                .Where(n => !n.IsConverted)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();
        }
    }

    public FocusTask ConvertNoteToTask(string noteId)
    {
        lock (_lock)
        {
            var note = _notes.FirstOrDefault(n => n.Id == noteId);
            if (note == null)
            {
                throw new InvalidOperationException($"Note not found: {noteId}");
            }

            // Create task from note
            var task = FocusTask.Create(note.Text);
            if (!string.IsNullOrEmpty(note.ProjectPath))
            {
                task.ProjectPaths.Add(note.ProjectPath);
            }

            _tasks.Add(task);

            // Mark note as converted
            note.ConvertedToTaskId = task.Id;

            SaveToConfig();
            OnTasksChanged();
            OnNotesChanged();

            return task;
        }
    }

    public void DeleteNote(string noteId)
    {
        lock (_lock)
        {
            var note = _notes.FirstOrDefault(n => n.Id == noteId);
            if (note == null) return;

            _notes.Remove(note);
            SaveToConfig();
            OnNotesChanged();
        }
    }

    #endregion

    #region Project Association

    public void AddProjectToTask(string taskId, string projectPath)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;

            var normalizedPath = NormalizePath(projectPath);
            if (!task.ProjectPaths.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
            {
                task.ProjectPaths.Add(normalizedPath);
                SaveToConfig();
                OnTasksChanged();

                if (_focusMode.CurrentTaskId == taskId)
                {
                    OnCurrentTaskChanged(task);
                }
            }
        }
    }

    public void RemoveProjectFromTask(string taskId, string projectPath)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;

            var normalizedPath = NormalizePath(projectPath);
            var toRemove = task.ProjectPaths
                .FirstOrDefault(p => p.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (toRemove != null)
            {
                task.ProjectPaths.Remove(toRemove);
                SaveToConfig();
                OnTasksChanged();

                if (_focusMode.CurrentTaskId == taskId)
                {
                    OnCurrentTaskChanged(task);
                }
            }
        }
    }

    public IReadOnlyList<string> GetProjectsForCurrentTask()
    {
        lock (_lock)
        {
            var task = GetCurrentTask();
            return task?.ProjectPaths ?? [];
        }
    }

    public bool IsProjectVisibleInFocusMode(string projectPath)
    {
        lock (_lock)
        {
            // If focus mode is disabled, all projects are visible
            if (!_focusMode.IsEnabled)
                return true;

            // If no current task, all projects visible
            var task = GetCurrentTask();
            if (task == null)
                return true;

            // If current task has no projects, show all (don't hide everything)
            if (task.ProjectPaths.Count == 0)
                return true;

            // Check if project is in current task's list
            var normalizedPath = NormalizePath(projectPath);
            return task.ProjectPaths.Any(p =>
                p.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    #endregion

    #region Task History

    public IReadOnlyList<string> GetTaskHistory()
    {
        lock (_lock)
        {
            return _focusMode.TaskHistory.ToList();
        }
    }

    #endregion

    #region PR/Branch Integration

    public async Task RefreshTaskPrInfoAsync(string taskId, string projectPath)
    {
        FocusTask? task;
        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == taskId);
        }

        if (task == null) return;

        var updated = await _gitPrService.AutoDetectPrInfoAsync(task, projectPath);

        if (updated)
        {
            lock (_lock)
            {
                SaveToConfig();
            }
            OnTasksChanged();

            if (_focusMode.CurrentTaskId == taskId)
            {
                OnCurrentTaskChanged(task);
            }
        }
    }

    #endregion

    #region Helpers

    private static string NormalizePath(string path)
    {
        // Normalize to consistent format
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    #endregion
}
