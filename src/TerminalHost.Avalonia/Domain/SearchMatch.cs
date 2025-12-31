namespace TerminalHost.Domain;

/// <summary>
/// Represents a single match within a file.
/// </summary>
public class SearchMatch
{
    /// <summary>
    /// The line number where the match was found (1-based).
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// The full content of the line containing the match.
    /// </summary>
    public string LineContent { get; set; } = string.Empty;

    /// <summary>
    /// The character position where the match starts (0-based).
    /// </summary>
    public int MatchStart { get; set; }

    /// <summary>
    /// The length of the matched text.
    /// </summary>
    public int MatchLength { get; set; }

    /// <summary>
    /// Context lines before the match (optional).
    /// </summary>
    public string? ContextBefore { get; set; }

    /// <summary>
    /// Context lines after the match (optional).
    /// </summary>
    public string? ContextAfter { get; set; }

    /// <summary>
    /// Gets the matched text.
    /// </summary>
    public string MatchedText =>
        MatchStart >= 0 && MatchLength > 0 && MatchStart + MatchLength <= LineContent.Length
            ? LineContent.Substring(MatchStart, MatchLength)
            : string.Empty;

    /// <summary>
    /// Gets the text before the match on the same line.
    /// </summary>
    public string TextBefore =>
        MatchStart > 0 ? LineContent[..MatchStart] : string.Empty;

    /// <summary>
    /// Gets the text after the match on the same line.
    /// </summary>
    public string TextAfter =>
        MatchStart + MatchLength < LineContent.Length
            ? LineContent[(MatchStart + MatchLength)..]
            : string.Empty;
}
