using System.IO;
using System.Text.RegularExpressions;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for detecting and managing Claude Code custom slash commands from .md files.
/// Scans ~/.claude/commands/ for global commands and .claude/commands/ for project commands.
/// </summary>
internal sealed partial class ClaudeCommandService : IClaudeCommandService, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IProfileRegistry _profileRegistry;

    private List<ClaudeCommand> _globalCommands = [];
    private readonly Dictionary<string, List<ClaudeCommand>> _projectCommandsCache = new(StringComparer.OrdinalIgnoreCase);

    private IDisposable? _globalWatcher;
    private readonly Dictionary<string, IDisposable> _projectWatchers = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _globalCommandsPath;

    public ClaudeCommandService(IFileSystem fileSystem, IProfileRegistry profileRegistry)
    {
        _fileSystem = fileSystem;
        _profileRegistry = profileRegistry;

        // Global commands path: ~/.claude/commands/
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _globalCommandsPath = Path.Combine(userProfile, ".claude", "commands");

        // Load global commands on startup
        RefreshGlobalCommands();
        StartGlobalWatcher();
    }

    public IReadOnlyList<ClaudeCommand> GlobalCommands => _globalCommands.AsReadOnly();

    public IReadOnlyList<ClaudeCommand> GetProjectCommands(string workingDirectory)
    {
        var normalizedPath = NormalizePath(workingDirectory);

        if (!_projectCommandsCache.TryGetValue(normalizedPath, out var commands))
        {
            commands = LoadProjectCommands(workingDirectory);
            _projectCommandsCache[normalizedPath] = commands;
            StartProjectWatcher(workingDirectory);
        }

        return commands.AsReadOnly();
    }

    public IReadOnlyList<ClaudeCommand> GetAllCommands(string? workingDirectory)
    {
        var result = new List<ClaudeCommand>();

        // Add global commands first
        result.AddRange(_globalCommands);

        // Add project commands (overriding global commands with the same name)
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            var projectCommands = GetProjectCommands(workingDirectory);
            var projectCommandNames = new HashSet<string>(projectCommands.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

            // Remove global commands that are overridden by project commands
            result.RemoveAll(c => projectCommandNames.Contains(c.Name));

            // Add project commands
            result.AddRange(projectCommands);
        }

        return result.AsReadOnly();
    }

    public void RefreshGlobalCommands()
    {
        _globalCommands = LoadCommands(_globalCommandsPath, ClaudeCommandSource.Global);
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshProjectCommands(string workingDirectory)
    {
        var normalizedPath = NormalizePath(workingDirectory);
        var commands = LoadProjectCommands(workingDirectory);
        _projectCommandsCache[normalizedPath] = commands;
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CommandsChanged;

    private List<ClaudeCommand> LoadProjectCommands(string workingDirectory)
    {
        var projectCommandsPath = Path.Combine(workingDirectory, ".claude", "commands");
        return LoadCommands(projectCommandsPath, ClaudeCommandSource.Project);
    }

    private List<ClaudeCommand> LoadCommands(string directoryPath, ClaudeCommandSource source)
    {
        var commands = new List<ClaudeCommand>();

        if (!_fileSystem.DirectoryExists(directoryPath))
            return commands;

        try
        {
            var mdFiles = _fileSystem.GetFiles(directoryPath, "*.md", SearchOption.TopDirectoryOnly);

            foreach (var filePath in mdFiles)
            {
                try
                {
                    var command = ParseCommandFile(filePath, source);
                    if (command != null)
                    {
                        commands.Add(command);
                    }
                }
                catch
                {
                    // Skip files that can't be read
                }
            }
        }
        catch
        {
            // Directory access error - return empty list
        }

        return commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private ClaudeCommand? ParseCommandFile(string filePath, ClaudeCommandSource source)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string? description = null;

        try
        {
            var content = _fileSystem.ReadAllText(filePath);
            description = ExtractDescription(content);
        }
        catch
        {
            // Can't read file content - use filename as description
        }

        // Look up shortcut from config
        var shortcut = GetShortcutForCommand(fileName);

        return new ClaudeCommand
        {
            Id = $"{source.ToString().ToLower()}-{fileName}",
            Name = fileName,
            FilePath = filePath,
            Description = description,
            Shortcut = shortcut,
            Source = source
        };
    }

    private static string? ExtractDescription(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        // Check for YAML frontmatter (--- ... ---)
        var frontmatterMatch = FrontmatterRegex().Match(content);
        if (frontmatterMatch.Success)
        {
            var frontmatter = frontmatterMatch.Groups[1].Value;

            // Look for "description:" field
            var descMatch = DescriptionRegex().Match(frontmatter);
            if (descMatch.Success)
            {
                return descMatch.Groups[1].Value.Trim().Trim('"', '\'');
            }
        }

        // No frontmatter or no description - use first non-empty line
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Skip frontmatter if present
        if (content.TrimStart().StartsWith("---"))
        {
            var endFrontmatter = content.IndexOf("---", content.IndexOf("---") + 3);
            if (endFrontmatter > 0)
            {
                var afterFrontmatter = content[(endFrontmatter + 3)..].TrimStart();
                lines = afterFrontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                // Remove markdown heading markers
                trimmed = trimmed.TrimStart('#').Trim();

                // Truncate if too long
                if (trimmed.Length > 100)
                {
                    trimmed = trimmed[..97] + "...";
                }

                return trimmed;
            }
        }

        return null;
    }

    private string? GetShortcutForCommand(string commandName)
    {
        // Get shortcuts from config
        var shortcuts = _profileRegistry.Settings.ClaudeCommandShortcuts;
        return shortcuts.TryGetValue(commandName, out var shortcut) ? shortcut : null;
    }

    private void StartGlobalWatcher()
    {
        if (!_fileSystem.DirectoryExists(_globalCommandsPath))
            return;

        try
        {
            var watcher = new FileSystemWatcher(_globalCommandsPath, "*.md")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            watcher.Created += (_, _) => RefreshGlobalCommands();
            watcher.Deleted += (_, _) => RefreshGlobalCommands();
            watcher.Changed += (_, _) => RefreshGlobalCommands();
            watcher.Renamed += (_, _) => RefreshGlobalCommands();

            _globalWatcher = new FileWatcherDisposable(watcher);
        }
        catch
        {
            // Can't watch directory - commands will be static
        }
    }

    private void StartProjectWatcher(string workingDirectory)
    {
        var normalizedPath = NormalizePath(workingDirectory);

        // Don't create duplicate watchers
        if (_projectWatchers.ContainsKey(normalizedPath))
            return;

        var projectCommandsPath = Path.Combine(workingDirectory, ".claude", "commands");
        if (!_fileSystem.DirectoryExists(projectCommandsPath))
            return;

        try
        {
            var watcher = new FileSystemWatcher(projectCommandsPath, "*.md")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            void OnChange(object sender, FileSystemEventArgs e)
            {
                RefreshProjectCommands(workingDirectory);
            }

            watcher.Created += OnChange;
            watcher.Deleted += OnChange;
            watcher.Changed += OnChange;
            watcher.Renamed += (_, _) => RefreshProjectCommands(workingDirectory);

            _projectWatchers[normalizedPath] = new FileWatcherDisposable(watcher);
        }
        catch
        {
            // Can't watch directory - commands will be static
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
    }

    public void Dispose()
    {
        _globalWatcher?.Dispose();

        foreach (var watcher in _projectWatchers.Values)
        {
            watcher.Dispose();
        }
        _projectWatchers.Clear();
    }

    [GeneratedRegex(@"^---\s*\n(.*?)\n---", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^description:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionRegex();

    private class FileWatcherDisposable : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private bool _disposed;

        public FileWatcherDisposable(FileSystemWatcher watcher)
        {
            _watcher = watcher;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _disposed = true;
            }
        }
    }
}
