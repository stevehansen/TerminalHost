using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// GitHub operations scoped to the current workspace (PR checkout, approve, merge, comments).
/// </summary>
public interface IGitHubOperations
{
    /// <summary>
    /// Gets detailed information about a specific pull request in this workspace's repo.
    /// </summary>
    Task<GitHubPullRequest?> GetPullRequestDetailsAsync(string repo, int prNumber);

    /// <summary>
    /// Gets the list of files changed in a pull request.
    /// </summary>
    Task<List<GitHubPrFile>> GetPullRequestFilesAsync(string repo, int prNumber);

    /// <summary>
    /// Gets the diff content for a specific file in a pull request.
    /// </summary>
    Task<string?> GetPullRequestFileDiffAsync(string repo, int prNumber, string filePath);

    /// <summary>
    /// Checks out a pull request branch in this workspace.
    /// </summary>
    Task<(bool success, string? error)> CheckoutPullRequestAsync(int prNumber);

    Task<bool> ApprovePullRequestAsync(int prNumber, string? comment = null);

    Task<bool> RequestChangesAsync(int prNumber, string comment);

    Task<bool> CommentOnPullRequestAsync(int prNumber, string comment);

    Task<bool> MergePullRequestAsync(int prNumber, string method = "squash", string? commitSubject = null);

    /// <summary>
    /// Gets all comments and review threads for a pull request.
    /// </summary>
    Task<PrComments?> GetPullRequestCommentsAsync(string repo, int prNumber);
}
