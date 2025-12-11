using System.IO;
using System.Text.Json;
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

    public (bool success, string? error) SaveRawJson(string json)
    {
        try
        {
            // Validate it's valid JSON and deserializes correctly
            var config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);
            if (config == null)
            {
                return (false, "Failed to parse configuration");
            }

            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigFilePath, json);
            return (true, null);
        }
        catch (JsonException ex)
        {
            return (false, $"JSON Error: {ex.Message}");
        }
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
