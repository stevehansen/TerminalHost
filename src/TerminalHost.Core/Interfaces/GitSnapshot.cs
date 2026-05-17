using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Immutable cross-service snapshot of a git workspace at a point in time.
/// </summary>
public sealed record GitSnapshot(
    GitStatus Status,
    string CurrentBranch,
    int Ahead,
    int Behind,
    GitHubPullRequest? PullRequest,
    IReadOnlyList<WorktreeInfo> Worktrees,
    bool IsMergeInProgress,
    bool IsRebaseInProgress,
    DateTimeOffset CapturedAt);
