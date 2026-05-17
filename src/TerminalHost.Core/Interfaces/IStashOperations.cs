using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IStashOperations
{
    Task<List<GitStashEntry>> GetStashListAsync();
    Task<GitOperationResult> CreateStashAsync(string? message = null, bool includeUntracked = false);
    Task<GitOperationResult> ApplyStashAsync(int index);
    Task<GitOperationResult> PopStashAsync(int index);
    Task<GitOperationResult> DropStashAsync(int index);
    Task<GitOperationResult> CreateBranchFromStashAsync(string branchName, int index);
}
