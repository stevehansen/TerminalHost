using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Detects Claude Code tasks from terminal output and creates corresponding FocusTask entries.
/// Uses regex patterns to parse task creation, progress updates, and completion messages.
/// </summary>
public class ClaudeTaskDetectionService : IClaudeTaskDetectionService
{
    private readonly ITaskService _taskService;
    private readonly object _lock = new();

    // Track Claude tasks by their ID
    private readonly Dictionary<string, FocusTask> _claudeTasksById = new();

    // Track which tasks belong to which terminal session
    private readonly Dictionary<Guid, List<string>> _sessionTasks = new();

    // Track active session IDs
    private readonly HashSet<Guid> _activeSessions = new();

    // Regex patterns for detecting Claude task output
    // NOTE: These patterns are initial estimates and may need tuning based on actual Claude CLI output
    private static readonly Regex TaskCreatePattern = new(
        @"(?:Creating|Starting|New)\s+task:?\s*[""']?(?<subject>.+?)[""']?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex TaskWithDescriptionPattern = new(
        @"^\s*Description:?\s+(?<description>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex TaskProgressPattern = new(
        @"(?<activeForm>(?:Adding|Building|Checking|Cleaning|Compiling|Creating|Debugging|Deleting|Deploying|Downloading|Executing|Extracting|Fetching|Fixing|Formatting|Generating|Implementing|Installing|Loading|Modifying|Optimizing|Parsing|Processing|Refactoring|Removing|Running|Saving|Scanning|Searching|Testing|Updating|Uploading|Validating|Verifying|Writing)\s+.+?)\.\.\.",
        RegexOptions.Compiled
    );

