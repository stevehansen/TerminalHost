using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Default <see cref="IGitWorkspace"/> implementation. Phase 1 (issue #50): pure pass-through
/// delegation to the four legacy git services. Sub-operation classes are 1:1 forwarders by
/// design — the abstraction collapses once the legacy services are demoted in Phase 4.
/// </summary>
internal sealed class GitWorkspace : IGitWorkspace
{
    private readonly IGitStatusService _status;
    private readonly IGitHubService _gitHub;
    private readonly IGitPrService _gitPr;
    private readonly IGitWorktreeService _worktrees;
    private readonly Action<GitWorkspace> _onDispose;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private GitSnapshot? _last;
    private int _disposed;

    public GitWorkspace(
        string workingDirectory,
        IGitStatusService status,
        IGitHubService gitHub,
        IGitPrService gitPr,
        IGitWorktreeService worktrees,
        Action<GitWorkspace> onDispose)
    {
        WorkingDirectory = workingDirectory;
        _status = status;
        _gitHub = gitHub;
        _gitPr = gitPr;
        _worktrees = worktrees;
        _onDispose = onDispose;

        Stash = new StashOps(this);
        Worktrees = new WorktreeOps(this);
        Submodules = new SubmoduleOps(this);
        Reflog = new ReflogOps(this);
        Tags = new TagOps(this);
        Conflicts = new ConflictOps(this);
        Branches = new BranchOps(this);
        GitHub = new GitHubOps(this);
    }

    public string WorkingDirectory { get; }

    public GitSnapshot? Last => Volatile.Read(ref _last);

    public event EventHandler<GitSnapshot>? SnapshotChanged;

    public async Task<GitSnapshot> RefreshAsync(GitSnapshotScope scope = GitSnapshotScope.Full, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(GitWorkspace));

        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The underlying services don't yet accept CancellationToken (Phase 1 constraint);
            // ct is only honored at the gate wait above.
            var statusTask = _status.GetGitStatusAsync(WorkingDirectory);
            var worktreesTask = _worktrees.ListWorktreesAsync(WorkingDirectory);
            var mergeTask = _status.IsMergeInProgressAsync(WorkingDirectory);
            var rebaseTask = _status.IsRebaseInProgressAsync(WorkingDirectory);
            var prTask = scope == GitSnapshotScope.Full
                ? _gitHub.GetCurrentPullRequestAsync(WorkingDirectory)
                : Task.FromResult<GitHubPullRequest?>(null);

            // Await each individually so a second failing task isn't swallowed by WhenAll's
            // single-exception unwrap. Order is fixed: any failure surfaces deterministically.
            var status = await statusTask.ConfigureAwait(false);
            var worktrees = await worktreesTask.ConfigureAwait(false);
            var isMerge = await mergeTask.ConfigureAwait(false);
            var isRebase = await rebaseTask.ConfigureAwait(false);
            var pr = await prTask.ConfigureAwait(false);

            var snapshot = new GitSnapshot(
                Status: status,
                CurrentBranch: status.BranchName,
                Ahead: status.AheadCount,
                Behind: status.BehindCount,
                PullRequest: pr,
                Worktrees: worktrees,
                IsMergeInProgress: isMerge,
                IsRebaseInProgress: isRebase,
                CapturedAt: DateTimeOffset.UtcNow);

            Volatile.Write(ref _last, snapshot);
            RaiseSnapshotChanged(snapshot);
            return snapshot;
        }
        finally
        {
            // Skip release if dispose flipped the flag while we held the gate — DisposeAsync
            // waits for the gate before disposing it, so the gate object is still alive here.
            if (Volatile.Read(ref _disposed) == 0)
                _refreshGate.Release();
        }
    }

    private void RaiseSnapshotChanged(GitSnapshot snapshot)
    {
        var handlers = SnapshotChanged;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((EventHandler<GitSnapshot>)handler).Invoke(this, snapshot); }
            catch { /* Subscriber bugs must not abort the refresh. */ }
        }
    }

    public Task<GitOperationResult> StageAsync(string filePath) => _status.StageFileAsync(WorkingDirectory, filePath);
    public Task<GitOperationResult> UnstageAsync(string filePath) => _status.UnstageFileAsync(WorkingDirectory, filePath);
    public Task<GitOperationResult> StageAllAsync() => _status.StageAllAsync(WorkingDirectory);
    public Task<GitOperationResult> DiscardAsync(string filePath) => _status.DiscardChangesAsync(WorkingDirectory, filePath);
    public Task<GitOperationResult> CommitAsync(string message, bool amend = false) => _status.CreateCommitAsync(WorkingDirectory, message, amend);
    public Task<GitOperationResult> PushAsync() => _status.PushAsync(WorkingDirectory);
    public Task<GitOperationResult> PullRebaseAsync() => _status.PullRebaseAsync(WorkingDirectory);
    public Task<GitOperationResult> CheckoutAsync(string branch) => _status.CheckoutBranchAsync(WorkingDirectory, branch);

    public async Task<IReadOnlyList<GitFileStatus>> GetChangedFilesAsync()
        => await _status.GetModifiedFilesAsync(WorkingDirectory).ConfigureAwait(false);

    public Task<string?> GetFileDiffAsync(string filePath, bool staged = false)
        => _status.GetFileDiffAsync(WorkingDirectory, filePath, staged);

    public Task<string?> GetStagedDiffAsync() => _status.GetStagedDiffAsync(WorkingDirectory);

    public async Task<IReadOnlyList<GitCommit>> GetHistoryAsync(GitHistoryQuery? query = null)
    {
        var q = query ?? new GitHistoryQuery();
        return await _status.GetCommitHistoryAsync(
            WorkingDirectory,
            q.Count,
            q.Author,
            q.FilePath,
            q.SearchText,
            q.AfterDate,
            q.BeforeDate).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync()
        => await _status.GetBranchesAsync(WorkingDirectory).ConfigureAwait(false);

    public IStashOperations Stash { get; }
    public IWorktreeOperations Worktrees { get; }
    public ISubmoduleOperations Submodules { get; }
    public IReflogOperations Reflog { get; }
    public ITagOperations Tags { get; }
    public IConflictOperations Conflicts { get; }
    public IBranchOperations Branches { get; }
    public IGitHubOperations GitHub { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _onDispose(this);
        // Wait for any in-flight RefreshAsync to finish before disposing the semaphore.
        // RefreshAsync's finally checks _disposed and skips Release; we acquire and dispose.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        _refreshGate.Dispose();
    }

    // ---- Sub-operation forwarders ----

    private sealed class StashOps(GitWorkspace ws) : IStashOperations
    {
        public Task<List<GitStashEntry>> GetStashListAsync() => ws._status.GetStashListAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> CreateStashAsync(string? message = null, bool includeUntracked = false)
            => ws._status.CreateStashAsync(ws.WorkingDirectory, message, includeUntracked);
        public Task<GitOperationResult> ApplyStashAsync(int index) => ws._status.ApplyStashAsync(ws.WorkingDirectory, index);
        public Task<GitOperationResult> PopStashAsync(int index) => ws._status.PopStashAsync(ws.WorkingDirectory, index);
        public Task<GitOperationResult> DropStashAsync(int index) => ws._status.DropStashAsync(ws.WorkingDirectory, index);
        public Task<GitOperationResult> CreateBranchFromStashAsync(string branchName, int index)
            => ws._status.CreateBranchFromStashAsync(ws.WorkingDirectory, branchName, index);
    }

    private sealed class WorktreeOps(GitWorkspace ws) : IWorktreeOperations
    {
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync() => ws._worktrees.ListWorktreesAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> CreateAsync(string branch, string targetPath, bool createBranch = false)
            => ws._worktrees.CreateWorktreeAsync(ws.WorkingDirectory, branch, targetPath, createBranch);
        public Task<GitOperationResult> RemoveAsync(string worktreePath, bool force = false)
            => ws._worktrees.RemoveWorktreeAsync(worktreePath, force);
        public Task<GitOperationResult> PruneAsync() => ws._worktrees.PruneWorktreesAsync(ws.WorkingDirectory);
        public Task<bool> IsWorktreeAsync(string path) => ws._worktrees.IsWorktreeAsync(path);
        public Task<string?> GetMainWorktreePathAsync() => ws._worktrees.GetMainWorktreePathAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> LockAsync(string worktreePath, string? reason = null)
            => ws._worktrees.LockWorktreeAsync(worktreePath, reason);
        public Task<GitOperationResult> UnlockAsync(string worktreePath) => ws._worktrees.UnlockWorktreeAsync(worktreePath);
    }

    private sealed class SubmoduleOps(GitWorkspace ws) : ISubmoduleOperations
    {
        public Task<List<SubmoduleInfo>> GetSubmodulesAsync() => ws._status.GetSubmodulesAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> InitializeAsync(string submodulePath) => ws._status.InitializeSubmoduleAsync(ws.WorkingDirectory, submodulePath);
        public Task<GitOperationResult> UpdateAsync(string submodulePath) => ws._status.UpdateSubmoduleAsync(ws.WorkingDirectory, submodulePath);
        public Task<GitOperationResult> UpdateToLatestAsync(string submodulePath) => ws._status.UpdateSubmoduleToLatestAsync(ws.WorkingDirectory, submodulePath);
    }

    private sealed class ReflogOps(GitWorkspace ws) : IReflogOperations
    {
        public Task<List<GitReflogEntry>> GetReflogAsync(int count = 50) => ws._status.GetReflogAsync(ws.WorkingDirectory, count);
        public Task<GitOperationResult> CreateBranchFromRefAsync(string branchName, string refSpec)
            => ws._status.CreateBranchFromRefAsync(ws.WorkingDirectory, branchName, refSpec);
        public Task<GitOperationResult> ResetAsync(string targetRef, ResetMode mode = ResetMode.Mixed)
            => ws._status.ResetAsync(ws.WorkingDirectory, targetRef, mode);
    }

    private sealed class TagOps(GitWorkspace ws) : ITagOperations
    {
        public Task<List<GitTag>> GetTagsAsync() => ws._status.GetTagsAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> CreateTagAsync(string tagName, string? message = null, string? commitHash = null)
            => ws._status.CreateTagAsync(ws.WorkingDirectory, tagName, message, commitHash);
        public Task<GitOperationResult> DeleteTagAsync(string tagName) => ws._status.DeleteTagAsync(ws.WorkingDirectory, tagName);
        public Task<GitOperationResult> PushTagAsync(string tagName) => ws._status.PushTagAsync(ws.WorkingDirectory, tagName);
        public Task<GitOperationResult> PushAllTagsAsync() => ws._status.PushAllTagsAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> DeleteRemoteTagAsync(string tagName) => ws._status.DeleteRemoteTagAsync(ws.WorkingDirectory, tagName);
    }

    private sealed class ConflictOps(GitWorkspace ws) : IConflictOperations
    {
        public Task<bool> IsMergeInProgressAsync() => ws._status.IsMergeInProgressAsync(ws.WorkingDirectory);
        public Task<bool> IsRebaseInProgressAsync() => ws._status.IsRebaseInProgressAsync(ws.WorkingDirectory);
        public Task<ConflictInfo?> ParseConflictFileAsync(string filePath) => ws._status.ParseConflictFileAsync(ws.WorkingDirectory, filePath);
        public Task<GitOperationResult> MarkResolvedAsync(string filePath) => ws._status.MarkResolvedAsync(ws.WorkingDirectory, filePath);

        public Task<GitOperationResult> MergeAbortAsync() => ws._status.MergeAbortAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> MergeContinueAsync() => ws._status.MergeContinueAsync(ws.WorkingDirectory);

        public Task<GitOperationResult> RebaseAsync(string ontoBranch) => ws._status.RebaseAsync(ws.WorkingDirectory, ontoBranch);
        public Task<GitOperationResult> RebaseContinueAsync() => ws._status.RebaseContinueAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> RebaseAbortAsync() => ws._status.RebaseAbortAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> RebaseSkipAsync() => ws._status.RebaseSkipAsync(ws.WorkingDirectory);

        public Task<GitOperationResult> CherryPickAsync(string commitHash, bool noCommit = false)
            => ws._status.CherryPickAsync(ws.WorkingDirectory, commitHash, noCommit);
        public Task<GitOperationResult> CherryPickContinueAsync() => ws._status.CherryPickContinueAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> CherryPickAbortAsync() => ws._status.CherryPickAbortAsync(ws.WorkingDirectory);

        public Task<GitOperationResult> RevertAsync(string commitHash, bool noCommit = false)
            => ws._status.RevertAsync(ws.WorkingDirectory, commitHash, noCommit);
        public Task<GitOperationResult> RevertContinueAsync() => ws._status.RevertContinueAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> RevertAbortAsync() => ws._status.RevertAbortAsync(ws.WorkingDirectory);
    }

    private sealed class BranchOps(GitWorkspace ws) : IBranchOperations
    {
        public Task<GitOperationResult> CreateAsync(string branchName) => ws._status.CreateBranchAsync(ws.WorkingDirectory, branchName);
        public Task<GitOperationResult> DeleteAsync(string branchName, bool force = false)
            => ws._status.DeleteBranchAsync(ws.WorkingDirectory, branchName, force);
        public Task<GitOperationResult> DeleteRemoteAsync(string remoteName, string branchName)
            => ws._status.DeleteRemoteBranchAsync(ws.WorkingDirectory, remoteName, branchName);
        public Task<GitOperationResult> FetchAllAsync() => ws._status.FetchAllAsync(ws.WorkingDirectory);
        public Task<GitOperationResult> PushBranchAsync(string branchName) => ws._status.PushBranchAsync(ws.WorkingDirectory, branchName);

        public Task<BranchComparisonResult> CompareAsync(string baseBranch, string compareBranch)
            => ws._status.CompareBranchesAsync(ws.WorkingDirectory, baseBranch, compareBranch);
        public Task<List<GitCommit>> GetCommitsBetweenAsync(string fromRef, string toRef)
            => ws._status.GetCommitsBetweenAsync(ws.WorkingDirectory, fromRef, toRef);
        public Task<List<GitFileStatus>> GetChangedFilesBetweenAsync(string baseBranch, string compareBranch)
            => ws._status.GetChangedFilesBetweenBranchesAsync(ws.WorkingDirectory, baseBranch, compareBranch);
        public Task<string?> GetFileDiffBetweenAsync(string baseBranch, string compareBranch, string filePath)
            => ws._status.GetFileDiffBetweenBranchesAsync(ws.WorkingDirectory, baseBranch, compareBranch, filePath);
        public Task<List<GitBranch>> GetKeyBranchesAsync(IEnumerable<string> keyBranchPatterns)
            => ws._status.GetKeyBranchesAsync(ws.WorkingDirectory, keyBranchPatterns);
        public Task<(int Ahead, int Behind)> GetAheadBehindAsync(string branch, string compareTo)
            => ws._status.GetAheadBehindAsync(ws.WorkingDirectory, branch, compareTo);
        public Task<GitOperationResult> UpdateBranchPointerAsync(string branchName, string targetRef)
            => ws._status.UpdateBranchPointerAsync(ws.WorkingDirectory, branchName, targetRef);

        public Task<GitOperationResult> FastForwardAsync(string targetBranch) => ws._status.FastForwardAsync(ws.WorkingDirectory, targetBranch);
        public Task<(bool CanFastForward, int CommitCount, string? Error)> CheckFastForwardAsync(string targetBranch)
            => ws._status.CheckFastForwardAsync(ws.WorkingDirectory, targetBranch);
    }

    private sealed class GitHubOps(GitWorkspace ws) : IGitHubOperations
    {
        public Task<GitHubPullRequest?> GetPullRequestDetailsAsync(string repo, int prNumber)
            => ws._gitHub.GetPullRequestDetailsAsync(repo, prNumber);
        public Task<List<GitHubPrFile>> GetPullRequestFilesAsync(string repo, int prNumber)
            => ws._gitHub.GetPullRequestFilesAsync(repo, prNumber);
        public Task<string?> GetPullRequestFileDiffAsync(string repo, int prNumber, string filePath)
            => ws._gitHub.GetPullRequestFileDiffAsync(repo, prNumber, filePath);
        public Task<(bool success, string? error)> CheckoutPullRequestAsync(int prNumber)
            => ws._gitHub.CheckoutPullRequestAsync(ws.WorkingDirectory, prNumber);
        public Task<bool> ApprovePullRequestAsync(int prNumber, string? comment = null)
            => ws._gitHub.ApprovePullRequestAsync(ws.WorkingDirectory, prNumber, comment);
        public Task<bool> RequestChangesAsync(int prNumber, string comment)
            => ws._gitHub.RequestChangesAsync(ws.WorkingDirectory, prNumber, comment);
        public Task<bool> CommentOnPullRequestAsync(int prNumber, string comment)
            => ws._gitHub.CommentOnPullRequestAsync(ws.WorkingDirectory, prNumber, comment);
        public Task<bool> MergePullRequestAsync(int prNumber, string method = "squash", string? commitSubject = null)
            => ws._gitHub.MergePullRequestAsync(ws.WorkingDirectory, prNumber, method, commitSubject);
        public Task<PrComments?> GetPullRequestCommentsAsync(string repo, int prNumber)
            => ws._gitHub.GetPullRequestCommentsAsync(repo, prNumber);
    }
}
