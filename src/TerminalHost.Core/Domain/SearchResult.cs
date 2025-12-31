namespace TerminalHost.Core.Domain;

/// <summary>
/// Options for configuring file search behavior.
/// </summary>
public class SearchOptions
{
    /// <summary>
    /// Whether the search is case-sensitive.
    /// </summary>
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// Whether to match whole words only.
    /// </summary>
    public bool WholeWord { get; set; }

    /// <summary>
    /// Whether to interpret the pattern as a regular expression.
    /// </summary>
    public bool UseRegex { get; set; }

    /// <summary>
    /// Glob pattern for files to include (e.g., "*.cs", "src/**").
    /// </summary>
    public string? IncludePattern { get; set; }

    /// <summary>
    /// Glob pattern for files/directories to exclude (e.g., "bin,obj,node_modules").
    /// </summary>
    public string? ExcludePattern { get; set; }

    /// <summary>
    /// Whether to respect .gitignore patterns.
    /// </summary>
    public bool UseGitignore { get; set; } = true;

    /// <summary>
    /// Number of context lines to show before and after matches.
    /// </summary>
    public int ContextLines { get; set; } = 1;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int MaxResults { get; set; } = 10000;
}

/// <summary>
/// Represents the complete results of a file search operation.
/// </summary>
public class SearchResults
{
    /// <summary>
    /// Files containing matches, grouped with their matches.
    /// </summary>
    public List<SearchFileResult> Files { get; set; } = [];

    /// <summary>
    /// Total number of matches across all files.
    /// </summary>
    public int TotalMatchCount { get; set; }

    /// <summary>
    /// Number of files with matches.
    /// </summary>
    public int FileCount => Files.Count;

    /// <summary>
    /// Whether the results were truncated due to MaxResults limit.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Time taken to perform the search in milliseconds.
    /// </summary>
    public long SearchTimeMs { get; set; }
}

/// <summary>
/// Represents all matches within a single file.
/// </summary>
public class SearchFileResult
{
    /// <summary>
    /// Relative path to the file from the search root.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Full path to the file.
    /// </summary>
    public string FullPath { get; set; } = "";

    /// <summary>
    /// Individual matches within this file.
    /// </summary>
    public List<SearchMatch> Matches { get; set; } = [];

    /// <summary>
    /// Number of matches in this file.
    /// </summary>
    public int MatchCount => Matches.Count;

    /// <summary>
    /// Whether this file's matches are expanded in the UI.
    /// </summary>
    public bool IsExpanded { get; set; } = true;
}

/// <summary>
/// Represents a single match within a file.
/// </summary>
public class SearchMatch
{
    /// <summary>
    /// 1-based line number where the match occurs.
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// 0-based column/character position within the line.
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Length of the matched text.
    /// </summary>
    public int MatchLength { get; set; }

    /// <summary>
    /// The complete line containing the match.
    /// </summary>
    public string LineText { get; set; } = "";

    /// <summary>
    /// Context lines before the match (if ContextLines > 0).
    /// </summary>
    public List<ContextLine> ContextBefore { get; set; } = [];

    /// <summary>
    /// Context lines after the match (if ContextLines > 0).
    /// </summary>
    public List<ContextLine> ContextAfter { get; set; } = [];

    /// <summary>
    /// The matched text portion.
    /// </summary>
    public string MatchedText { get; set; } = "";

    /// <summary>
    /// Text before the match on the same line.
    /// </summary>
    public string TextBefore => Column > 0 && Column <= LineText.Length
        ? LineText[..Column]
        : "";

    // Aliases for compatibility (settable properties)
    public string LineContent { get => LineText; set => LineText = value; }
    public int MatchStart { get => Column; set => Column = value; }

    /// <summary>
    /// Text after the match on the same line.
    /// </summary>
    public string TextAfter
    {
        get
        {
            var endIndex = Column + MatchLength;
            return endIndex < LineText.Length ? LineText[endIndex..] : "";
        }
    }
}

/// <summary>
/// Represents a context line (before or after a match).
/// </summary>
public class ContextLine
{
    /// <summary>
    /// 1-based line number.
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// The line text.
    /// </summary>
    public string Text { get; set; } = "";
}
