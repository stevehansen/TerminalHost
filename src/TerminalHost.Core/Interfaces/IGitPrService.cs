using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for detecting and fetching GitHub PR information.
/// </summary>
public interface IGitPrService
{
    /// <summary>
    /// Parse a task title for PR or issue number patterns.
    /// </summary>
    /// <returns>Tuple of (prNumber, issueNumber) - both may be null if no pattern found.</returns>
    (string? prNumber, string? issueNumber) ParseTaskTitle(string title);

    /// <summary>
    /// Search for a git branch matching an issue/PR number in the given project.
    /// </summary>
    Task<string?> FindBranchForIssueAsync(string projectPath, string issueNumber);

    /// <summary>
    /// Fetch PR details from GitHub using the gh CLI.
    /// </summary>
    /// <returns>PR details or null if not found or gh is not available.</returns>
    Task<GitPrDetails?> FetchPrDetailsAsync(string projectPath, string prNumber);

    /// <summary>
    /// Check if the GitHub CLI (gh) is available.
    /// </summary>
    bool IsGitHubCliAvailable();
}
