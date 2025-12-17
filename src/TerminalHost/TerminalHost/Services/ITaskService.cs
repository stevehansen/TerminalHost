using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for managing focus tasks and quick notes.
/// </summary>
public interface ITaskService
{
    // Task CRUD
    FocusTask CreateTask(string title, string? description = null, string? parentTaskId = null);
    FocusTask? GetTask(string id);
    IReadOnlyList<FocusTask> GetAllTasks();
    IReadOnlyList<FocusTask> GetBacklogTasks();
    IReadOnlyList<FocusTask> GetCompletedTasks(DateTime? since = null);
    IReadOnlyList<FocusTask> GetChildTasks(string parentTaskId);
    void UpdateTask(FocusTask task);
    void DeleteTask(string id);

    // Task state transitions
    void StartTask(string id);
    void CompleteTask(string id);
    void PauseTask(string id);
    void DeferTask(string id);

    // Focus mode
    FocusTask? GetCurrentTask();
    void SetCurrentTask(string? taskId);
    bool IsFocusModeEnabled { get; }
    void ToggleFocusMode();
    void EnableFocusMode(string taskId);
    void DisableFocusMode();

    // Quick notes
    QuickNote CreateNote(string text, string? projectPath = null);
    IReadOnlyList<QuickNote> GetQuickNotes();
    FocusTask ConvertNoteToTask(string noteId);
    void DeleteNote(string noteId);

    // Project association
    void AddProjectToTask(string taskId, string projectPath);
    void RemoveProjectFromTask(string taskId, string projectPath);
    IReadOnlyList<string> GetProjectsForCurrentTask();
    bool IsProjectVisibleInFocusMode(string projectPath);

    // Task history
    IReadOnlyList<string> GetTaskHistory();

    // Events
    event EventHandler<FocusTask?>? CurrentTaskChanged;
    event EventHandler<bool>? FocusModeChanged;
    event EventHandler? TasksChanged;
    event EventHandler? NotesChanged;
}
