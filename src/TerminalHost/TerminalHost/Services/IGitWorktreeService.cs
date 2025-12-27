using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for managing git worktrees.
/// </summary>
public interface IGitWorktreeService
{
    /// <summary>
    /// Gets all worktrees for a repository.
    /// </summary>
    /// <param name="repositoryPath">Path to any directory within the repository.</param>
    /// <returns>List of worktree information.</returns>
    Task<List<WorktreeInfo>> GetWorktreesAsync(string repositoryPath);

    /// <summary>
    /// Creates a new worktree.
    /// </summary>
    /// <param name="repositoryPath">Path to any directory within the repository.</param>
    /// <param name="worktreePath">Path where the worktree should be created.</param>
    /// <param name="branch">Branch name to checkout (or create if createBranch is true).</param>
    /// <param name="createBranch">Whether to create a new branch.</param>
    /// <returns>Success status and optional error message.</returns>
    Task<(bool Success, string? Error)> CreateWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        string branch,
        bool createBranch = false);

    /// <summary>
    /// Removes a worktree.
    /// </summary>
    /// <param name="repositoryPath">Path to any directory within the repository.</param>
    /// <param name="worktreePath">Path to the worktree to remove.</param>
    /// <param name="force">Whether to force removal even if worktree has uncommitted changes.</param>
    /// <returns>Success status and optional error message.</returns>
    Task<(bool Success, string? Error)> RemoveWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        bool force = false);

    /// <summary>
    /// Gets the root directory of the git repository.
    /// </summary>
    /// <param name="path">Any path within the repository.</param>
    /// <returns>The repository root path, or null if not a git repository.</returns>
    Task<string?> GetRepositoryRootAsync(string path);

    /// <summary>
    /// Creates a new branch from a worktree's current state.
    /// </summary>
    /// <param name="repositoryPath">Path to any directory within the repository.</param>
    /// <param name="worktreePath">Path to the worktree.</param>
    /// <param name="branchName">Name for the new branch.</param>
    /// <returns>Success status and optional error message.</returns>
    Task<(bool Success, string? Error)> CreateBranchFromWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        string branchName);

    /// <summary>
    /// Gets available branches that can be used for creating worktrees.
    /// </summary>
    /// <param name="repositoryPath">Path to any directory within the repository.</param>
    /// <returns>List of branch names.</returns>
    Task<List<string>> GetAvailableBranchesAsync(string repositoryPath);
}
