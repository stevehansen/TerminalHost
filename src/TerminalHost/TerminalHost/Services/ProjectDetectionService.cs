using System.IO;
using System.Text.Json;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for auto-detecting project types and generating run configurations.
/// </summary>
public class ProjectDetectionService
{
    private readonly ProfileRegistry _profileRegistry;

    public ProjectDetectionService(ProfileRegistry profileRegistry)
    {
        _profileRegistry = profileRegistry;
    }

    /// <summary>
    /// Detects the project type by scanning for marker files in the directory.
    /// </summary>
    /// <param name="workingDirectory">The directory to scan.</param>
    /// <returns>The detected project type, or null if not detected.</returns>
    public ProjectType? DetectProjectType(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            return null;

        var projectTypes = _profileRegistry.GetProjectTypes()
            .OrderByDescending(p => p.Priority)
            .ToList();

        foreach (var projectType in projectTypes)
        {
            foreach (var pattern in projectType.DetectFiles)
            {
                try
                {
                    var files = Directory.GetFiles(workingDirectory, pattern, SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                    {
                        return projectType;
                    }
                }
                catch (Exception)
                {
                    // Ignore access errors
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a default run configuration based on the detected project type.
    /// </summary>
    /// <param name="projectType">The detected project type.</param>
    /// <param name="useWatchCommand">Whether to use the watch command if available.</param>
    /// <returns>A run configuration for the project type.</returns>
    public RunConfiguration CreateDefaultConfiguration(ProjectType projectType, bool useWatchCommand = true)
    {
        var command = useWatchCommand && !string.IsNullOrEmpty(projectType.WatchCommand)
            ? projectType.WatchCommand
            : projectType.DefaultCommand;

        return new RunConfiguration
        {
            Id = "default",
            Name = useWatchCommand && !string.IsNullOrEmpty(projectType.WatchCommand) ? "Development" : "Run",
            Command = command,
            IsDefault = true,
            UrlPattern = projectType.UrlPattern
        };
    }

    /// <summary>
    /// Suggests run configurations based on the project type and directory contents.
    /// </summary>
    /// <param name="workingDirectory">The directory to scan.</param>
    /// <param name="projectType">The detected project type.</param>
    /// <returns>A list of suggested run configurations.</returns>
    public List<RunConfiguration> SuggestConfigurations(string workingDirectory, ProjectType projectType)
    {
        var configs = new List<RunConfiguration>();

        // Add default configuration
        if (!string.IsNullOrEmpty(projectType.WatchCommand))
        {
            configs.Add(new RunConfiguration
            {
                Id = "dev",
                Name = "Development",
                Command = projectType.WatchCommand,
                IsDefault = true,
                UrlPattern = projectType.UrlPattern
            });
        }

        configs.Add(new RunConfiguration
        {
            Id = "run",
            Name = "Run",
            Command = projectType.DefaultCommand,
            IsDefault = string.IsNullOrEmpty(projectType.WatchCommand),
            UrlPattern = projectType.UrlPattern
        });

        // For Node.js projects, try to parse package.json scripts
        if (projectType.Id == "nodejs-npm")
        {
            var additionalConfigs = ParsePackageJsonScripts(workingDirectory, projectType);
            foreach (var config in additionalConfigs)
            {
                if (!configs.Any(c => c.Command == config.Command))
                {
                    configs.Add(config);
                }
            }
        }

        return configs;
    }

    /// <summary>
    /// Parses package.json to extract npm scripts as run configurations.
    /// </summary>
    private List<RunConfiguration> ParsePackageJsonScripts(string workingDirectory, ProjectType projectType)
    {
        var configs = new List<RunConfiguration>();
        var packageJsonPath = Path.Combine(workingDirectory, "package.json");

        if (!File.Exists(packageJsonPath))
            return configs;

        try
        {
            var json = File.ReadAllText(packageJsonPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("scripts", out var scripts))
            {
                var interestingScripts = new[] { "start", "dev", "serve", "build", "test" };

                foreach (var script in scripts.EnumerateObject())
                {
                    if (interestingScripts.Contains(script.Name.ToLowerInvariant()))
                    {
                        configs.Add(new RunConfiguration
                        {
                            Id = $"npm-{script.Name}",
                            Name = $"npm {script.Name}",
                            Command = $"npm run {script.Name}",
                            IsDefault = false,
                            UrlPattern = projectType.UrlPattern
                        });
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore parse errors
        }

        return configs;
    }

    /// <summary>
    /// Gets run configurations for a directory, using cached settings or detecting fresh.
    /// </summary>
    /// <param name="workingDirectory">The directory.</param>
    /// <param name="directorySettings">The directory settings (may be modified).</param>
    /// <returns>List of run configurations.</returns>
    public List<RunConfiguration> GetOrCreateConfigurations(string workingDirectory, DirectorySettings directorySettings)
    {
        // If we already have configurations, return them
        if (directorySettings.RunConfigurations.Count > 0)
        {
            return directorySettings.RunConfigurations;
        }

        // Try to detect project type
        var projectType = DetectProjectType(workingDirectory);
        if (projectType == null)
        {
            // No project type detected, return generic shell configuration
            return
            [
                new RunConfiguration
                {
                    Id = "shell",
                    Name = "Shell",
                    Command = "",  // Empty - user must configure
                    IsDefault = true
                }
            ];
        }

        // Cache the detected project type
        directorySettings.DetectedProjectType = projectType.Id;

        // Generate and cache configurations
        var configs = SuggestConfigurations(workingDirectory, projectType);
        directorySettings.RunConfigurations = configs;

        // Set the default active configuration
        var defaultConfig = configs.FirstOrDefault(c => c.IsDefault) ?? configs.FirstOrDefault();
        if (defaultConfig != null)
        {
            directorySettings.ActiveRunConfigurationId = defaultConfig.Id;
        }

        return configs;
    }
}
