using TerminalHost.Domain;

namespace TerminalHost.Services;

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
}
