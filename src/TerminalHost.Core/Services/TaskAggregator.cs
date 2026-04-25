using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class TaskAggregator : ITaskAggregator, IDisposable
{
    private readonly ITaskService _taskService;
    private readonly IClaudeTaskFileService? _fileService;
    private readonly IClaudeTaskDetectionService? _detectionService;

    private readonly EventHandler _onTaskServiceChanged;
    private readonly EventHandler _onFileServiceChanged;
    private readonly EventHandler<ClaudeTaskEventArgs> _onDetectionChanged;

    public event EventHandler? Changed;

    public TaskAggregator(
        ITaskService taskService,
        IClaudeTaskFileService? fileService = null,
        IClaudeTaskDetectionService? detectionService = null)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _fileService = fileService;
        _detectionService = detectionService;

        _onTaskServiceChanged = (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _onFileServiceChanged = (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _onDetectionChanged = (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        _taskService.TasksChanged += _onTaskServiceChanged;
        if (_fileService != null) _fileService.TasksChanged += _onFileServiceChanged;
        if (_detectionService != null) _detectionService.ClaudeTaskChanged += _onDetectionChanged;
    }

    public IReadOnlyList<FocusTask> GetAll()
    {
        return Merge(manualFilter: _ => true, claudeFilter: _ => true).ToList();
    }

    public IReadOnlyList<FocusTask> GetForWorkspace(string workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath))
            return Array.Empty<FocusTask>();

        var normalized = NormalizeWorkspacePath(workspacePath);

        return Merge(
            manualFilter: t => MatchesWorkspaceExplicit(t, normalized),
            claudeFilter: t => MatchesWorkspaceOrUnscoped(t, normalized)
        ).ToList();
    }

    private IEnumerable<FocusTask> Merge(
        Func<FocusTask, bool> manualFilter,
        Func<FocusTask, bool> claudeFilter)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in _taskService.GetAllTasks())
        {
            if (!manualFilter(task)) continue;
            if (seen.Add(IdentityKey(task))) yield return task;
        }

        if (_fileService != null)
        {
            foreach (var task in _fileService.GetAllTasks())
            {
                if (!claudeFilter(task)) continue;
                if (seen.Add(IdentityKey(task))) yield return task;
            }
        }

        if (_detectionService != null)
        {
            foreach (var task in _detectionService.GetAllClaudeTasks())
            {
                if (!claudeFilter(task)) continue;
                if (seen.Add(IdentityKey(task))) yield return task;
            }
        }
    }

    /// <summary>
    /// Canonical dedup identity. Disambiguates by ClaudeSessionId when present —
    /// the same ClaudeTaskId can recur across sessions and must remain distinct.
    /// </summary>
    internal static string IdentityKey(FocusTask task)
    {
        if (!string.IsNullOrEmpty(task.ClaudeTaskId) && !string.IsNullOrEmpty(task.ClaudeSessionId))
            return task.ClaudeSessionId + ":" + task.ClaudeTaskId;
        if (!string.IsNullOrEmpty(task.ClaudeTaskId))
            return task.ClaudeTaskId;
        return task.Id;
    }

    private static bool MatchesWorkspaceExplicit(FocusTask task, string normalizedWorkspace)
    {
        if (task.ProjectPaths == null || task.ProjectPaths.Count == 0) return false;
        return task.ProjectPaths.Any(p => NormalizeWorkspacePath(p) == normalizedWorkspace);
    }

    private static bool MatchesWorkspaceOrUnscoped(FocusTask task, string normalizedWorkspace)
    {
        if (task.ProjectPaths == null || task.ProjectPaths.Count == 0) return true;
        return task.ProjectPaths.Any(p => NormalizeWorkspacePath(p) == normalizedWorkspace);
    }

    internal static string NormalizeWorkspacePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
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

    public void Dispose()
    {
        _taskService.TasksChanged -= _onTaskServiceChanged;
        if (_fileService != null) _fileService.TasksChanged -= _onFileServiceChanged;
        if (_detectionService != null) _detectionService.ClaudeTaskChanged -= _onDetectionChanged;
    }
}
