using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IReflogOperations
{
    Task<List<GitReflogEntry>> GetReflogAsync(int count = 50);
    Task<GitOperationResult> CreateBranchFromRefAsync(string branchName, string refSpec);
    Task<GitOperationResult> ResetAsync(string targetRef, ResetMode mode = ResetMode.Mixed);
}
