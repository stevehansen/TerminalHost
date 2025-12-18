namespace TerminalHost.Domain;

/// <summary>
/// Represents a GitHub Actions workflow run.
/// </summary>
public class GitHubWorkflowRun
{
    /// <summary>
    /// The run ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The workflow name.
    /// </summary>
    public string WorkflowName { get; set; } = "";

    /// <summary>
    /// The repository in "owner/repo" format.
    /// </summary>
    public string Repository { get; set; } = "";

    /// <summary>
    /// The branch that triggered the run.
    /// </summary>
    public string Branch { get; set; } = "";

    /// <summary>
    /// The run status: "completed", "in_progress", "queued".
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// The run conclusion: "success", "failure", "cancelled", "skipped".
    /// </summary>
    public string? Conclusion { get; set; }

    /// <summary>
    /// The commit SHA that triggered the run.
    /// </summary>
    public string CommitSha { get; set; } = "";

    /// <summary>
    /// The commit message.
    /// </summary>
    public string CommitMessage { get; set; } = "";

    /// <summary>
    /// When the run was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the run was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The URL to view the run on GitHub.
    /// </summary>
    public string HtmlUrl { get; set; } = "";

    /// <summary>
    /// Gets the repository name without owner.
    /// </summary>
    public string RepositoryName => Repository.Contains('/') ? Repository.Split('/').Last() : Repository;

    /// <summary>
    /// Gets a human-readable time since the run was updated.
    /// </summary>
    public string TimeSinceUpdate
    {
        get
        {
            var diff = DateTime.Now - UpdatedAt;
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            return UpdatedAt.ToString("MMM dd");
        }
    }

    /// <summary>
    /// Gets an icon representing the run conclusion.
    /// </summary>
    public string ConclusionIcon => Conclusion switch
    {
        "success" => "P",
        "failure" => "X",
        "cancelled" => "-",
        "skipped" => "O",
        _ => "?"
    };
}