    private static readonly Regex TaskCompletePattern = new(
        @"(?:Completed|Finished|Done):?\s*[""']?(?<subject>.+?)[""']?\s*(?:\((?<elapsed>[\dhms\s]+)\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Pattern for "Task 1 of 3" style progress indicators
    private static readonly Regex TaskCountPattern = new(
        @"(?:Task|Step)\s+(?<current>\d+)\s+of\s+(?<total>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Pattern to strip ANSI escape codes from terminal output
    private static readonly Regex AnsiEscapePattern = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])",
        RegexOptions.Compiled
    );

    public event EventHandler<ClaudeTaskEventArgs>? ClaudeTaskChanged;

    /// <summary>
    /// Strips ANSI escape codes from terminal output.
    /// </summary>
    private static string StripAnsiCodes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return AnsiEscapePattern.Replace(text, string.Empty);
    }

    public ClaudeTaskDetectionService(ITaskService taskService)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
    }

    public void StartMonitoring(Guid sessionId)
    {
        lock (_lock)
        {
            _activeSessions.Add(sessionId);
            if (!_sessionTasks.ContainsKey(sessionId))
            {
                _sessionTasks[sessionId] = new List<string>();
            }
        }
    }

    public void StopMonitoring(Guid sessionId)
    {
        lock (_lock)
        {
            _activeSessions.Remove(sessionId);
        }
    }

    public void ProcessOutput(string line, Guid sessionId)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        // Strip ANSI escape codes from terminal output
        line = StripAnsiCodes(line);

        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_lock)
        {
            // Skip if not actively monitoring this session
            if (!_activeSessions.Contains(sessionId))
                return;

            // Try to detect task creation
            var createMatch = TaskCreatePattern.Match(line);
            if (createMatch.Success)
            {
                var subject = StripAnsiCodes(createMatch.Groups["subject"].Value.Trim());
                CreateClaudeTask(subject, sessionId);
                return;
            }

            // Try to detect task completion
            var completeMatch = TaskCompletePattern.Match(line);
            if (completeMatch.Success)
            {
                var subject = completeMatch.Groups["subject"].Value.Trim();
                var elapsed = completeMatch.Groups["elapsed"].Success
                    ? completeMatch.Groups["elapsed"].Value.Trim()
                    : null;
                CompleteClaudeTask(subject, sessionId, elapsed);
                return;
            }

            // Try to detect in-progress task (with "..." ellipsis)
            var progressMatch = TaskProgressPattern.Match(line);
            if (progressMatch.Success)
            {
                var activeForm = progressMatch.Groups["activeForm"].Value.Trim();
                UpdateClaudeTaskProgress(activeForm, sessionId);
                return;
            }

            // Try to detect "Task X of Y" patterns
            var countMatch = TaskCountPattern.Match(line);
            if (countMatch.Success)
            {
                // This could be used for progress indication in the future
                // For now, just log or track it
            }
        }
    }

    public IReadOnlyList<FocusTask> GetClaudeTasks(Guid sessionId)
    {
        lock (_lock)
        {
            if (!_sessionTasks.TryGetValue(sessionId, out var taskIds))
                return Array.Empty<FocusTask>();

            return taskIds
                .Select(id => _claudeTasksById.TryGetValue(id, out var task) ? task : null)
                .Where(t => t != null)
                .ToList()!;
        }
    }

    public IReadOnlyList<FocusTask> GetAllClaudeTasks()
    {
        lock (_lock)
        {
            return _claudeTasksById.Values.ToList();
        }
    }

    public void CleanupSession(Guid sessionId)
    {
        lock (_lock)
        {
            if (!_sessionTasks.TryGetValue(sessionId, out var taskIds))
                return;

            // Delete all tasks associated with this session
            foreach (var taskId in taskIds.ToList())
            {
                if (_claudeTasksById.TryGetValue(taskId, out var task))
                {
                    _taskService.DeleteTask(taskId);
                    _claudeTasksById.Remove(taskId);

                    ClaudeTaskChanged?.Invoke(this, new ClaudeTaskEventArgs
                    {
                        Task = task,
                        EventType = ClaudeTaskEventType.Deleted,
                        SessionId = sessionId
                    });
                }
            }

            _sessionTasks.Remove(sessionId);
            _activeSessions.Remove(sessionId);
        }
    }

    private void CreateClaudeTask(string subject, Guid sessionId)
    {
        // Validate subject - reject invalid titles (too short, only punctuation/whitespace, etc.)
        if (string.IsNullOrWhiteSpace(subject) ||
            subject.Length < 3 ||
            !subject.Any(char.IsLetterOrDigit))
        {
            return; // Skip creating task with invalid title
        }

        // Create a unique task using the service
        var task = _taskService.CreateTask(subject);
        task.IsClaudeTask = true;
        task.ClaudeSessionId = sessionId.ToString();
        task.Status = FocusTaskStatus.InProgress;
        task.Start();

        // Generate a Claude task ID based on the subject (for matching later)
        task.ClaudeTaskId = GenerateClaudeTaskId(subject);

        // Update task with Claude-specific properties
        _taskService.UpdateTask(task);

        // Track internally
        _claudeTasksById[task.Id] = task;

        if (!_sessionTasks.ContainsKey(sessionId))
            _sessionTasks[sessionId] = new List<string>();
        _sessionTasks[sessionId].Add(task.Id);

        // Notify listeners
        ClaudeTaskChanged?.Invoke(this, new ClaudeTaskEventArgs
        {
            Task = task,
            EventType = ClaudeTaskEventType.Created,
            SessionId = sessionId
        });
    }

    private void UpdateClaudeTaskProgress(string activeForm, Guid sessionId)
    {
        // Find the most recent in-progress task for this session
        if (!_sessionTasks.TryGetValue(sessionId, out var taskIds))
            return;

        var inProgressTask = taskIds
            .Select(id => _claudeTasksById.TryGetValue(id, out var t) ? t : null)
            .Where(t => t != null && t.Status == FocusTaskStatus.InProgress)
            .OrderByDescending(t => t.StartedAt)
            .FirstOrDefault();

        if (inProgressTask == null)
        {
            // No in-progress task, create a new one
            CreateClaudeTask(activeForm, sessionId);
            return;
        }

        // Update the active form if it's different
        if (inProgressTask.ActiveForm != activeForm)
        {
            inProgressTask.ActiveForm = activeForm;
            _taskService.UpdateTask(inProgressTask);

            ClaudeTaskChanged?.Invoke(this, new ClaudeTaskEventArgs
            {
                Task = inProgressTask,
                EventType = ClaudeTaskEventType.Updated,
                SessionId = sessionId
            });
        }
    }

    private void CompleteClaudeTask(string subject, Guid sessionId, string? elapsed)
    {
        // Find a matching task by subject or Claude task ID
        if (!_sessionTasks.TryGetValue(sessionId, out var taskIds))
            return;

        var claudeTaskId = GenerateClaudeTaskId(subject);

        var matchingTask = taskIds
            .Select(id => _claudeTasksById.TryGetValue(id, out var t) ? t : null)
            .Where(t => t != null && t.Status == FocusTaskStatus.InProgress)
            .FirstOrDefault(t =>
                t.ClaudeTaskId == claudeTaskId ||
                t.Title.Equals(subject, StringComparison.OrdinalIgnoreCase));

        if (matchingTask == null)
        {
            // No matching task found, might have missed the creation
            // Create and immediately complete it
            CreateClaudeTask(subject, sessionId);
            matchingTask = _claudeTasksById.Values
                .FirstOrDefault(t => t.ClaudeTaskId == claudeTaskId);
        }

        if (matchingTask != null)
        {
            matchingTask.Complete();

            // Update with elapsed time if provided (for display purposes)
            if (!string.IsNullOrEmpty(elapsed))
            {
                matchingTask.Notes = $"Completed in {elapsed}";
            }

            _taskService.UpdateTask(matchingTask);

            ClaudeTaskChanged?.Invoke(this, new ClaudeTaskEventArgs
            {
                Task = matchingTask,
                EventType = ClaudeTaskEventType.Completed,
                SessionId = sessionId
            });

            // Optionally, remove from tracking after a delay (cleanup completed tasks)
            // For now, keep them until session cleanup
        }
    }

    /// <summary>
    /// Generate a normalized task ID from a subject string for matching.
    /// </summary>
    private static string GenerateClaudeTaskId(string subject)
    {
        // Normalize: lowercase, remove punctuation, collapse spaces
        var normalized = Regex.Replace(subject.ToLowerInvariant(), @"[^\w\s]", "");
        normalized = Regex.Replace(normalized, @"\s+", "-");
        return $"claude-{normalized}";
    }
}
