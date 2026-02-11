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
    Task<GitOperationResult> PullRebaseAsync(string workingDirectory);
    Task<GitOperationResult> PushAsync(string workingDirectory);
    Task<GitOperationResult> PushBranchAsync(string workingDirectory, string branchName);

    // Staging operations
    Task<GitOperationResult> StageFileAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> UnstageFileAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> StageAllAsync(string workingDirectory);
    Task<GitOperationResult> UnstageAllAsync(string workingDirectory);
    Task<GitOperationResult> DiscardChangesAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> DiscardAllChangesAsync(string workingDirectory);

    // Hunk-level staging
    Task<GitOperationResult> StageHunkAsync(string workingDirectory, string patchContent);
    Task<GitOperationResult> UnstageHunkAsync(string workingDirectory, string patchContent);

    // Commit operations
    Task<string?> GetStagedDiffAsync(string workingDirectory);
    Task<GitOperationResult> CreateCommitAsync(string workingDirectory, string message, bool amend = false);
    Task<GitOperationResult> UndoLastCommitAsync(string workingDirectory);

    // Commit history
    Task<List<GitCommit>> GetCommitHistoryAsync(string workingDirectory, int count = 50, string? author = null,
        string? filePath = null, string? searchText = null,
        DateTimeOffset? afterDate = null, DateTimeOffset? beforeDate = null);
    Task<GitCommitDetails?> GetCommitDetailsAsync(string workingDirectory, string hash);
    Task<string?> GetCommitDiffAsync(string workingDirectory, string hash, string? filePath = null);

    // Stash operations
    Task<List<GitStashEntry>> GetStashListAsync(string workingDirectory);
    Task<GitOperationResult> CreateStashAsync(string workingDirectory, string? message = null, bool includeUntracked = false);
    Task<GitOperationResult> ApplyStashAsync(string workingDirectory, int index);
    Task<GitOperationResult> PopStashAsync(string workingDirectory, int index);
    Task<GitOperationResult> DropStashAsync(string workingDirectory, int index);
    Task<GitOperationResult> CreateBranchFromStashAsync(string workingDirectory, string branchName, int index);

    // File history and blame
    Task<GitBlameResult?> GetFileBlameAsync(string workingDirectory, string filePath);
    Task<string?> GetFileContentAtCommitAsync(string workingDirectory, string filePath, string commitHash);
    Task<string?> GetFileDiffBetweenCommitsAsync(string workingDirectory, string filePath, string fromHash, string toHash);

    // Reflog
    Task<List<GitReflogEntry>> GetReflogAsync(string workingDirectory, int count = 50);

    // Cherry-pick and Revert
    Task<GitOperationResult> CherryPickAsync(string workingDirectory, string commitHash, bool noCommit = false);
    Task<GitOperationResult> CherryPickContinueAsync(string workingDirectory);
    Task<GitOperationResult> CherryPickAbortAsync(string workingDirectory);
    Task<GitOperationResult> RevertAsync(string workingDirectory, string commitHash, bool noCommit = false);
    Task<GitOperationResult> RevertContinueAsync(string workingDirectory);
    Task<GitOperationResult> RevertAbortAsync(string workingDirectory);

    // Branch from reflog
    Task<GitOperationResult> CreateBranchFromRefAsync(string workingDirectory, string branchName, string refSpec);

    // Reset operations
    Task<GitOperationResult> ResetAsync(string workingDirectory, string targetRef, ResetMode mode = ResetMode.Mixed);

    // Fast-forward (merge --ff-only)
    Task<GitOperationResult> FastForwardAsync(string workingDirectory, string targetBranch);
    Task<(bool CanFastForward, int CommitCount, string? Error)> CheckFastForwardAsync(string workingDirectory, string targetBranch);

    // Standalone rebase
    Task<GitOperationResult> RebaseAsync(string workingDirectory, string ontoBranch);
    Task<GitOperationResult> RebaseContinueAsync(string workingDirectory);
    Task<GitOperationResult> RebaseAbortAsync(string workingDirectory);
    Task<GitOperationResult> RebaseSkipAsync(string workingDirectory);
    Task<bool> IsRebaseInProgressAsync(string workingDirectory);

    // Branch comparison
    Task<BranchComparisonResult> CompareBranchesAsync(string workingDirectory, string baseBranch, string compareBranch);
    Task<List<GitCommit>> GetCommitsBetweenAsync(string workingDirectory, string fromRef, string toRef);

    /// <summary>
    /// Gets the list of changed files between two branches using git diff --name-status.
    /// </summary>
    Task<List<GitFileStatus>> GetChangedFilesBetweenBranchesAsync(string workingDirectory, string baseBranch, string compareBranch);

    /// <summary>
    /// Gets the diff for a specific file between two branches.
    /// </summary>
    Task<string?> GetFileDiffBetweenBranchesAsync(string workingDirectory, string baseBranch, string compareBranch, string filePath);

    // Key branches detection
    Task<List<GitBranch>> GetKeyBranchesAsync(string workingDirectory, IEnumerable<string> keyBranchPatterns);

    /// <summary>
    /// Gets the ahead/behind commit counts between two branches.
    /// </summary>
    /// <param name="workingDirectory">The repository path.</param>
    /// <param name="branch">The branch to check (typically current branch).</param>
    /// <param name="compareTo">The branch to compare against (typically a key branch).</param>
    /// <returns>Tuple of (ahead count, behind count) where ahead = commits in branch not in compareTo.</returns>
    Task<(int Ahead, int Behind)> GetAheadBehindAsync(string workingDirectory, string branch, string compareTo);

    /// <summary>
    /// Moves a local branch pointer to a target reference without checkout.
    /// Uses 'git branch -f {branchName} {targetRef}'.
    /// </summary>
    /// <param name="workingDirectory">The repository path.</param>
    /// <param name="branchName">The branch to update (must not be the current branch).</param>
    /// <param name="targetRef">The commit/branch to point to.</param>
    Task<GitOperationResult> UpdateBranchPointerAsync(string workingDirectory, string branchName, string targetRef);

    // Tag operations
    Task<List<GitTag>> GetTagsAsync(string workingDirectory);
    Task<GitOperationResult> CreateTagAsync(string workingDirectory, string tagName, string? message = null, string? commitHash = null);
    Task<GitOperationResult> DeleteTagAsync(string workingDirectory, string tagName);
    Task<GitOperationResult> PushTagAsync(string workingDirectory, string tagName);
    Task<GitOperationResult> PushAllTagsAsync(string workingDirectory);
    Task<GitOperationResult> DeleteRemoteTagAsync(string workingDirectory, string tagName);

    // Submodule operations
    /// <summary>
    /// Gets the list of submodules with their current status.
    /// </summary>
    Task<List<SubmoduleInfo>> GetSubmodulesAsync(string workingDirectory);

    /// <summary>
    /// Initializes a submodule.
    /// </summary>
    Task<GitOperationResult> InitializeSubmoduleAsync(string workingDirectory, string submodulePath);

    /// <summary>
    /// Updates a submodule to the tracked commit.
    /// </summary>
    Task<GitOperationResult> UpdateSubmoduleAsync(string workingDirectory, string submodulePath);

    /// <summary>
    /// Updates a submodule to the latest commit from remote (--remote).
    /// </summary>
    Task<GitOperationResult> UpdateSubmoduleToLatestAsync(string workingDirectory, string submodulePath);

    // Merge conflict resolution
    Task<bool> IsMergeInProgressAsync(string workingDirectory);
    Task<ConflictInfo?> ParseConflictFileAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> MarkResolvedAsync(string workingDirectory, string filePath);
    Task<GitOperationResult> MergeAbortAsync(string workingDirectory);
    Task<GitOperationResult> MergeContinueAsync(string workingDirectory);
}
