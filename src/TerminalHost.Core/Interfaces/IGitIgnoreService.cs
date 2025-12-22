namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for checking if files/directories are ignored by git.
/// </summary>
public interface IGitIgnoreService
{
    /// <summary>
    /// Gets all ignored file/directory paths in a repository.
    /// Returns full paths (case-insensitive HashSet).
    /// </summary>
    Task<HashSet<string>> GetIgnoredPathsAsync(string workingDirectory);

    /// <summary>
    /// Checks if a specific path is ignored by git.
    /// </summary>
    Task<bool> IsIgnoredAsync(string workingDirectory, string path);

    /// <summary>
    /// Checks if the directory is a git repository.
    /// </summary>
    bool IsGitRepository(string workingDirectory);
}
