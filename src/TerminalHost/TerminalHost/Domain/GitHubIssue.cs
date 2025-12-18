namespace TerminalHost.Domain;

/// <summary>
/// Represents a GitHub issue.
/// </summary>
public class GitHubIssue
{
    /// <summary>
    /// The issue number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// The issue title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The repository in "owner/repo" format.
    /// </summary>
    public string Repository { get; set; } = "";

    /// <summary>
    /// The author's GitHub username.
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// The issue state: "open" or "closed".
    /// </summary>
    public string State { get; set; } = "open";

    /// <summary>
    /// Labels assigned to the issue (rich format with colors).
    /// </summary>
    public List<GitHubLabel> Labels { get; set; } = [];

    /// <summary>
    /// When the issue was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the issue was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets the repository name without owner.
    /// </summary>
    public string RepositoryName => Repository.Contains('/') ? Repository.Split('/').Last() : Repository;

    /// <summary>
    /// Gets a human-readable time since the issue was updated.
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
    /// Gets a comma-separated list of labels.
    /// </summary>
    public string LabelsSummary => Labels.Count > 0 ? string.Join(", ", Labels.Take(3).Select(l => l.Name)) : "";

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
}
