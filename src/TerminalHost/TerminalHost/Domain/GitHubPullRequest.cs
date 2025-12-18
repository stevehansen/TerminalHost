namespace TerminalHost.Domain;

/// <summary>
/// Represents a GitHub label.
/// </summary>
public class GitHubLabel
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";

    /// <summary>
    /// Gets whether this is a size label.
    /// </summary>
    public bool IsSizeLabel => Name.StartsWith("size/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the size value (XS, S, M, L, XL, XXL) if this is a size label.
    /// </summary>
    public string? SizeValue => IsSizeLabel ? Name.Replace("size/", "", StringComparison.OrdinalIgnoreCase) : null;

    /// <summary>
    /// Gets a color with # prefix for XAML binding.
    /// </summary>
    public string ColorHex => string.IsNullOrEmpty(Color) ? "#808080" : $"#{Color}";
}

/// <summary>
/// Represents a GitHub pull request.
/// </summary>
public class GitHubPullRequest
{
    /// <summary>
    /// The PR number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// The PR title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The PR body/description (markdown).
    /// Only populated when viewing a single PR in review mode.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// The repository in "owner/repo" format.
    /// </summary>
    public string Repository { get; set; } = "";

    /// <summary>
    /// The author's GitHub username.
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// The PR state: "open", "merged", or "closed".
    /// </summary>
    public string State { get; set; } = "open";

    /// <summary>
    /// The base branch name (e.g., "main").
    /// </summary>
    public string? BaseBranch { get; set; }

    /// <summary>
    /// The head branch name (the PR branch).
    /// </summary>
    public string? HeadBranch { get; set; }

    /// <summary>
    /// Number of lines added.
    /// </summary>
    public int Additions { get; set; }

    /// <summary>
    /// Number of lines deleted.
    /// </summary>
    public int Deletions { get; set; }

    /// <summary>
    /// Number of files changed.
    /// </summary>
    public int ChangedFiles { get; set; }

    /// <summary>
    /// CI status: "passing", "failing", "pending", or null if unknown.
    /// </summary>
    public string? CiStatus { get; set; }

    /// <summary>
    /// Review decision: "approved", "changes_requested", "review_required", or null.
    /// </summary>
    public string? ReviewDecision { get; set; }

    /// <summary>
    /// Number of reviews completed.
    /// </summary>
    public int ReviewsCompleted { get; set; }

    /// <summary>
    /// Number of reviews required.
    /// </summary>
    public int ReviewsRequired { get; set; }

    /// <summary>
    /// When the PR was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the PR was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Labels attached to the PR.
    /// </summary>
    public List<GitHubLabel> Labels { get; set; } = [];

    /// <summary>
    /// Gets the repository name without owner (e.g., "repo" from "owner/repo").
    /// </summary>
    public string RepositoryName => Repository.Contains('/') ? Repository.Split('/').Last() : Repository;

    /// <summary>
    /// Gets the size label if present (e.g., "XS", "S", "M", "L", "XL", "XXL").
    /// </summary>
    public GitHubLabel? SizeLabel => Labels.FirstOrDefault(l => l.IsSizeLabel);

    /// <summary>
    /// Gets the size display text (e.g., "XS", "M", "XXL").
    /// </summary>
    public string? SizeDisplay => SizeLabel?.SizeValue;

    /// <summary>
    /// Gets the size label color for display.
    /// </summary>
    public string? SizeColor => SizeLabel?.ColorHex;

    /// <summary>
    /// Gets a human-readable time since the PR was updated.
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
    /// Gets a formatted diff summary (e.g., "+142 -23").
    /// </summary>
    public string DiffSummary => $"+{Additions} -{Deletions}";

    /// <summary>
    /// Gets a CI status icon.
    /// </summary>
    public string CiStatusIcon => CiStatus switch
    {
        "passing" => "P",
        "failing" => "X",
        "pending" => "O",
        _ => "?"
    };
}
