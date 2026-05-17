using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IWorktreeOperations
{
    Task<IReadOnlyList<WorktreeInfo>> ListAsync();
    Task<GitOperationResult> CreateAsync(string branch, string targetPath, bool createBranch = false);
    Task<GitOperationResult> RemoveAsync(string worktreePath, bool force = false);
    Task<GitOperationResult> PruneAsync();
    Task<bool> IsWorktreeAsync(string path);
    Task<string?> GetMainWorktreePathAsync();
    Task<GitOperationResult> LockAsync(string worktreePath, string? reason = null);
    Task<GitOperationResult> UnlockAsync(string worktreePath);
}
