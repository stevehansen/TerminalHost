using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Domain;

namespace TerminalHost.Services;

internal sealed class ConfigurationService : IConfigurationService
{
    private readonly IFileSystem _fileSystem;
    private readonly ISystemInfoService _systemInfoService;
    private readonly JsonFileService<AppConfiguration> _jsonFileService;
    private static readonly object _saveLock = new object();

    public string ConfigurationFilePath { get; }
    private readonly string _configDirectory;

    public ConfigurationService(IFileSystem fileSystem, ISystemInfoService systemInfoService, string? userDataDir = null)
    {
        _fileSystem = fileSystem;
        _systemInfoService = systemInfoService;

        if (!string.IsNullOrEmpty(userDataDir))
        {
            _configDirectory = userDataDir;
        }
        else
        {
            _configDirectory = systemInfoService.GetApplicationDataPath();
        }

        ConfigurationFilePath = Path.Combine(_configDirectory, "config.json");

        _jsonFileService = new JsonFileService<AppConfiguration>(fileSystem, ConfigurationFilePath, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AppConfiguration Load([System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        var config = _jsonFileService.Load();
        var needsSave = false;

        if (!config.Profiles.Any()) // If config has no profiles, add the default shell profile
        {
            config.Profiles.Add(CreateDefaultShellProfile());
            needsSave = true;
        }

        // Apply platform-specific defaults for shell command if it's still Windows default
        if (string.IsNullOrEmpty(config.Settings.ShellCommand) ||
            config.Settings.ShellCommand.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
            config.Settings.ShellCommand.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            var defaultShell = _systemInfoService.GetDefaultShell();
            config.Settings.ShellCommand = defaultShell;
            config.Settings.ShellCommandName = Path.GetFileName(defaultShell) switch
            {
                "zsh" => "Zsh",
                "bash" => "Bash",
                "fish" => "Fish",
                _ => "Shell"
            };
            needsSave = true;
        }

        // Apply platform-specific defaults for custom command (Claude) if it's still Windows default
        if (string.IsNullOrEmpty(config.Settings.CustomCommand) ||
            config.Settings.CustomCommand.Contains("USERPROFILE") ||
            config.Settings.CustomCommand.Contains("claude.exe"))
        {
            config.Settings.CustomCommand = _systemInfoService.GetDefaultCustomCommand();
            needsSave = true;
        }

        // Apply platform-specific defaults for AI Assistant commands if they're still Windows defaults
        if (config.AiAssistants != null)
        {
            foreach (var assistant in config.AiAssistants)
            {
                if (!string.IsNullOrEmpty(assistant.Command) &&
                    (assistant.Command.Contains("USERPROFILE") || assistant.Command.Contains(".exe")))
                {
                    // Replace Windows path with macOS-appropriate command
                    if (assistant.Id == "claude" || assistant.Command.Contains("claude"))
                    {
                        assistant.Command = _systemInfoService.GetDefaultCustomCommand();
                        needsSave = true;
                    }
                    else if (assistant.Command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // Remove .exe extension for other commands
                        assistant.Command = assistant.Command[..^4];
                        needsSave = true;
                    }
                }
            }
        }

        if (needsSave)
        {
            Save(config);
        }

        return config;
    }

    public void Save(AppConfiguration configuration, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        lock (_saveLock)
        {
            _jsonFileService.Save(configuration);
        }
    }

    public string LoadRawJson()
    {
        // This method still needs to directly read the file to provide raw JSON string,
        // but it can leverage the JsonFileService's logic for finding the primary file
        // or falling back to backup before reading.
        // For simplicity, we'll try to read the primary path, and if it fails, fallback to default JSON.
        if (_fileSystem.FileExists(ConfigurationFilePath))
        {
            return _fileSystem.ReadAllText(ConfigurationFilePath);
        }
        else if (_fileSystem.FileExists(ConfigurationFilePath + ".bak"))
        {
            return _fileSystem.ReadAllText(ConfigurationFilePath + ".bak");
        }

        return JsonSerializer.Serialize(CreateDefaultConfiguration(), JsonOptions);
    }

    public (bool success, string? error, string? warning) SaveRawJson(string json)
    {
        try
        {
            // Validate it's valid JSON and deserializes correctly
            var config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);
            if (config == null)
            {
                return (false, "Failed to parse configuration", null);
            }

            // Validate configuration values
            var (errors, warnings) = ValidateConfiguration(config);
            if (errors.Count > 0)
            {
                return (false, string.Join("\n", errors), null);
            }

            _fileSystem.CreateDirectory(_configDirectory);
            _fileSystem.WriteAllText(ConfigurationFilePath, json); // Direct write for raw JSON
            
            // Return success with optional warnings
            var warningMessage = warnings.Count > 0 ? string.Join("\n", warnings) : null;
            return (true, null, warningMessage);
        }
        catch (JsonException ex)
        {
            return (false, $"JSON Error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Validates configuration values and returns errors (block save) and warnings (allow save).
    /// </summary>
    private (List<string> errors, List<string> warnings) ValidateConfiguration(AppConfiguration config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate link patterns have valid regex (ERROR - blocks save)
        if (config.LinkPatterns != null)
        {
            foreach (var pattern in config.LinkPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern.Pattern))
                    continue;

                try
                {
                    // Test compile the regex
                    _ = new Regex(pattern.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
                }
                catch (RegexParseException ex)
                {
                    var name = !string.IsNullOrWhiteSpace(pattern.Name) ? pattern.Name : pattern.Id;
                    errors.Add($"Invalid regex in link pattern '{name}': {ex.Message}");
                }
            }
        }

        // Get custom paths for command validation
        var customPaths = config.Settings?.CustomPaths ?? new List<string>();

        // Validate custom command exists (WARNING - allows save but notifies user)
        var customCommand = config.Settings?.CustomCommand;
        if (!string.IsNullOrWhiteSpace(customCommand))
        {
            var commandExe = customCommand.Split(' ')[0];
            if (!CommandExists(commandExe, customPaths))
            {
                warnings.Add($"Custom command not found: {commandExe}");
            }
        }

        // Validate AI Assistant commands exist
        if (config.AiAssistants != null)
        {
            foreach (var assistant in config.AiAssistants.Where(a => a.Enabled))
            {
                if (!string.IsNullOrWhiteSpace(assistant.Command))
                {
                    var commandExe = assistant.Command.Split(' ')[0];
                    if (!CommandExists(commandExe, customPaths))
                    {
                        warnings.Add($"AI Assistant '{assistant.Name}' command not found: {commandExe}");
                    }
                }
            }
        }

        // Validate Profile commands exist
        if (config.Profiles != null)
        {
            foreach (var profile in config.Profiles)
            {
                if (!string.IsNullOrWhiteSpace(profile.Command))
                {
                    var commandExe = profile.Command.Split(' ')[0];
                    if (!CommandExists(commandExe, customPaths))
                    {
                        warnings.Add($"Profile '{profile.Name}' command not found: {commandExe}");
                    }
                }
            }
        }

        // Validate keyboard shortcut conflicts (WARNING - allows save but notifies user)
        var shortcutConflicts = ShortcutConflictService.GetAllConflicts(
            config.QuickCommands,
            config.Profiles);
        warnings.AddRange(shortcutConflicts);

        return (errors, warnings);
    }

    /// <summary>
    /// Checks if a command exists on the system.
    /// </summary>
    private bool CommandExists(string command, IEnumerable<string> customPaths)
    {
        // Check if it's a direct file path that exists
        if (_fileSystem.FileExists(command))
            return true;

        // Try expanding environment variables
        var expanded = Environment.ExpandEnvironmentVariables(command);
        if (expanded != command && _fileSystem.FileExists(expanded))
            return true;

        // Check if it's a built-in shell command
        if (IsBuiltInShell(command))
            return true;

        // Check in PATH and custom paths
        return IsCommandInPath(command, customPaths);
    }

    private static bool IsBuiltInShell(string command)
    {
        // Only include true shell commands that are guaranteed to exist on macOS/Unix
        var builtInShells = new[]
        {
            "zsh", "/bin/zsh",
            "bash", "/bin/bash",
            "sh", "/bin/sh",
            "fish",
            "tcsh", "/bin/tcsh",
            "csh", "/bin/csh"
        };

        var commandName = Path.GetFileName(command);
        return builtInShells.Any(b =>
            commandName.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            command.Equals(b, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCommandInPath(string command, IEnumerable<string>? customPaths = null)
    {
        // If it's already a path (contains directory separator), don't search PATH
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains('/'))
            return false;

        // Combine system PATH with custom paths
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathSeparator = OperatingSystem.IsWindows() ? ';' : ':';
        var systemPaths = pathEnv.Split(pathSeparator);

        // Add custom paths first (they take priority)
        var allPaths = (customPaths ?? Enumerable.Empty<string>()).Concat(systemPaths);

        foreach (var path in allPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var fullPath = Path.Combine(path, command);
            if (File.Exists(fullPath))
                return true;

            // On Windows, also check with common extensions
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(fullPath + ".exe") || File.Exists(fullPath + ".cmd") || File.Exists(fullPath + ".bat"))
                    return true;
            }
        }

        return false;
    }

    private AppConfiguration CreateDefaultConfiguration()
    {
        var config = new AppConfiguration
        {
            Profiles =
            [
                CreateDefaultShellProfile()
            ],
            Settings = new AppSettings
            {
                ConfirmOnClose = true,
                ShowInSystemTray = false
            }
        };

        return config;
    }

    // Helper method to create a default shell profile
    private Profile CreateDefaultShellProfile()
    {
        var shell = _systemInfoService.GetDefaultShell();
        var shellName = Path.GetFileName(shell) switch
        {
            "zsh" => "Zsh",
            "bash" => "Bash",
            "fish" => "Fish",
            _ => "Shell"
        };

        return new()
        {
            Id = "shell",
            Name = shellName,
            Command = shell,
            WorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Icon = "🔷",
            AutoStart = true
        };
    }

}