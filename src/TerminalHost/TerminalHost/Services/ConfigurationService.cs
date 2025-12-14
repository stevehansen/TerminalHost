using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public class ConfigurationService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TerminalHost");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppConfiguration Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return CreateDefaultConfiguration();
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions)
                   ?? CreateDefaultConfiguration();
        }
        catch (JsonException)
        {
            return CreateDefaultConfiguration();
        }
    }

    public void Save(AppConfiguration configuration)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    public string LoadRawJson()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return JsonSerializer.Serialize(CreateDefaultConfiguration(), JsonOptions);
        }

        return File.ReadAllText(ConfigFilePath);
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

            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigFilePath, json);

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
    private static (List<string> errors, List<string> warnings) ValidateConfiguration(AppConfiguration config)
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
            var commandExists = File.Exists(commandExe) ||
                               File.Exists(Environment.ExpandEnvironmentVariables(commandExe)) ||
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

    public static string GetConfigFilePath() => ConfigFilePath;

    private AppConfiguration CreateDefaultConfiguration()
    {
        var config = new AppConfiguration
        {
            Profiles = new List<Profile>
            {
                new Profile
                {
                    Id = "powershell",
                    Name = "PowerShell",
                    Command = "pwsh.exe",
                    WorkingDir = "%USERPROFILE%",
                    Icon = "🔷",
                    AutoStart = true
                }
            },
            Settings = new AppSettings
            {
                ConfirmOnClose = true,
                ShowInSystemTray = false
            }
        };

        // Try to save the default configuration
        try
        {
            Save(config);
        }
        catch
        {
            // Ignore if we can't save
        }

        return config;
    }
}
