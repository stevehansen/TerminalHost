using System.IO;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for checking if files/directories are ignored by git using git commands.
/// </summary>
public class GitIgnoreService : IGitIgnoreService
{
    private readonly IGitProcessRunner _gitRunner;
    private readonly IFileSystem _fileSystem;

    public GitIgnoreService(IGitProcessRunner gitRunner, IFileSystem fileSystem)
    {
        _gitRunner = gitRunner;
        _fileSystem = fileSystem;
    }

    public bool IsGitRepository(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory))
            return false;

        var gitDir = Path.Combine(workingDirectory, ".git");
        return _fileSystem.DirectoryExists(gitDir) || _fileSystem.FileExists(gitDir);
    }

    public async Task<HashSet<string>> GetIgnoredPathsAsync(string workingDirectory)
    {
        var ignoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!IsGitRepository(workingDirectory))
            return ignoredPaths;

        try
        {
            // Use git status --ignored --porcelain to get all ignored files
            // Output format: !! path/to/ignored/file
            var result = await _gitRunner.RunGitCommandAsync(
                workingDirectory,
                "status --ignored --porcelain");

            if (string.IsNullOrEmpty(result))
                return ignoredPaths;

            foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Ignored files start with "!! "
                if (line.StartsWith("!! "))
                {
                    var relativePath = line.Substring(3).Trim();

                    // Remove trailing slash from directories
                    relativePath = relativePath.TrimEnd('/');

                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        // Store as full path for easier comparison
                        var fullPath = Path.GetFullPath(Path.Combine(workingDirectory, relativePath));
                        ignoredPaths.Add(fullPath);
                    }
                }
            }
        }
        catch
        {
            // Git command failed - return empty set
        }

        return ignoredPaths;
    }

    public async Task<bool> IsIgnoredAsync(string workingDirectory, string path)
    {
        if (!IsGitRepository(workingDirectory))
            return false;

        try
        {
            // Use git check-ignore to check a single path
            // Returns the path if ignored, empty/null if not ignored
            var relativePath = GetRelativePath(workingDirectory, path);
            var result = await _gitRunner.RunGitCommandAsync(
                workingDirectory,
                $"check-ignore \"{relativePath}\"");

            return !string.IsNullOrEmpty(result);
        }
        catch
        {
            return false;
        }
    }

    private static string GetRelativePath(string workingDirectory, string fullPath)
    {
        var normalizedWorkDir = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(fullPath);

        if (normalizedPath.StartsWith(normalizedWorkDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath.Substring(normalizedWorkDir.Length + 1);
        }

        return fullPath;
    }
}
