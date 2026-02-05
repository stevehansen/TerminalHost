using System.IO;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for reading Claude Code session data from ~/.claude/projects/.
///
/// Uses a hybrid approach:
/// - Reads sessions-index.json for rich metadata (summary, branch, timestamps)
/// - Scans JSONL files directly to discover new sessions not yet indexed
///
/// Note: Claude Code updates sessions-index.json lazily (on exit or periodically),
/// so we can't rely solely on it for real-time session discovery.
/// </summary>
public sealed class ClaudeSessionIndexService : IClaudeSessionIndexService, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly string _projectsRootPath;
    private readonly Dictionary<string, ClaudeSessionIndexEntry> _sessionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ClaudeSessionIndexEntry>> _projectSessionsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private FileSystemWatcher? _indexWatcher;
    private FileSystemWatcher? _jsonlWatcher;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event EventHandler? SessionsChanged;

    public ClaudeSessionIndexService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        // Projects root path: ~/.claude/projects/
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _projectsRootPath = Path.Combine(userProfile, ".claude", "projects");

        // Load on startup
        Refresh();

        // Start watching for changes (both index files and JSONL files)
        StartFileWatchers();
    }

    public IReadOnlyList<ClaudeSessionIndexEntry> GetSessionsForProject(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return Array.Empty<ClaudeSessionIndexEntry>();

        // Normalize path for comparison
        var normalizedPath = NormalizePath(projectPath);

        lock (_lock)
        {
            if (_projectSessionsCache.TryGetValue(normalizedPath, out var sessions))
            {
                return sessions
                    .OrderByDescending(s => s.Modified ?? s.Created ?? DateTime.MinValue)
                    .ToList()
                    .AsReadOnly();
            }
        }

        return Array.Empty<ClaudeSessionIndexEntry>();
    }

    public ClaudeSessionIndexEntry? GetSessionById(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;

        lock (_lock)
        {
            return _sessionCache.TryGetValue(sessionId, out var entry) ? entry : null;
        }
    }

    public string? GetProjectPathForSession(string sessionId)
    {
        var entry = GetSessionById(sessionId);
        return entry?.ProjectPath;
    }

    public IReadOnlyList<ClaudeSessionIndexEntry> GetAllSessions()
    {
        lock (_lock)
        {
            return _sessionCache.Values
                .OrderByDescending(s => s.Modified ?? s.Created ?? DateTime.MinValue)
                .ToList()
                .AsReadOnly();
        }
    }

    public IReadOnlyList<string> GetAllProjectPaths()
    {
        lock (_lock)
        {
            return _projectSessionsCache.Keys.ToList().AsReadOnly();
        }
    }

    public void Refresh()
    {
        lock (_lock)
        {
            _sessionCache.Clear();
            _projectSessionsCache.Clear();

            if (!_fileSystem.DirectoryExists(_projectsRootPath))
                return;

            try
            {
                var projectDirs = _fileSystem.GetDirectories(_projectsRootPath);

                foreach (var projectDir in projectDirs)
                {
                    LoadProjectIndex(projectDir);
                }
            }
            catch
            {
                // Continue with empty cache on error
            }
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadProjectIndex(string projectDir)
    {
        string? projectPath = null;
        var indexPath = Path.Combine(projectDir, "sessions-index.json");

        // First, try to load from sessions-index.json for rich metadata
        if (_fileSystem.FileExists(indexPath))
        {
            try
            {
                var json = _fileSystem.ReadAllText(indexPath);
                var index = JsonSerializer.Deserialize<ClaudeSessionIndex>(json, JsonOptions);

                if (index?.Entries != null)
                {
                    projectPath = index.OriginalPath;

                    foreach (var entry in index.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.SessionId))
                            continue;

                        // Use projectPath from entry, or fall back to originalPath from index
                        var entryProjectPath = entry.ProjectPath ?? index.OriginalPath;
                        if (!string.IsNullOrEmpty(entryProjectPath))
                        {
                            AddSessionEntry(entry, entryProjectPath);
                        }
                    }
                }
            }
            catch
            {
                // Skip invalid index files
            }
        }

        // Also scan JSONL files to discover sessions not yet in the index
        // Claude Code updates the index lazily, so new sessions may not be indexed yet
        ScanJsonlFiles(projectDir, projectPath);
    }

    /// <summary>
    /// Scans for JSONL session files that may not be in the sessions-index.json yet.
    /// Creates minimal entries for sessions discovered this way.
    /// </summary>
    private void ScanJsonlFiles(string projectDir, string? projectPath)
    {
        try
        {
            var jsonlFiles = _fileSystem.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly);

            foreach (var jsonlFile in jsonlFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(jsonlFile);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                // Skip if already in cache (from sessions-index.json)
                if (_sessionCache.ContainsKey(fileName))
                    continue;

                // Determine project path if we don't have it
                var sessionProjectPath = projectPath;
                if (string.IsNullOrEmpty(sessionProjectPath))
                {
                    // Decode from directory name as fallback
                    var projectDirName = Path.GetFileName(projectDir);
                    sessionProjectPath = DecodeProjectPath(projectDirName);
                }

                if (string.IsNullOrEmpty(sessionProjectPath))
                    continue;

                // Create a minimal entry for this session
                DateTime? created = null;
                DateTime? modified = null;
                try
                {
                    var fileInfo = new FileInfo(jsonlFile);
                    created = fileInfo.CreationTimeUtc;
                    modified = fileInfo.LastWriteTimeUtc;
                }
                catch { }

                var entry = new ClaudeSessionIndexEntry
                {
                    SessionId = fileName,
                    FullPath = jsonlFile,
                    ProjectPath = sessionProjectPath,
                    Created = created,
                    Modified = modified,
                    // Note: These sessions don't have rich metadata until Claude updates the index
                    Summary = null,
                    GitBranch = null,
                    MessageCount = 0,
                    FirstPrompt = null
                };

                AddSessionEntry(entry, sessionProjectPath);
            }
        }
        catch
        {
            // Skip directories that can't be read
        }
    }

    /// <summary>
    /// Adds a session entry to the caches.
    /// </summary>
    private void AddSessionEntry(ClaudeSessionIndexEntry entry, string projectPath)
    {
        var normalizedPath = NormalizePath(projectPath);

        // Add to session cache
        _sessionCache[entry.SessionId] = entry;

        // Add to project sessions cache
        if (!_projectSessionsCache.TryGetValue(normalizedPath, out var projectSessions))
        {
            projectSessions = [];
            _projectSessionsCache[normalizedPath] = projectSessions;
        }
        projectSessions.Add(entry);
    }

    /// <summary>
    /// Decodes a project directory name to the actual file path.
    /// Format: "P--W-Memoreaz-MobileApp" → "P:\W\Memoreaz.MobileApp"
    /// </summary>
    private static string DecodeProjectPath(string? encodedPath)
    {
        if (string.IsNullOrEmpty(encodedPath))
            return string.Empty;

        // Replace -- with the appropriate path separator for this OS
        var separator = Path.DirectorySeparatorChar.ToString();
        return encodedPath.Replace("--", separator);
    }

    private void StartFileWatchers()
    {
        if (!_fileSystem.DirectoryExists(_projectsRootPath))
            return;

        // Watch sessions-index.json files for metadata updates
        try
        {
            _indexWatcher = new FileSystemWatcher(_projectsRootPath, "sessions-index.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _indexWatcher.Created += OnFileChanged;
            _indexWatcher.Changed += OnFileChanged;
            _indexWatcher.Deleted += OnFileChanged;
        }
        catch
        {
            // File watcher not critical - continue without it
        }

        // Also watch JSONL files for new sessions (since index updates lazily)
        try
        {
            _jsonlWatcher = new FileSystemWatcher(_projectsRootPath, "*.jsonl")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _jsonlWatcher.Created += OnFileChanged;
            _jsonlWatcher.Changed += OnFileChanged;
            _jsonlWatcher.Deleted += OnFileChanged;
        }
        catch
        {
            // File watcher not critical - continue without it
        }
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        // Debounce: wait a bit to batch multiple rapid changes
        Task.Delay(300).ContinueWith(_ => Refresh());
    }

    /// <summary>
    /// Normalizes a path for consistent comparison.
    /// Both Windows and macOS (default APFS) use case-insensitive filesystems.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        // Normalize path and always lowercase for case-insensitive comparison
        // Both Windows and macOS (default APFS) use case-insensitive filesystems
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    public void Dispose()
    {
        _indexWatcher?.Dispose();
        _jsonlWatcher?.Dispose();
    }
}
