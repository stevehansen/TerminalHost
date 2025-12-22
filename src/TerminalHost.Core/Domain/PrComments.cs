namespace TerminalHost.Core.Domain;

/// <summary>
/// A single comment in a PR review thread.
/// </summary>
public class PrCommentItem
{
    /// <summary>
    /// The author's login name.
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// The comment body (markdown).
    /// </summary>
    public string Body { get; set; } = "";

    /// <summary>
    /// When the comment was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Whether this is a bot comment.
    /// </summary>
    public bool IsBot { get; set; }
}

/// <summary>
/// A review thread on a specific file/line in a PR.
/// </summary>
public class PrReviewThread
{
    /// <summary>
    /// Unique ID for the thread.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The file path the thread is on.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// The line number in the diff (can be null for file-level comments).
    /// </summary>
    public int? Line { get; set; }

    /// <summary>
    /// Which side of the diff (LEFT = old, RIGHT = new).
    /// </summary>
    public string? DiffSide { get; set; }

    /// <summary>
    /// Whether this thread is resolved.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Whether this thread is outdated (code has changed since comment).
    /// </summary>
    public bool IsOutdated { get; set; }

    /// <summary>
    /// The comments in this thread, in chronological order.
    /// </summary>
    public List<PrCommentItem> Comments { get; set; } = [];

    /// <summary>
    /// First comment (original comment that started the thread).
    /// </summary>
    public PrCommentItem? FirstComment => Comments.Count > 0 ? Comments[0] : null;

    /// <summary>
    /// Number of replies (excluding the first comment).
    /// </summary>
    public int ReplyCount => Math.Max(0, Comments.Count - 1);
}

/// <summary>
/// All comments on a PR, organized by type.
/// </summary>
public class PrComments
{
    /// <summary>
    /// General comments on the PR (not on specific files/lines).
    /// </summary>
    public List<PrCommentItem> GeneralComments { get; set; } = [];

    /// <summary>
    /// Review threads on specific files/lines.
    /// </summary>
    public List<PrReviewThread> ReviewThreads { get; set; } = [];

    /// <summary>
    /// Get all threads for a specific file.
    /// </summary>
    public List<PrReviewThread> GetThreadsForFile(string filePath)
    {
        return ReviewThreads
            .Where(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Line ?? 0)
            .ToList();
    }

    /// <summary>
    /// Get threads for a specific file and line.
    /// </summary>
    public List<PrReviewThread> GetThreadsForLine(string filePath, int line)
    {
        return ReviewThreads
            .Where(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase) && t.Line == line)
            .ToList();
    }

    /// <summary>
    /// Total number of threads.
    /// </summary>
    public int TotalThreadCount => ReviewThreads.Count;

    /// <summary>
    /// Number of unresolved threads.
    /// </summary>
    public int UnresolvedThreadCount => ReviewThreads.Count(t => !t.IsResolved);
}
