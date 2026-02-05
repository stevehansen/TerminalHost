using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for reading persisted Claude Code tasks from ~/.claude/tasks/.
/// Each session writes task JSON files to ~/.claude/tasks/{session-id}/*.json.
/// Uses IClaudeSessionIndexService for session→project path mapping.
/// </summary>
public sealed class ClaudeTaskFileService : IClaudeTaskFileService, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IClaudeSessionIndexService _sessionIndexService;
    private readonly string _tasksRootPath;
    private readonly string _claudeRootPath;
    private readonly Dictionary<string, List<FocusTask>> _sessionTasksCache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _fileWatcher;
    private FileSystemWatcher? _directoryWatcher;
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceTokenSource;
    private readonly object _debounceLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        lock (_lock)
        {
            if (_sessionTasksCache.TryGetValue(sessionId, out var tasks))
            {
                return tasks.AsReadOnly();
            }

            // Try loading if not in cache
            var sessionTasks = LoadSessionTasks(sessionId);
            if (sessionTasks.Count > 0)
            {
                _sessionTasksCache[sessionId] = sessionTasks;
                return sessionTasks.AsReadOnly();
            }

            return Array.Empty<FocusTask>();
        }
    }

    public IReadOnlyList<FocusTask> GetAllTasks()
    {
        lock (_lock)
        {
            var allTasks = new List<FocusTask>();
            foreach (var sessionTasks in _sessionTasksCache.Values)
            {
                allTasks.AddRange(sessionTasks);
            }
            return allTasks.AsReadOnly();
        }
    }

    public IReadOnlyList<string> GetActiveSessions()
    {
        if (!_fileSystem.DirectoryExists(_tasksRootPath))
            return Array.Empty<string>();

        try
        {
            var sessionDirs = _fileSystem.GetDirectories(_tasksRootPath);
            return sessionDirs
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
        lock (_lock)
        {
            _sessionTasksCache.Clear();

            var sessions = GetActiveSessions();
            foreach (var sessionId in sessions)
            {
                var tasks = LoadSessionTasks(sessionId);
                if (tasks.Count > 0)
                {
                    _sessionTasksCache[sessionId] = tasks;
                }
            }
        }

        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<FocusTask> LoadSessionTasks(string sessionId)
    {
        var sessionPath = Path.Combine(_tasksRootPath, sessionId);
        if (!_fileSystem.DirectoryExists(sessionPath))
            return [];

        var tasks = new List<FocusTask>();

        try
        {
            var jsonFiles = _fileSystem.GetFiles(sessionPath, "*.json", SearchOption.TopDirectoryOnly);

            foreach (var filePath in jsonFiles)
            {
                try
                {
                    var json = _fileSystem.ReadAllText(filePath);
                    var taskDto = JsonSerializer.Deserialize<ClaudeTaskDto>(json, JsonOptions);

                    if (taskDto != null)
                    {
                        var task = MapToFocusTask(taskDto, sessionId, filePath);
                        tasks.Add(task);
                    }
                }
                catch
                {
                    // Skip invalid task files
                }
            }

            // Sort by ClaudeTaskId (which is the numeric ID from JSON)
            tasks = tasks.OrderBy(t => int.TryParse(t.ClaudeTaskId, out var n) ? n : int.MaxValue).ToList();
        }
        catch
        {
            // Skip sessions that fail to load
        }

        return tasks;
    }

    private FocusTask MapToFocusTask(ClaudeTaskDto dto, string sessionId, string filePath)
    {
        // Generate consistent Guid from session + task ID
        var guidSeed = $"{sessionId}:{dto.Id}";
        var guid = GenerateGuidFromString(guidSeed);

        // Parse status
        var rawStatus = dto.Status;
        var status = rawStatus?.ToLowerInvariant() switch
        {
            "completed" => FocusTaskStatus.Completed,
            "in_progress" or "in-progress" => FocusTaskStatus.InProgress,
            "pending" or "not_started" or "not-started" => FocusTaskStatus.NotStarted,
            _ => FocusTaskStatus.NotStarted
        };

        // Get file creation time for timestamps
        DateTime createdAt;
        try
        {
            createdAt = File.GetCreationTimeUtc(filePath);
        }
        catch
        {
            createdAt = DateTime.UtcNow;
        }

        // Get project path for this session using the session index service
        var projectPaths = new List<string>();
        var projectPath = _sessionIndexService.GetProjectPathForSession(sessionId);
        if (!string.IsNullOrEmpty(projectPath))
        {
            projectPaths.Add(projectPath);
        }

        return new FocusTask
        {
            Id = guid.ToString(),
            Title = StripAnsiCodes(dto.Subject) is { Length: > 0 } subject ? subject : $"Task {dto.Id}",
            Description = StripAnsiCodes(dto.Description),
            ActiveForm = StripAnsiCodes(dto.ActiveForm),
            Status = status,
            Priority = 0, // Not in Claude task files
            ProjectPaths = projectPaths,
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
        // Watch for file changes if tasks directory exists
        if (_fileSystem.DirectoryExists(_tasksRootPath))
        {
            try
            {
                _fileWatcher = new FileSystemWatcher(_tasksRootPath, "*.json")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Created += OnFileChanged;
                _fileWatcher.Changed += OnFileChanged;
                _fileWatcher.Deleted += OnFileChanged;
            }
            catch
            {
                // File watcher not critical - continue without it
            }
        }

        // Also watch for new session directories being created in ~/.claude/
        // This handles the case where tasks/ doesn't exist yet
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
        // If a new directory was created under tasks/, refresh
        if (e.FullPath.StartsWith(_tasksRootPath, StringComparison.OrdinalIgnoreCase))
        {
            DebounceRefresh();
        }
        // If tasks/ directory was just created, start the file watcher
        else if (e.FullPath.Equals(_tasksRootPath, StringComparison.OrdinalIgnoreCase) && _fileWatcher == null)
        {
            StartFileWatcher();
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
        _fileWatcher?.Dispose();
        _directoryWatcher?.Dispose();
        _debounceTokenSource?.Cancel();
        _debounceTokenSource?.Dispose();
    }

    /// <summary>
    /// DTO for deserializing Claude task JSON files.
    /// Matches the structure of ~/.claude/tasks/{session-id}/*.json files.
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
