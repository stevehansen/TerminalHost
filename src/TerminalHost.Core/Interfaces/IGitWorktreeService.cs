using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for managing git worktrees.
/// </summary>
public interface IGitWorktreeService
{
    /// <summary>
    /// Lists all worktrees for the repository at the given path.
    /// </summary>
    /// <param name="repoPath">Path to any worktree of the repository.</param>
    /// <returns>List of worktree information.</returns>
    Task<IReadOnlyList<WorktreeInfo>> ListWorktreesAsync(string repoPath);

    /// <summary>
    /// Creates a new worktree.
    /// </summary>
    /// <param name="repoPath">Path to any worktree of the repository.</param>
    /// <param name="branch">Branch to checkout in the new worktree.</param>
    /// <param name="targetPath">Path where the worktree should be created.</param>
    /// <param name="createBranch">If true, creates a new branch with the given name.</param>
    /// <returns>Operation result with success status and any error message.</returns>
    Task<GitOperationResult> CreateWorktreeAsync(string repoPath, string branch, string targetPath, bool createBranch = false);

    /// <summary>
    /// Removes a worktree.
    /// </summary>
    /// <param name="worktreePath">Path to the worktree to remove.</param>
    /// <param name="force">If true, removes even if there are uncommitted changes.</param>
    /// <returns>Operation result with success status and any error message.</returns>
    Task<GitOperationResult> RemoveWorktreeAsync(string worktreePath, bool force = false);

    /// <summary>
    /// Prunes worktree information for worktrees that no longer exist on disk.
    /// </summary>
    /// <param name="repoPath">Path to any worktree of the repository.</param>
    /// <returns>Operation result with success status and any error message.</returns>
    Task<GitOperationResult> PruneWorktreesAsync(string repoPath);

    /// <summary>
    /// Checks if the given path is a git worktree (linked worktree, not the main repo).
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns>True if the path is a linked worktree.</returns>
    Task<bool> IsWorktreeAsync(string path);

    /// <summary>
    /// Gets the path to the main worktree (original repository) from any worktree path.
    /// </summary>
    /// <param name="worktreePath">Path to any worktree.</param>
    /// <returns>Path to the main worktree, or null if not a git repository.</returns>
    Task<string?> GetMainWorktreePathAsync(string worktreePath);

    /// <summary>
    /// Locks a worktree to prevent accidental removal.
    /// </summary>
    /// <param name="worktreePath">Path to the worktree to lock.</param>
    /// <param name="reason">Optional reason for locking.</param>
    /// <returns>Operation result with success status and any error message.</returns>
    Task<GitOperationResult> LockWorktreeAsync(string worktreePath, string? reason = null);

    /// <summary>
    /// Unlocks a previously locked worktree.
    /// </summary>
    /// <param name="worktreePath">Path to the worktree to unlock.</param>
    /// <returns>Operation result with success status and any error message.</returns>
    Task<GitOperationResult> UnlockWorktreeAsync(string worktreePath);
}
