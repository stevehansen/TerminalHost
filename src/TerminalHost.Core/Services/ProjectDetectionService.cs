using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for auto-detecting project types and generating run configurations.
/// </summary>
public sealed class ProjectDetectionService : IProjectDetectionService
{
    private readonly IProfileRegistry _profileRegistry;
    private readonly IFileSystem _fileSystem;

    public ProjectDetectionService(IProfileRegistry profileRegistry, IFileSystem fileSystem)
    {
        _profileRegistry = profileRegistry;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Detects the project type by scanning for marker files in the directory.
    /// </summary>
    /// <param name="workingDirectory">The directory to scan.</param>
    /// <returns>The detected project type, or null if not detected.</returns>
    public ProjectType? DetectProjectType(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !_fileSystem.DirectoryExists(workingDirectory))
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
                    var files = _fileSystem.GetFiles(workingDirectory, pattern, SearchOption.TopDirectoryOnly);
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
    /// Finds the best .csproj to run for a .NET project.
    /// </summary>
    /// <param name="workingDirectory">The directory to scan.</param>
    /// <returns>Relative path to the .csproj, or null if a single runnable project is in root.</returns>
    public string? FindDotNetProject(string workingDirectory)
    {
        try
        {
            // First, check for .sln file and parse it
            var slnFiles = _fileSystem.GetFiles(workingDirectory, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                var projectFromSln = FindRunnableProjectFromSolution(slnFiles[0], workingDirectory);
                if (projectFromSln != null)
                    return projectFromSln;
            }

            // Check for .csproj in root directory
            var rootCsprojs = _fileSystem.GetFiles(workingDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
            if (rootCsprojs.Length == 1)
            {
                // Single project in root - check if it's runnable
                if (IsRunnableProject(rootCsprojs[0]))
                    return null; // No --project needed, dotnet run will find it
            }

            // Search subdirectories (max 2 levels deep)
            var allCsprojs = FindCsprojFiles(workingDirectory, maxDepth: 2);
            var runnableProjects = allCsprojs
                .Where(IsRunnableProject)
                .ToList();

            if (runnableProjects.Count == 0)
                return null;

            // Prefer projects that look like the main app (not tests, not shared libs)
            var mainProject = runnableProjects
                .OrderByDescending(p => ScoreProject(p, workingDirectory))
                .FirstOrDefault();

            if (mainProject != null)
            {
                // Return relative path
                return GetRelativePath(workingDirectory, mainProject);
            }
        }
        catch (Exception)
        {
            // Ignore errors, fall back to default behavior
        }

        return null;
    }

    /// <summary>
    /// Parses a .sln file to find runnable projects.
    /// </summary>
    private string? FindRunnableProjectFromSolution(string slnPath, string workingDirectory)
    {
        try
        {
            var slnContent = _fileSystem.ReadAllText(slnPath);
            var slnDir = Path.GetDirectoryName(slnPath) ?? workingDirectory;

            // Parse project references: Project("{...}") = "Name", "path\to\project.csproj", "{...}"
            var projectRegex = new Regex(@"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+"",\s*""([^""]+\.csproj)""", RegexOptions.IgnoreCase);
            var matches = projectRegex.Matches(slnContent);

            var runnableProjects = new List<(string path, int score)>
            {
            };

            foreach (Match match in matches)
            {
                var relativePath = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(slnDir, relativePath));

                if (_fileSystem.FileExists(fullPath) && IsRunnableProject(fullPath))
                {
                    var score = ScoreProject(fullPath, workingDirectory);
                    runnableProjects.Add((fullPath, score));
                }
            }

            var best = runnableProjects
                .OrderByDescending(p => p.score)
                .FirstOrDefault();

            if (best.path != null)
            {
                return GetRelativePath(workingDirectory, best.path);
            }
        }
        catch (Exception)
        {
            // Ignore parse errors
        }

        return null;
    }

    /// <summary>
    /// Finds .csproj files up to a certain depth.
    /// </summary>
    private List<string> FindCsprojFiles(string directory, int maxDepth)
    {
        var results = new List<string>();
        FindCsprojFilesRecursive(directory, maxDepth, 0, results);
        return results;
    }

    private void FindCsprojFilesRecursive(string directory, int maxDepth, int currentDepth, List<string> results)
    {
        if (currentDepth > maxDepth)
            return;

        try
        {
            results.AddRange(_fileSystem.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly));

            foreach (var subDir in _fileSystem.GetDirectories(directory))
            {
                // Skip common non-project directories
                var dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith('.') || dirName == "bin" || dirName == "obj" ||
                    dirName == "node_modules" || dirName == "packages")
                    continue;

                FindCsprojFilesRecursive(subDir, maxDepth, currentDepth + 1, results);
            }
        }
        catch (Exception)
        {
            // Ignore access errors
        }
    }

    /// <summary>
    /// Checks if a .csproj is a runnable project (not a class library or test project).
    /// </summary>
    private bool IsRunnableProject(string csprojPath)
    {
        try
        {
            // Use _fileSystem to read XML content
            var content = _fileSystem.ReadAllText(csprojPath);
            var doc = XDocument.Parse(content);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Check OutputType - we want Exe or WinExe
            var outputType = doc.Descendants(ns + "OutputType").FirstOrDefault()?.Value ??
                            doc.Descendants("OutputType").FirstOrDefault()?.Value;

            if (outputType != null)
            {
                if (outputType.Equals("Library", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Check for Sdk attribute - Web and Worker projects are runnable
            var sdk = doc.Root?.Attribute("Sdk")?.Value ?? "";
            if (sdk.Contains("Microsoft.NET.Sdk.Web") || sdk.Contains("Microsoft.NET.Sdk.Worker"))
                return true;

            // If OutputType is Exe or WinExe, it's runnable
            if (outputType != null &&
                (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
                 outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check if it's a test project
            var isTestProject = doc.Descendants(ns + "IsTestProject").FirstOrDefault()?.Value ??
                               doc.Descendants("IsTestProject").FirstOrDefault()?.Value;
            if (isTestProject?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                return false;

            // Check for test framework references
            var packageRefs = doc.Descendants(ns + "PackageReference")
                .Concat(doc.Descendants("PackageReference"))
                .Select(e => e.Attribute("Include")?.Value ?? "")
                .ToList();

            if (packageRefs.Any(p => p.Contains("xunit") || p.Contains("NUnit") ||
                                     p.Contains("MSTest") || p.Contains("Test")))
                return false;

            // Default: assume runnable if OutputType not specified (console app default)
            return outputType == null || !outputType.Equals("Library", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Scores a project to determine which is most likely the main entry point.
    /// </summary>
    private int ScoreProject(string csprojPath, string workingDirectory)
    {
        var score = 0;
        var fileName = Path.GetFileNameWithoutExtension(csprojPath).ToLowerInvariant();
        var dirName = Path.GetFileName(Path.GetDirectoryName(csprojPath) ?? "").ToLowerInvariant();
        var workingDirName = Path.GetFileName(workingDirectory.TrimEnd(Path.DirectorySeparatorChar)).ToLowerInvariant();

        try
        {
            // Parse XML from file system
            var content = _fileSystem.ReadAllText(csprojPath);
            var doc = XDocument.Parse(content);
            var sdk = doc.Root?.Attribute("Sdk")?.Value ?? "";

            // Prefer web projects
            if (sdk.Contains("Microsoft.NET.Sdk.Web"))
                score += 50;

            // Prefer worker services
            if (sdk.Contains("Microsoft.NET.Sdk.Worker"))
                score += 40;
        }
        catch { }

        // Prefer projects in src directory
        if (csprojPath.Contains(Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar))
            score += 20;

        // Prefer projects whose name matches the solution/working directory name
        if (fileName == workingDirName || dirName == workingDirName)
            score += 30;

        // Prefer projects with common main-app names
        if (fileName.EndsWith(".web") || fileName.EndsWith(".api") || fileName.EndsWith(".server"))
            score += 25;

        // Deprioritize common non-main names
        if (fileName.Contains("test") || fileName.Contains("spec"))
            score -= 100;
        if (fileName.Contains("shared") || fileName.Contains("common") || fileName.Contains("core"))
            score -= 10;

        // Prefer shorter paths (closer to root)
        var depth = csprojPath.Count(c => c == Path.DirectorySeparatorChar);
        score -= depth * 2;

        return score;
    }

    /// <summary>
    /// Gets a relative path from a base directory.
    /// </summary>
    private static string GetRelativePath(string basePath, string fullPath)
    {
        var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var fullUri = new Uri(fullPath);
        var relativeUri = baseUri.MakeRelativeUri(fullUri);
        return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
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

        // For .NET projects, find the specific project to run
        string projectFlag = "";
        if (projectType.Id == "dotnet")
        {
            var projectPath = FindDotNetProject(workingDirectory);
            if (!string.IsNullOrEmpty(projectPath))
            {
                projectFlag = $" --project \"{projectPath}\"" ;
            }
        }

        // Add default configuration
        if (!string.IsNullOrEmpty(projectType.WatchCommand))
        {
            configs.Add(new RunConfiguration
            {
                Id = "dev",
                Name = "Development",
                Command = projectType.WatchCommand + projectFlag,
                IsDefault = true,
                UrlPattern = projectType.UrlPattern
            });
        }

        configs.Add(new RunConfiguration
        {
            Id = "run",
            Name = "Run",
            Command = projectType.DefaultCommand + projectFlag,
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

        if (!_fileSystem.FileExists(packageJsonPath))
            return configs;

        try
        {
            var json = _fileSystem.ReadAllText(packageJsonPath);
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
