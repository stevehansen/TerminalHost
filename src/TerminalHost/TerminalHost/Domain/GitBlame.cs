namespace TerminalHost.Domain;

/// <summary>
/// Represents a single line of git blame output.
/// </summary>
public class GitBlameLine
{
    /// <summary>
    /// The commit hash that last modified this line.
    /// </summary>
    public string CommitHash { get; set; } = "";

    /// <summary>
    /// Short (7-char) commit hash.
    /// </summary>
    public string ShortHash => CommitHash.Length >= 7 ? CommitHash[..7] : CommitHash;

    /// <summary>
    /// The author who made the change.
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// The author's email.
    /// </summary>
    public string AuthorEmail { get; set; } = "";

    /// <summary>
    /// The date of the commit.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// The line number in the file (1-based).
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// The content of the line.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Whether this is the first line of a group from the same commit.
    /// </summary>
    public bool IsGroupStart { get; set; }

    /// <summary>
    /// Relative date string (e.g., "2 hours ago").
    /// </summary>
    public string RelativeDate
    {
        get
        {
            var span = DateTime.Now - Date;

            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";

            return $"{(int)(span.TotalDays / 365)}y ago";
        }
    }

    /// <summary>
    /// Color based on author email hash for consistent coloring per author.
    /// </summary>
    public string AuthorColor
    {
        get
        {
            var colors = new[]
            {
                "#4EC9B0", // Cyan
                "#569CD6", // Blue
                "#C586C0", // Purple
                "#E2C08D", // Yellow
                "#CE9178", // Orange
                "#6A9955", // Green
                "#D7BA7D", // Gold
                "#9CDCFE"  // Light blue
            };

            var hash = AuthorEmail.GetHashCode();
            return colors[Math.Abs(hash) % colors.Length];
        }
    }
}

/// <summary>
/// Represents the complete blame result for a file.
/// </summary>
public class GitBlameResult
{
    /// <summary>
    /// The file path that was blamed.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// The list of blame lines.
    /// </summary>
    public List<GitBlameLine> Lines { get; set; } = [];
}
