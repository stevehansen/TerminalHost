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
    Task<GitOperationResult> PullRebaseAsync(string workingDirectory);
    Task<GitOperationResult> PushAsync(string workingDirectory);

    // Staging operations
    Task<bool> StageFileAsync(string workingDirectory, string filePath);
    Task<bool> UnstageFileAsync(string workingDirectory, string filePath);
    Task<bool> StageAllAsync(string workingDirectory);
    Task<bool> UnstageAllAsync(string workingDirectory);
    Task<bool> DiscardChangesAsync(string workingDirectory, string filePath);

    // Commit operations
    Task<(bool Success, string? Error)> CommitAsync(string workingDirectory, string message, bool amend = false);

    // Commit history operations
    Task<List<GitCommit>> GetCommitHistoryAsync(string workingDirectory, int skip = 0, int take = 50, string? author = null);
    Task<GitCommitDetails?> GetCommitDetailsAsync(string workingDirectory, string commitHash);
    Task<string?> GetFileDiffInCommitAsync(string workingDirectory, string commitHash, string filePath);

    // Stash operations
    Task<List<GitStashEntry>> GetStashListAsync(string workingDirectory);
    Task<(bool Success, string? Error)> StashAsync(string workingDirectory, string? message = null, bool includeUntracked = false);
    Task<(bool Success, string? Error)> StashApplyAsync(string workingDirectory, int index);
    Task<(bool Success, string? Error)> StashPopAsync(string workingDirectory, int index);
    Task<(bool Success, string? Error)> StashDropAsync(string workingDirectory, int index);
    Task<(bool Success, string? Error)> StashBranchAsync(string workingDirectory, int index, string branchName);

    // File history and blame operations
    Task<GitBlameResult?> GetFileBlameAsync(string workingDirectory, string filePath);
    Task<List<GitCommit>> GetFileHistoryAsync(string workingDirectory, string filePath, int skip = 0, int take = 50);
    Task<string?> GetFileContentAtCommitAsync(string workingDirectory, string commitHash, string filePath);

    // Reflog operations
    Task<List<GitReflogEntry>> GetReflogAsync(string workingDirectory, int take = 100);
    Task<(bool Success, string? Error)> CreateBranchFromRefAsync(string workingDirectory, string refSpec, string branchName);

    // Cherry-pick operations
    Task<(bool Success, string? Error)> CherryPickAsync(string workingDirectory, string commitHash);
    Task<(bool Success, string? Error)> CherryPickContinueAsync(string workingDirectory);
    Task<(bool Success, string? Error)> CherryPickAbortAsync(string workingDirectory);

    // Revert operations
    Task<(bool Success, string? Error)> RevertAsync(string workingDirectory, string commitHash);
    Task<(bool Success, string? Error)> RevertContinueAsync(string workingDirectory);
    Task<(bool Success, string? Error)> RevertAbortAsync(string workingDirectory);

    // Gitignore operations
    /// <summary>
    /// Gets the set of files that are ignored by .gitignore.
    /// Returns full paths for all ignored files.
    /// </summary>
    Task<HashSet<string>> GetIgnoredFilesAsync(string workingDirectory);
}
