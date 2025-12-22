namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a pending review comment on a pull request.
/// </summary>
public class PrReviewComment
{
    /// <summary>
    /// The file path the comment is on.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// The line number in the file (in the diff context).
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// The comment body text.
    /// </summary>
    public string Body { get; set; } = "";

    /// <summary>
    /// Whether this is a suggestion (code change proposal).
    /// </summary>
    public bool IsSuggestion { get; set; }

    /// <summary>
    /// The suggested code if IsSuggestion is true.
    /// </summary>
    public string? SuggestedCode { get; set; }
}
