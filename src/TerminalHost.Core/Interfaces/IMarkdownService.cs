namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for converting Markdown to HTML.
/// Supports Mermaid diagrams, YAML frontmatter tables, and relative link resolution.
/// </summary>
public interface IMarkdownService
{
    /// <summary>
    /// Converts Markdown content to HTML with embedded dark-theme styling.
    /// </summary>
    /// <param name="markdown">The markdown content to convert.</param>
    /// <param name="basePath">Optional base path for resolving relative links and images.</param>
    string ConvertToHtml(string markdown, string? basePath = null);

    /// <summary>
    /// Reads a Markdown file and converts it to HTML.
    /// Relative links are resolved relative to the file's directory.
    /// </summary>
    Task<string> ConvertFileToHtmlAsync(string filePath);
}
