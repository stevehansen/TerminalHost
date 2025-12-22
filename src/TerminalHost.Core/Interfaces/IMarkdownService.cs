namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for converting Markdown to HTML.
/// </summary>
public interface IMarkdownService
{
    /// <summary>
    /// Converts Markdown content to HTML with embedded dark-theme styling.
    /// </summary>
    string ConvertToHtml(string markdown);

    /// <summary>
    /// Reads a Markdown file and converts it to HTML.
    /// </summary>
    Task<string> ConvertFileToHtmlAsync(string filePath);
}
