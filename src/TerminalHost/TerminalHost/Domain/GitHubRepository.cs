namespace TerminalHost.Domain;

/// <summary>
/// Represents a GitHub repository.
/// </summary>
public class GitHubRepository
{
    /// <summary>
    /// The full name in "owner/repo" format.
    /// </summary>
    public string FullName { get; set; } = "";

    /// <summary>
    /// The repository description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Whether the repository is private.
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// When the repository was last pushed to.
    /// </summary>
    public DateTime? LastPushedAt { get; set; }

    /// <summary>
    /// The repository URL.
    /// </summary>
    public string HtmlUrl => $"https://github.com/{FullName}";

    /// <summary>
    /// Gets just the repository name without owner.
    /// </summary>
    public string Name => FullName.Contains('/') ? FullName.Split('/').Last() : FullName;

    /// <summary>
    /// Gets the owner (organization or user).
    /// </summary>
    public string Owner => FullName.Contains('/') ? FullName.Split('/').First() : "";

    /// <summary>
    /// Gets a human-readable time since last push.
    /// </summary>
    public string TimeSinceLastPush
    {
        get
        {
            if (LastPushedAt == null)
                return "never";

            var diff = DateTime.UtcNow - LastPushedAt.Value;
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            return LastPushedAt.Value.ToString("MMM dd");
        }
    }
}
