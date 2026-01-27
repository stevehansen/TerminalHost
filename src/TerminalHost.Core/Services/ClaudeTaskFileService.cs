using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for reading persisted Claude Code tasks from ~/.claude/tasks/.
/// Each session writes task JSON files to ~/.claude/tasks/{session-id}/*.json.
/// </summary>
public sealed class ClaudeTaskFileService : IClaudeTaskFileService, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly string _tasksRootPath;
    private readonly string _projectsRootPath;
    private readonly Dictionary<string, List<FocusTask>> _sessionTasksCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sessionToProjectPathCache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _fileWatcher;
    private FileSystemWatcher? _projectsWatcher;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event EventHandler? TasksChanged;

    public ClaudeTaskFileService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        // Tasks root path: ~/.claude/tasks/
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _tasksRootPath = Path.Combine(userProfile, ".claude", "tasks");
        _projectsRootPath = Path.Combine(userProfile, ".claude", "projects");

        // Load session-to-project mappings
        RefreshSessionProjectMappings();

        // Load tasks on startup
        Refresh();

        // Start watching for changes
        StartFileWatcher();
        StartProjectsWatcher();
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
            // Refresh session-to-project mappings first
            RefreshSessionProjectMappings();

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

            // Sort by ID (which is typically numeric)
            tasks = tasks.OrderBy(t => t.Id.ToString()).ToList();
        }
        catch
        {
            // Return empty list on error
        }

        return tasks;
    }

    private FocusTask MapToFocusTask(ClaudeTaskDto dto, string sessionId, string filePath)
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

        // Get project path for this session
        var projectPaths = new List<string>();
        if (_sessionToProjectPathCache.TryGetValue(sessionId, out var projectPath))
        {
            projectPaths.Add(projectPath);
        }

        return new FocusTask
        {
            Id = guid.ToString(),
            Title = dto.Subject ?? $"Task {dto.Id}",
            Description = dto.Description ?? string.Empty,
            ActiveForm = dto.ActiveForm ?? string.Empty,
            Status = status,
            Priority = 0, // Not in Claude task files
            ProjectPaths = projectPaths,
            CreatedAt = createdAt,
            StartedAt = status == FocusTaskStatus.InProgress ? createdAt : null,
            CompletedAt = status == FocusTaskStatus.Completed ? createdAt : null,
            IsClaudeTask = true,
            ClaudeTaskId = dto.Id,
            ClaudeSessionId = sessionId
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
        if (!_fileSystem.DirectoryExists(_tasksRootPath))
            return;

        try
        {
            _fileWatcher = new FileSystemWatcher(_tasksRootPath, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
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

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        // Debounce: wait a bit to batch multiple rapid changes
        Task.Delay(500).ContinueWith(_ => Refresh());
    }

    private void StartProjectsWatcher()
    {
        if (!_fileSystem.DirectoryExists(_projectsRootPath))
            return;

        try
        {
            _projectsWatcher = new FileSystemWatcher(_projectsRootPath, "*.jsonl")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _projectsWatcher.Created += OnProjectFileChanged;
            _projectsWatcher.Changed += OnProjectFileChanged;
            _projectsWatcher.Deleted += OnProjectFileChanged;
        }
        catch
        {
            // File watcher not critical - continue without it
        }
    }

    private void OnProjectFileChanged(object? sender, FileSystemEventArgs e)
    {
        // Debounce: wait a bit to batch multiple rapid changes
        Task.Delay(500).ContinueWith(_ => Refresh());
    }

    /// <summary>
    /// Scans ~/.claude/projects/ to build a map of session IDs to project paths.
    /// Project directories are named with encoded paths (e.g., "P--W-Memoreaz-MobileApp" for "P:\W\Memoreaz.MobileApp").
    /// Each project directory contains .jsonl files named after session IDs.
    /// </summary>
    private void RefreshSessionProjectMappings()
    {
        _sessionToProjectPathCache.Clear();

        if (!_fileSystem.DirectoryExists(_projectsRootPath))
            return;

        try
        {
            var projectDirs = _fileSystem.GetDirectories(_projectsRootPath);

            foreach (var projectDir in projectDirs)
            {
                var projectDirName = Path.GetFileName(projectDir);
                if (string.IsNullOrEmpty(projectDirName))
                    continue;

                // Decode the project directory name to get the actual path
                // Format: "P--W-Memoreaz-MobileApp" → "P:\W\Memoreaz.MobileApp"
                var projectPath = DecodeProjectPath(projectDirName);

                // Get all .jsonl session files in this project directory
                try
                {
                    var sessionFiles = _fileSystem.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly);

                    foreach (var sessionFile in sessionFiles)
                    {
                        var sessionFileName = Path.GetFileNameWithoutExtension(sessionFile);
                        if (!string.IsNullOrEmpty(sessionFileName))
                        {
                            // Map session ID to project path
                            _sessionToProjectPathCache[sessionFileName] = projectPath;
                        }
                    }
                }
                catch
                {
                    // Skip project directories that can't be read
                }
            }
        }
        catch
        {
            // If we can't read the projects directory, continue with empty cache
        }
    }

    /// <summary>
    /// Decodes a project directory name to the actual file path.
    /// Format: "P--W-Memoreaz-MobileApp" → "P:\W\Memoreaz.MobileApp"
    /// </summary>
    private static string DecodeProjectPath(string encodedPath)
    {
        if (string.IsNullOrEmpty(encodedPath))
            return string.Empty;

        // Replace -- with the appropriate path separator for this OS
        var separator = Path.DirectorySeparatorChar.ToString();
        return encodedPath.Replace("--", separator);
    }

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _projectsWatcher?.Dispose();
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
