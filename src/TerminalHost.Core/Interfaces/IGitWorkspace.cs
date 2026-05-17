using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// A workingDirectory-scoped facade over git status, GitHub, PR detection, and worktree services.
/// Holds the working directory so callers don't have to plumb it through every call.
/// </summary>
public interface IGitWorkspace : IAsyncDisposable
{
    string WorkingDirectory { get; }

    /// <summary>
    /// Fan-out refresh that captures status, branch ahead/behind, worktrees, and (when
    /// <paramref name="scope"/> is <see cref="GitSnapshotScope.Full"/>) the current pull request
    /// into a single immutable snapshot. Subsequent reads can use <see cref="Last"/>.
    /// </summary>
    /// <param name="ct">
    /// Cancellation is honored only at the serialization gate; once the underlying fan-out
    /// begins, the legacy git services do not accept tokens, so requests in flight will not
    /// be cancelled until Phase 2 threads tokens through the new ports.
    /// </param>
    Task<GitSnapshot> RefreshAsync(GitSnapshotScope scope = GitSnapshotScope.Full, CancellationToken ct = default);

    /// <summary>The most recent snapshot, or null if <see cref="RefreshAsync"/> has not been called.</summary>
    GitSnapshot? Last { get; }

    /// <summary>Raised whenever a refresh completes with a new snapshot.</summary>
    event EventHandler<GitSnapshot>? SnapshotChanged;

    Task<GitOperationResult> StageAsync(string filePath);
    Task<GitOperationResult> UnstageAsync(string filePath);
    Task<GitOperationResult> StageAllAsync();
    Task<GitOperationResult> DiscardAsync(string filePath);
    Task<GitOperationResult> CommitAsync(string message, bool amend = false);
    Task<GitOperationResult> PushAsync();
    Task<GitOperationResult> PullRebaseAsync();
    Task<GitOperationResult> CheckoutAsync(string branch);

    Task<IReadOnlyList<GitFileStatus>> GetChangedFilesAsync();
    Task<string?> GetFileDiffAsync(string filePath, bool staged = false);
    Task<string?> GetStagedDiffAsync();
    Task<IReadOnlyList<GitCommit>> GetHistoryAsync(GitHistoryQuery? query = null);
    Task<IReadOnlyList<GitBranch>> GetBranchesAsync();

    GitHubPullRequest? CurrentPullRequest => Last?.PullRequest;

    IStashOperations Stash { get; }
    IWorktreeOperations Worktrees { get; }
    ISubmoduleOperations Submodules { get; }
    IReflogOperations Reflog { get; }
    ITagOperations Tags { get; }
    IConflictOperations Conflicts { get; }
    IBranchOperations Branches { get; }
    IGitHubOperations GitHub { get; }
}
