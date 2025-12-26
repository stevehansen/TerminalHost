using TerminalHost.Domain;

namespace TerminalHost.Services;

public interface IGitStatusService
{
    Task<GitStatus> GetGitStatusAsync(string workingDirectory);
    Task<List<GitFileStatus>> GetModifiedFilesAsync(string workingDirectory);
    Task<(List<GitFileStatus> Staged, List<GitFileStatus> Unstaged)> GetStagedAndUnstagedFilesAsync(string workingDirectory);
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
    Task<bool> StageFileAsync(string workingDirectory, string filePath);
    Task<bool> UnstageFileAsync(string workingDirectory, string filePath);
    Task<bool> StageAllAsync(string workingDirectory);
    Task<bool> UnstageAllAsync(string workingDirectory);
    Task<bool> DiscardChangesAsync(string workingDirectory, string filePath);

    // Commit operations
    Task<(bool Success, string? Error)> CommitAsync(string workingDirectory, string message, bool amend = false);
}
