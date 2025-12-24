using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using TerminalHost.Domain;

namespace TerminalHost.Services;

internal sealed class ConfigurationService : IConfigurationService
{
    private readonly IFileSystem _fileSystem;
    private readonly JsonFileService<AppConfiguration> _jsonFileService;
    private static readonly object _saveLock = new object();

    public string ConfigurationFilePath { get; }
    private readonly string _configDirectory;

    public ConfigurationService(IFileSystem fileSystem, string? userDataDir = null)
    {
        _fileSystem = fileSystem;

        if (!string.IsNullOrEmpty(userDataDir))
        {
            _configDirectory = userDataDir;
        }
        else
        {
            // macOS: ~/Library/Application Support/TerminalHost
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _configDirectory = Path.Combine(home, "Library", "Application Support", "TerminalHost");
        }

        ConfigurationFilePath = Path.Combine(_configDirectory, "config.json");

        _jsonFileService = new JsonFileService<AppConfiguration>(fileSystem, ConfigurationFilePath, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AppConfiguration Load()
    {
        var config = _jsonFileService.Load();
        if (!config.Profiles.Any()) // If config has no profiles, add the default shell profile
        {
            config.Profiles.Add(CreateDefaultShellProfile());
            Save(config); // Save after adding the default profile
        }
        return config;
    }

    public void Save(AppConfiguration configuration)
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

        // Validate custom command exists (WARNING - allows save but notifies user)
        if (config.Settings != null && !string.IsNullOrWhiteSpace(config.Settings.CustomCommand))
        {
            var commandExe = config.Settings.CustomCommand.Split(' ')[0];
            var commandExists = _fileSystem.FileExists(commandExe) ||
                               _fileSystem.FileExists(Environment.ExpandEnvironmentVariables(commandExe)) ||
                               IsBuiltInCommand(commandExe) ||
                               IsCommandInPath(commandExe);

            if (!commandExists)
            {
                warnings.Add($"Custom command not found: {commandExe}");
            }
        }

        return (errors, warnings);
    }

    private static bool IsBuiltInCommand(string command)
    {
        var builtIns = new[]
        {
            // macOS shells
            "zsh", "/bin/zsh",
            "bash", "/bin/bash",
            "sh", "/bin/sh",
            "fish", "/usr/local/bin/fish", "/opt/homebrew/bin/fish",
            // Common utilities
            "tmux", "screen"
        };

        var commandName = Path.GetFileName(command);
        return builtIns.Any(b =>
            commandName.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            command.Equals(b, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCommandInPath(string command)
    {
        // If it's already a path (contains directory separator), don't search PATH
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains('/'))
            return false;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return false;

        var pathSeparator = OperatingSystem.IsWindows() ? ';' : ':';
        var paths = pathEnv.Split(pathSeparator);

        foreach (var path in paths)
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

    private static AppConfiguration CreateDefaultConfiguration()
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
    private static Profile CreateDefaultShellProfile()
    {
        var shell = GetDefaultShell();
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

    private static string GetDefaultShell()
    {
        // Check for environment variable first
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return shell;

        // macOS defaults
        if (File.Exists("/bin/zsh")) return "/bin/zsh";
        if (File.Exists("/bin/bash")) return "/bin/bash";

        return "/bin/sh";
    }
}