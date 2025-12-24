using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IGitStatusService
{
    Task<GitStatus> GetGitStatusAsync(string workingDirectory);
    Task<List<GitFileStatus>> GetModifiedFilesAsync(string workingDirectory);
    Task<string?> GetFileDiffAsync(string workingDirectory, string filePath, bool staged = false);
    Task<string?> GetFileContentAtHeadAsync(string workingDirectory, string filePath);
    Task<List<GitBranch>> GetBranchesAsync(string workingDirectory);
    Task<GitOperationResult> CheckoutBranchAsync(string workingDirectory, string branchName, bool isRemote = false);
    Task<GitOperationResult> CreateBranchAsync(string workingDirectory, string branchName);
    Task<GitOperationResult> DeleteBranchAsync(string workingDirectory, string branchName, bool force = false);
    Task<GitOperationResult> DeleteRemoteBranchAsync(string workingDirectory, string remoteName, string branchName);
    Task<GitOperationResult> FetchAllAsync(string workingDirectory);
    Task<GitOperationResult> PullAsync(string workingDirectory);

    // Staging operations
    Task<GitOperationResult> StageFileAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> UnstageFileAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> StageAllAsync(string workingDirectory);
    Task<GitOperationResult> UnstageAllAsync(string workingDirectory);
    Task<GitOperationResult> DiscardChangesAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> DiscardAllChangesAsync(string workingDirectory);

    // Commit operations
    Task<GitOperationResult> CreateCommitAsync(string workingDirectory, string message, bool amend = false);

    // Commit history
    Task<List<GitCommit>> GetCommitHistoryAsync(string workingDirectory, int count = 50, string? author = null, string? filePath = null);
    Task<GitCommitDetails?> GetCommitDetailsAsync(string workingDirectory, string hash);
    Task<string?> GetCommitDiffAsync(string workingDirectory, string hash, string? filePath = null);

    // Stash operations
    Task<List<GitStashEntry>> GetStashListAsync(string workingDirectory);
    Task<GitOperationResult> CreateStashAsync(string workingDirectory, string? message = null, bool includeUntracked = false);
    Task<GitOperationResult> ApplyStashAsync(string workingDirectory, int index);
    Task<GitOperationResult> PopStashAsync(string workingDirectory, int index);
    Task<GitOperationResult> DropStashAsync(string workingDirectory, int index);
    Task<GitOperationResult> CreateBranchFromStashAsync(string workingDirectory, string branchName, int index);
}
