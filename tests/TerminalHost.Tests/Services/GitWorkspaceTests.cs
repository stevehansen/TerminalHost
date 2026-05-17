using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class GitWorkspaceTests
{
    private readonly Mock<IGitStatusService> _status = new();
    private readonly Mock<IGitHubService> _gitHub = new();
    private readonly Mock<IGitPrService> _gitPr = new();
    private readonly Mock<IGitWorktreeService> _worktrees = new();
    private readonly GitWorkspaceFactory _factory;
    private readonly string _path;
    private readonly string _normalizedPath;

    public GitWorkspaceTests()
    {
        _factory = new GitWorkspaceFactory(_status.Object, _gitHub.Object, _gitPr.Object, _worktrees.Object);
        _path = Path.GetTempPath();
        _normalizedPath = Path.GetFullPath(_path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        _status.Setup(s => s.GetGitStatusAsync(It.IsAny<string>()))
            .ReturnsAsync(new GitStatus
            {
                IsGitRepository = true,
                BranchName = "main",
                AheadCount = 1,
                BehindCount = 2,
            });
        _worktrees.Setup(w => w.ListWorktreesAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<WorktreeInfo>());
        _status.Setup(s => s.IsMergeInProgressAsync(It.IsAny<string>())).ReturnsAsync(false);
        _status.Setup(s => s.IsRebaseInProgressAsync(It.IsAny<string>())).ReturnsAsync(false);
    }

    private async Task<IGitWorkspace> OpenAsync()
    {
        var ws = await _factory.OpenAsync(_path);
        ws.ShouldNotBeNull();
        return ws!;
    }

    [Fact]
    public async Task WorkingDirectory_ReturnsNormalizedPath()
    {
        var ws = await OpenAsync();

        ws.WorkingDirectory.ShouldBe(_normalizedPath);
    }

    [Fact]
    public async Task RefreshAsync_FullScope_CallsStatusWorktreesAndPullRequest()
    {
        var pr = new GitHubPullRequest { Number = 42 };
        _gitHub.Setup(g => g.GetCurrentPullRequestAsync(_normalizedPath)).ReturnsAsync(pr);
        var ws = await OpenAsync();

        var snapshot = await ws.RefreshAsync(GitSnapshotScope.Full);

        snapshot.ShouldNotBeNull();
        snapshot.PullRequest.ShouldBe(pr);
        snapshot.Status.IsGitRepository.ShouldBeTrue();
        snapshot.CurrentBranch.ShouldBe("main");
        snapshot.Ahead.ShouldBe(1);
        snapshot.Behind.ShouldBe(2);
        snapshot.Worktrees.ShouldNotBeNull();
        ws.Last.ShouldBeSameAs(snapshot);

        _status.Verify(s => s.GetGitStatusAsync(_normalizedPath), Times.AtLeastOnce);
        _worktrees.Verify(w => w.ListWorktreesAsync(_normalizedPath), Times.Once);
        _gitHub.Verify(g => g.GetCurrentPullRequestAsync(_normalizedPath), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_LocalOnlyScope_DoesNotCallPullRequest()
    {
        var ws = await OpenAsync();

        var snapshot = await ws.RefreshAsync(GitSnapshotScope.LocalOnly);

        snapshot.PullRequest.ShouldBeNull();
        _gitHub.Verify(g => g.GetCurrentPullRequestAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_RaisesSnapshotChangedAfterPopulatingLast()
    {
        var ws = await OpenAsync();
        GitSnapshot? eventSnapshot = null;
        GitSnapshot? lastAtEventTime = null;
        ws.SnapshotChanged += (_, snap) =>
        {
            eventSnapshot = snap;
            lastAtEventTime = ws.Last;
        };

        var returned = await ws.RefreshAsync(GitSnapshotScope.LocalOnly);

        eventSnapshot.ShouldNotBeNull();
        eventSnapshot.ShouldBeSameAs(returned);
        lastAtEventTime.ShouldBeSameAs(returned);
    }

    [Fact]
    public async Task Last_IsNullBeforeAnyRefresh()
    {
        var ws = await OpenAsync();

        ws.Last.ShouldBeNull();
    }

    [Fact]
    public async Task CurrentPullRequest_AfterFullRefresh_ReturnsPullRequestFromSnapshot()
    {
        var pr = new GitHubPullRequest { Number = 7 };
        _gitHub.Setup(g => g.GetCurrentPullRequestAsync(_normalizedPath)).ReturnsAsync(pr);
        var ws = await OpenAsync();

        await ws.RefreshAsync(GitSnapshotScope.Full);

        ws.CurrentPullRequest.ShouldBe(pr);
    }

    [Fact]
    public async Task CurrentPullRequest_AfterLocalOnlyRefresh_IsNull()
    {
        var ws = await OpenAsync();

        await ws.RefreshAsync(GitSnapshotScope.LocalOnly);

        ws.CurrentPullRequest.ShouldBeNull();
    }

    [Fact]
    public async Task StageAsync_ForwardsToStatusServiceWithWorkingDirectory()
    {
        _status.Setup(s => s.StageFileAsync(_normalizedPath, "file.cs"))
            .ReturnsAsync(new GitOperationResult { Success = true });
        var ws = await OpenAsync();

        var result = await ws.StageAsync("file.cs");

        result.Success.ShouldBeTrue();
        _status.Verify(s => s.StageFileAsync(_normalizedPath, "file.cs"), Times.Once);
    }

    [Fact]
    public async Task CommitAsync_WithAmend_ForwardsAllParameters()
    {
        _status.Setup(s => s.CreateCommitAsync(_normalizedPath, "msg", true))
            .ReturnsAsync(new GitOperationResult { Success = true });
        var ws = await OpenAsync();

        var result = await ws.CommitAsync("msg", amend: true);

        result.Success.ShouldBeTrue();
        _status.Verify(s => s.CreateCommitAsync(_normalizedPath, "msg", true), Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_WithPopulatedQuery_ForwardsAllSixParameters()
    {
        var after = DateTimeOffset.UtcNow.AddDays(-7);
        var before = DateTimeOffset.UtcNow;
        var query = new GitHistoryQuery(
            Count: 25,
            Author: "alice",
            FilePath: "src/foo.cs",
            SearchText: "fix",
            AfterDate: after,
            BeforeDate: before);
        _status.Setup(s => s.GetCommitHistoryAsync(_normalizedPath, 25, "alice", "src/foo.cs", "fix", after, before))
            .ReturnsAsync(new List<GitCommit>());
        var ws = await OpenAsync();

        await ws.GetHistoryAsync(query);

        _status.Verify(s => s.GetCommitHistoryAsync(_normalizedPath, 25, "alice", "src/foo.cs", "fix", after, before), Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_WithNullQuery_UsesDefaultValues()
    {
        _status.Setup(s => s.GetCommitHistoryAsync(_normalizedPath, 50, null, null, null, null, null))
            .ReturnsAsync(new List<GitCommit>());
        var ws = await OpenAsync();

        await ws.GetHistoryAsync(null);

        _status.Verify(s => s.GetCommitHistoryAsync(_normalizedPath, 50, null, null, null, null, null), Times.Once);
    }

    [Fact]
    public async Task Stash_CreateStashAsync_DelegatesToStatusService()
    {
        _status.Setup(s => s.CreateStashAsync(_normalizedPath, "wip", true))
            .ReturnsAsync(new GitOperationResult { Success = true });
        var ws = await OpenAsync();

        var result = await ws.Stash.CreateStashAsync("wip", includeUntracked: true);

        result.Success.ShouldBeTrue();
        _status.Verify(s => s.CreateStashAsync(_normalizedPath, "wip", true), Times.Once);
    }

    [Fact]
    public async Task Worktrees_ListAsync_DelegatesToWorktreeService()
    {
        var ws = await OpenAsync();

        await ws.Worktrees.ListAsync();

        _worktrees.Verify(w => w.ListWorktreesAsync(_normalizedPath), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Submodules_GetSubmodulesAsync_DelegatesToStatusService()
    {
        _status.Setup(s => s.GetSubmodulesAsync(_normalizedPath)).ReturnsAsync(new List<SubmoduleInfo>());
        var ws = await OpenAsync();

        await ws.Submodules.GetSubmodulesAsync();

        _status.Verify(s => s.GetSubmodulesAsync(_normalizedPath), Times.Once);
    }

    [Fact]
    public async Task Reflog_GetReflogAsync_DelegatesToStatusServiceWithCount()
    {
        _status.Setup(s => s.GetReflogAsync(_normalizedPath, 50)).ReturnsAsync(new List<GitReflogEntry>());
        var ws = await OpenAsync();

        await ws.Reflog.GetReflogAsync(50);

        _status.Verify(s => s.GetReflogAsync(_normalizedPath, 50), Times.Once);
    }

    [Fact]
    public async Task Tags_GetTagsAsync_DelegatesToStatusService()
    {
        _status.Setup(s => s.GetTagsAsync(_normalizedPath)).ReturnsAsync(new List<GitTag>());
        var ws = await OpenAsync();

        await ws.Tags.GetTagsAsync();

        _status.Verify(s => s.GetTagsAsync(_normalizedPath), Times.Once);
    }

    [Fact]
    public async Task Conflicts_IsMergeInProgressAsync_DelegatesToStatusService()
    {
        // Reset and re-stub so we can verify the call from the sub-interface explicitly.
        _status.Invocations.Clear();
        _status.Setup(s => s.IsMergeInProgressAsync(_normalizedPath)).ReturnsAsync(true);
        var ws = await OpenAsync();
        _status.Invocations.Clear();

        var result = await ws.Conflicts.IsMergeInProgressAsync();

        result.ShouldBeTrue();
        _status.Verify(s => s.IsMergeInProgressAsync(_normalizedPath), Times.Once);
    }

    [Fact]
    public async Task Branches_CompareBranchesAsync_DelegatesToStatusService()
    {
        _status.Setup(s => s.CompareBranchesAsync(_normalizedPath, "a", "b"))
            .ReturnsAsync(new BranchComparisonResult { BaseBranch = "a", CompareBranch = "b" });
        var ws = await OpenAsync();

        var result = await ws.Branches.CompareAsync("a", "b");

        result.BaseBranch.ShouldBe("a");
        result.CompareBranch.ShouldBe("b");
        _status.Verify(s => s.CompareBranchesAsync(_normalizedPath, "a", "b"), Times.Once);
    }

    [Fact]
    public async Task GitHub_GetCurrentPullRequestAsync_NotPresentOnInterface_UsesGetPullRequestDetails()
    {
        // The IGitHubOperations sub-interface does not expose a per-workspace
        // GetCurrentPullRequestAsync method (workspace-level CurrentPullRequest
        // lives on IGitWorkspace itself, populated by RefreshAsync). Verify the
        // most representative GitHub delegate: GetPullRequestDetailsAsync.
        _gitHub.Setup(g => g.GetPullRequestDetailsAsync("owner/repo", 99))
            .ReturnsAsync(new GitHubPullRequest { Number = 99 });
        var ws = await OpenAsync();

        var pr = await ws.GitHub.GetPullRequestDetailsAsync("owner/repo", 99);

        pr.ShouldNotBeNull();
        pr!.Number.ShouldBe(99);
        _gitHub.Verify(g => g.GetPullRequestDetailsAsync("owner/repo", 99), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentCalls_SerializeAndProduceConsistentLast()
    {
        // The implementation uses a SemaphoreSlim refresh gate (NOT deduplication): two
        // concurrent RefreshAsync calls serialize through the gate and both complete. Task
        // scheduling does not guarantee which call enters first, so the test asserts the
        // contract — both complete, status is called twice during refresh, and Last equals
        // whichever snapshot finished last — without depending on r1-vs-r2 ordering.
        _status.Setup(s => s.GetGitStatusAsync(_normalizedPath))
            .ReturnsAsync(new GitStatus { IsGitRepository = true, BranchName = "main" });
        var ws = await OpenAsync();

        var refreshCallCount = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondMayProceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _status.Setup(s => s.GetGitStatusAsync(_normalizedPath))
            .Returns(async () =>
            {
                var n = Interlocked.Increment(ref refreshCallCount);
                if (n == 1)
                {
                    // First call signals it entered the gate, then waits for the test to release.
                    firstEntered.SetResult();
                    await secondMayProceed.Task;
                    return new GitStatus { IsGitRepository = true, BranchName = "alpha" };
                }
                return new GitStatus { IsGitRepository = true, BranchName = "beta" };
            });

        var r1 = ws.RefreshAsync(GitSnapshotScope.LocalOnly);
        // Wait until the first call has entered the gate before kicking off r2 — this is
        // what removes the "did r1 actually start first?" race from the test.
        await firstEntered.Task;
        var r2 = ws.RefreshAsync(GitSnapshotScope.LocalOnly);
        secondMayProceed.SetResult();

        var first = await r1;
        var second = await r2;

        first.CurrentBranch.ShouldBe("alpha");
        second.CurrentBranch.ShouldBe("beta");
        ws.Last.ShouldBeSameAs(second);
        refreshCallCount.ShouldBe(2);
    }
}
