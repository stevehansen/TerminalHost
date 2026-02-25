using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for reading persisted Claude Code tasks from ~/.claude/tasks/.
///
/// Reads from: ~/.claude/tasks/{session-id}/{task-id}.json
///   - Individual JSON files per task with {id, subject, description, activeForm, status, blocks, blockedBy}
///   - Created by Claude Code's TaskCreate/TaskUpdate tools
///
/// Uses IClaudeSessionIndexService for session→project path mapping.
/// </summary>
public sealed class ClaudeTaskFileService : IClaudeTaskFileService, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IClaudeSessionIndexService _sessionIndexService;
    private readonly string _tasksRootPath;
    private readonly string _claudeRootPath;
    private Dictionary<string, List<FocusTask>> _sessionTasksCache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _tasksFileWatcher;
    private FileSystemWatcher? _directoryWatcher;
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceTokenSource;
    private readonly object _debounceLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Sessions with no file updates within this duration are considered inactive.
    /// In-progress and pending tasks from inactive sessions are hidden since those
    /// sessions ended without cleaning up their task files.
    /// </summary>
    private static readonly TimeSpan SessionStaleThreshold = TimeSpan.FromMinutes(10);

    // Pattern to strip ANSI escape codes from text (cursor movement, colors, etc.)
    private static readonly Regex AnsiEscapePattern = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Strips ANSI escape codes from text.
    /// </summary>
    private static string StripAnsiCodes(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        return AnsiEscapePattern.Replace(text, string.Empty);
    }

    public event EventHandler? TasksChanged;

    public ClaudeTaskFileService(IFileSystem fileSystem, IClaudeSessionIndexService sessionIndexService)
    {
        _fileSystem = fileSystem;
        _sessionIndexService = sessionIndexService;

        // Paths: ~/.claude/ and ~/.claude/tasks/
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _claudeRootPath = Path.Combine(userProfile, ".claude");
        _tasksRootPath = Path.Combine(_claudeRootPath, "tasks");

        // Load tasks on startup
        Refresh();

        // Start watching for changes
        StartFileWatcher();

        // Subscribe to session index changes
        _sessionIndexService.SessionsChanged += (_, _) => DebounceRefresh();
    }

    public IReadOnlyList<FocusTask> GetSessionTasks(string sessionId)
    {
        // Grab cache reference (Refresh swaps the whole dictionary, so this snapshot is safe)
        Dictionary<string, List<FocusTask>> cache;
        lock (_lock) { cache = _sessionTasksCache; }

        return cache.TryGetValue(sessionId, out var tasks)
            ? tasks.AsReadOnly()
            : Array.Empty<FocusTask>();
    }

    public IReadOnlyList<FocusTask> GetAllTasks()
    {
        // Grab cache reference (Refresh swaps the whole dictionary, so this snapshot is safe)
        Dictionary<string, List<FocusTask>> cache;
        lock (_lock) { cache = _sessionTasksCache; }

        var allTasks = new List<FocusTask>();
        foreach (var sessionTasks in cache.Values)
        {
            allTasks.AddRange(sessionTasks);
        }
        return allTasks.AsReadOnly();
    }

    public IReadOnlyList<string> GetActiveSessions()
    {
        if (!_fileSystem.DirectoryExists(_tasksRootPath))
            return Array.Empty<string>();

        try
        {
            return _fileSystem.GetDirectories(_tasksRootPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList()!;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Refresh()
    {
        // Build new cache outside the lock (all I/O happens here)
        var newCache = new Dictionary<string, List<FocusTask>>(StringComparer.OrdinalIgnoreCase);

        // Load from tasks/ directory (nested format from TaskCreate tool)
        LoadTasksDirectory(newCache);

        // Brief lock to swap cache
        lock (_lock)
        {
            _sessionTasksCache = newCache;
        }

        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Loads tasks from ~/.claude/tasks/{session-id}/{task-id}.json files.
    /// These are created by Claude Code's TaskCreate/TaskUpdate tools.
    /// </summary>
    private void LoadTasksDirectory(Dictionary<string, List<FocusTask>> cache)
    {
        if (!_fileSystem.DirectoryExists(_tasksRootPath))
            return;

        try
        {
            var sessionDirs = _fileSystem.GetDirectories(_tasksRootPath);

            foreach (var sessionDir in sessionDirs)
            {
                var sessionId = Path.GetFileName(sessionDir);
                if (string.IsNullOrEmpty(sessionId))
                    continue;

                try
                {
                    var jsonFiles = _fileSystem.GetFiles(sessionDir, "*.json", SearchOption.TopDirectoryOnly);
                    if (jsonFiles.Length == 0)
                        continue;

                    // Get project path for this session
                    var projectPaths = new List<string>();
                    var projectPath = _sessionIndexService.GetProjectPathForSession(sessionId);
                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        projectPaths.Add(projectPath);
                    }

                    var tasks = new List<FocusTask>();
                    var mostRecentWrite = DateTime.MinValue;

                    foreach (var filePath in jsonFiles)
                    {
                        try
                        {
                            var json = _fileSystem.ReadAllText(filePath);
                            var taskDto = JsonSerializer.Deserialize<ClaudeTaskDto>(json, JsonOptions);

                            if (taskDto != null)
                            {
                                var task = MapTaskDtoToFocusTask(taskDto, sessionId, filePath, projectPaths);
                                tasks.Add(task);

                                // Track most recent file modification
                                try
                                {
                                    var writeTime = File.GetLastWriteTimeUtc(filePath);
                                    if (writeTime > mostRecentWrite)
                                        mostRecentWrite = writeTime;
                                }
                                catch { }
                            }
                        }
                        catch
                        {
                            // Skip invalid task files
                        }
                    }

                    // If session task files haven't been modified recently, the session is inactive.
                    // Filter out in-progress and pending tasks (they'll never complete).
                    if (mostRecentWrite != DateTime.MinValue &&
                        DateTime.UtcNow - mostRecentWrite > SessionStaleThreshold)
                    {
                        tasks = tasks.Where(t => t.Status == FocusTaskStatus.Completed).ToList();
                    }

                    if (tasks.Count > 0)
                    {
                        // Sort by task ID (numeric)
                        tasks = tasks.OrderBy(t => int.TryParse(t.ClaudeTaskId, out var n) ? n : int.MaxValue).ToList();
                        cache[sessionId] = tasks;
                    }
                }
                catch
                {
                    // Skip sessions that fail to load
                }
            }
        }
        catch
        {
            // Continue with whatever was loaded
        }
    }

    /// <summary>
    /// Maps a TaskCreate-style DTO to a FocusTask.
    /// </summary>
    private FocusTask MapTaskDtoToFocusTask(ClaudeTaskDto dto, string sessionId, string filePath, List<string> projectPaths)
    {
        // Generate consistent Guid from session + task ID
        var guidSeed = $"{sessionId}:{dto.Id}";
        var guid = GenerateGuidFromString(guidSeed);

        // Parse status
        var status = dto.Status?.ToLowerInvariant() switch
        {
            "completed" => FocusTaskStatus.Completed,
            "in_progress" or "in-progress" => FocusTaskStatus.InProgress,
            "pending" or "not_started" or "not-started" => FocusTaskStatus.NotStarted,
            _ => FocusTaskStatus.NotStarted
        };

        // Get file creation time
        DateTime createdAt;
        try
        {
            createdAt = File.GetCreationTimeUtc(filePath);
        }
        catch
        {
            createdAt = DateTime.UtcNow;
        }

        return new FocusTask
        {
            Id = guid.ToString(),
            Title = StripAnsiCodes(dto.Subject) is { Length: > 0 } subject ? subject : $"Task {dto.Id}",
            Description = StripAnsiCodes(dto.Description),
            ActiveForm = StripAnsiCodes(dto.ActiveForm),
            Status = status,
            Priority = 0,
            ProjectPaths = new List<string>(projectPaths),
            CreatedAt = createdAt,
            StartedAt = status == FocusTaskStatus.InProgress ? createdAt : null,
            CompletedAt = status == FocusTaskStatus.Completed ? createdAt : null,
            IsClaudeTask = true,
            ClaudeTaskId = dto.Id,
            ClaudeSessionId = sessionId,
            Blocks = dto.Blocks ?? [],
            BlockedBy = dto.BlockedBy ?? []
        };
    }

    /// <summary>
    /// Generates a deterministic Guid from a string.
    /// </summary>
    private static Guid GenerateGuidFromString(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private void StartFileWatcher()
    {
        // Watch for file changes in tasks/ directory
        if (_fileSystem.DirectoryExists(_tasksRootPath))
        {
            try
            {
                _tasksFileWatcher = new FileSystemWatcher(_tasksRootPath, "*.json")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                _tasksFileWatcher.Created += OnFileChanged;
                _tasksFileWatcher.Changed += OnFileChanged;
                _tasksFileWatcher.Deleted += OnFileChanged;
            }
            catch
            {
                // File watcher not critical - continue without it
            }
        }

        // Watch for tasks/ directory being created in ~/.claude/
        if (_fileSystem.DirectoryExists(_claudeRootPath))
        {
            try
            {
                _directoryWatcher = new FileSystemWatcher(_claudeRootPath)
                {
                    NotifyFilter = NotifyFilters.DirectoryName,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                _directoryWatcher.Created += OnDirectoryChanged;
            }
            catch
            {
                // Directory watcher not critical - continue without it
            }
        }
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        DebounceRefresh();
    }

    private void OnDirectoryChanged(object? sender, FileSystemEventArgs e)
    {
        // If tasks/ directory was created, start the tasks file watcher
        if (e.FullPath.Equals(_tasksRootPath, StringComparison.OrdinalIgnoreCase) && _tasksFileWatcher == null)
        {
            StartFileWatcher();
            DebounceRefresh();
        }
        // If a new session subdirectory was created under tasks/
        else if (e.FullPath.StartsWith(_tasksRootPath, StringComparison.OrdinalIgnoreCase))
        {
            DebounceRefresh();
        }
    }

    /// <summary>
    /// Debounces refresh calls to batch multiple rapid file changes.
    /// </summary>
    private void DebounceRefresh()
    {
        lock (_debounceLock)
        {
            // Cancel any pending refresh
            _debounceTokenSource?.Cancel();
            _debounceTokenSource?.Dispose();
            _debounceTokenSource = new CancellationTokenSource();

            var token = _debounceTokenSource.Token;

            // Schedule refresh after 300ms debounce
            Task.Delay(300, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Refresh();
                }
            }, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        _tasksFileWatcher?.Dispose();
        _directoryWatcher?.Dispose();
        _debounceTokenSource?.Cancel();
        _debounceTokenSource?.Dispose();
    }

    /// <summary>
    /// DTO for deserializing Claude Code TaskCreate-style task files.
    /// Matches the structure of ~/.claude/tasks/{session-id}/{task-id}.json files.
    /// </summary>
    private sealed class ClaudeTaskDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("activeForm")]
        public string? ActiveForm { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("blocks")]
        public List<string>? Blocks { get; set; }

        [JsonPropertyName("blockedBy")]
        public List<string>? BlockedBy { get; set; }
    }
}
