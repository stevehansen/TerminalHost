using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class ConfigurationService : IConfigurationService
{
    private readonly IFileSystem _fileSystem;
    private readonly JsonFileService<AppConfiguration> _jsonFileService;
    private static readonly object _saveLock = new object();

    public string ConfigurationFilePath { get; }
    private readonly string _configDirectory;

    public ConfigurationService(IFileSystem fileSystem, string? userDataDir = null)
    {
        _fileSystem = fileSystem;

        _configDirectory = userDataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TerminalHost");

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
        IoCounters.TrackConfigLoad(caller);
        var config = _jsonFileService.Load();
        if (!config.Profiles.Any()) // If config has no profiles, add the default PowerShell profile
        {
            config.Profiles.Add(CreateDefaultPowerShellProfile());
            Save(config); // Save after adding the default profile
        }
        config.Settings.Memory.EnsureDefaults();
        return config;
    }

    public void Save(AppConfiguration configuration, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        IoCounters.TrackConfigSave(caller);
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
                               IsBuiltInCommand(commandExe);

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
            "cmd", "cmd.exe",
            "pwsh", "pwsh.exe",
            "powershell", "powershell.exe",
            "bash", "bash.exe",
            "wsl", "wsl.exe"
        };

        return builtIns.Any(b => b.Equals(command, StringComparison.OrdinalIgnoreCase));
    }

    private static AppConfiguration CreateDefaultConfiguration()
    {
        var config = new AppConfiguration
        {
            Profiles =
            [
                new() {
                    Id = "powershell",
                    Name = "PowerShell",
                    Command = "pwsh.exe",
                    WorkingDir = "%USERPROFILE%",
                    Icon = "🔷",
                    AutoStart = true
                }
            ],
            Settings = new AppSettings
            {
                ConfirmOnClose = true,
                ShowInSystemTray = false
            }
        };

        return config;
    }

    // Helper method to create a default PowerShell profile
    private static Profile CreateDefaultPowerShellProfile()
    {
        return new()
        {
            Id = "powershell",
            Name = "PowerShell",
            Command = "pwsh.exe",
            WorkingDir = "%USERPROFILE%",
            Icon = "🔷",
            AutoStart = true
        };
    }
}
