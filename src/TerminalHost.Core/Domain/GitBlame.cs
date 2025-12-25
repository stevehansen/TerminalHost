namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a single line with blame information.
/// </summary>
public class GitBlameLine
{
    public int LineNumber { get; set; }
    public string CommitHash { get; set; } = "";
    public string ShortHash { get; set; } = "";
    public string Author { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public DateTimeOffset CommitDate { get; set; }
    public string RelativeDate { get; set; } = "";
    public string Summary { get; set; } = "";
    public string LineContent { get; set; } = "";

    /// <summary>
    /// True if this is the first line in a group of consecutive lines from the same commit.
    /// Used for visual grouping in the UI.
    /// </summary>
    public bool IsFirstInGroup { get; set; }

    /// <summary>
    /// Number of consecutive lines from this commit (only set on first line in group).
    /// </summary>
    public int GroupSize { get; set; }
}

/// <summary>
/// Complete blame result for a file.
/// </summary>
public class GitBlameResult
{
    public string FilePath { get; set; } = "";
    public List<GitBlameLine> Lines { get; set; } = [];
    public List<string> UniqueAuthors { get; set; } = [];

    /// <summary>
    /// Maps author names to display colors for visual differentiation.
    /// </summary>
    public Dictionary<string, string> AuthorColors { get; set; } = [];
}
