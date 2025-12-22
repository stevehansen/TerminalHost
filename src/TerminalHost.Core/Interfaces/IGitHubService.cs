using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for GitHub operations using the gh CLI.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Checks if the GitHub CLI (gh) is available and authenticated.
    /// </summary>
    bool IsGitHubCliAvailable();

    /// <summary>
    /// Resets the GitHub CLI availability cache, forcing a re-check on next call.
    /// </summary>
    void ResetAvailabilityCache();

    /// <summary>
    /// Gets pull requests where the current user is requested as a reviewer.
    /// </summary>
    Task<List<GitHubPullRequest>> GetReviewRequestsAsync();

    /// <summary>
    /// Gets pull requests authored by the current user.
    /// </summary>
    Task<List<GitHubPullRequest>> GetMyPullRequestsAsync();

    /// <summary>
    /// Gets issues assigned to the current user.
    /// </summary>
    Task<List<GitHubIssue>> GetMyIssuesAsync();

    /// <summary>
    /// Gets recent failed workflow runs for the user's repositories.
    /// </summary>
    Task<List<GitHubWorkflowRun>> GetFailedWorkflowRunsAsync();

    /// <summary>
    /// Gets detailed information about a specific pull request.
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
    /// Checks out a pull request branch in the specified directory.
    /// Returns (success, errorMessage) tuple.
    /// </summary>
    Task<(bool success, string? error)> CheckoutPullRequestAsync(string workingDirectory, int prNumber);

    /// <summary>
    /// Approves a pull request.
    /// </summary>
    Task<bool> ApprovePullRequestAsync(string workingDirectory, int prNumber, string? comment = null);

    /// <summary>
    /// Requests changes on a pull request.
    /// </summary>
    Task<bool> RequestChangesAsync(string workingDirectory, int prNumber, string comment);

    /// <summary>
    /// Adds a comment to a pull request.
    /// </summary>
    Task<bool> CommentOnPullRequestAsync(string workingDirectory, int prNumber, string comment);

    /// <summary>
    /// Merges a pull request.
    /// </summary>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="prNumber">The PR number.</param>
    /// <param name="method">The merge method (squash, merge, rebase).</param>
    /// <param name="commitSubject">Optional commit message subject (title). For squash, defaults to "PR Title (#Number)" if not provided.</param>
    Task<bool> MergePullRequestAsync(string workingDirectory, int prNumber, string method = "squash", string? commitSubject = null);

    /// <summary>
    /// Gets repositories accessible to the current user.
    /// </summary>
    Task<List<GitHubRepository>> GetUserRepositoriesAsync(int limit = 100);

    /// <summary>
    /// Clones a repository to the specified directory.
    /// </summary>
    Task<string?> CloneRepositoryAsync(string repoFullName, string targetDirectory);

    /// <summary>
    /// Gets the current user's GitHub username.
    /// </summary>
    Task<string?> GetCurrentUserAsync();

    /// <summary>
    /// Detects if the current directory is in a PR branch and returns PR details.
    /// </summary>
    Task<GitHubPullRequest?> GetCurrentPullRequestAsync(string workingDirectory);

    /// <summary>
    /// Gets the diff content for a specific file in the working directory.
    /// Uses git diff to get the changes.
    /// </summary>
    Task<string> GetFileDiffAsync(string workingDirectory, string filename);

    /// <summary>
    /// Gets all comments and review threads for a pull request.
    /// </summary>
    Task<PrComments?> GetPullRequestCommentsAsync(string repo, int prNumber);
}
