using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IBranchOperations
{
    Task<GitOperationResult> CreateAsync(string branchName);
    Task<GitOperationResult> DeleteAsync(string branchName, bool force = false);
    Task<GitOperationResult> DeleteRemoteAsync(string remoteName, string branchName);
    Task<GitOperationResult> FetchAllAsync();
    Task<GitOperationResult> PushBranchAsync(string branchName);

    Task<BranchComparisonResult> CompareAsync(string baseBranch, string compareBranch);
    Task<List<GitCommit>> GetCommitsBetweenAsync(string fromRef, string toRef);

    /// <summary>
    /// Gets the list of changed files between two branches using git diff --name-status.
    /// </summary>
    Task<List<GitFileStatus>> GetChangedFilesBetweenAsync(string baseBranch, string compareBranch);

    /// <summary>
    /// Gets the diff for a specific file between two branches.
    /// </summary>
    Task<string?> GetFileDiffBetweenAsync(string baseBranch, string compareBranch, string filePath);

    Task<List<GitBranch>> GetKeyBranchesAsync(IEnumerable<string> keyBranchPatterns);

    /// <summary>
    /// Gets the ahead/behind commit counts between two branches.
    /// </summary>
    Task<(int Ahead, int Behind)> GetAheadBehindAsync(string branch, string compareTo);

    /// <summary>
    /// Moves a local branch pointer to a target reference without checkout.
    /// </summary>
    Task<GitOperationResult> UpdateBranchPointerAsync(string branchName, string targetRef);

    Task<GitOperationResult> FastForwardAsync(string targetBranch);
    Task<(bool CanFastForward, int CommitCount, string? Error)> CheckFastForwardAsync(string targetBranch);
}
