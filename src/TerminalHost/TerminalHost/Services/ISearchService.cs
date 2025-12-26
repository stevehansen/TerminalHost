using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for searching text across project files.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches for a pattern across files in the specified directory.
    /// </summary>
    /// <param name="directory">The directory to search in.</param>
    /// <param name="searchPattern">The text or regex pattern to search for.</param>
    /// <param name="caseSensitive">Whether the search is case-sensitive.</param>
    /// <param name="wholeWord">Whether to match whole words only.</param>
    /// <param name="useRegex">Whether to treat the pattern as a regular expression.</param>
    /// <param name="includePattern">Glob pattern for files to include (e.g., "*.cs").</param>
    /// <param name="excludePattern">Glob pattern for files to exclude (e.g., "*.min.js").</param>
    /// <param name="respectGitignore">Whether to respect .gitignore rules.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of search results grouped by file.</returns>
    Task<List<SearchResult>> SearchAsync(
        string directory,
        string searchPattern,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false,
        string? includePattern = null,
        string? excludePattern = null,
        bool respectGitignore = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces all occurrences in a single file.
    /// </summary>
    /// <param name="filePath">The file to modify.</param>
    /// <param name="search">The text to search for.</param>
    /// <param name="replace">The replacement text.</param>
    /// <param name="caseSensitive">Whether the search is case-sensitive.</param>
    /// <param name="wholeWord">Whether to match whole words only.</param>
    /// <param name="useRegex">Whether to treat the pattern as a regular expression.</param>
    /// <returns>The number of replacements made.</returns>
    Task<int> ReplaceInFileAsync(
        string filePath,
        string search,
        string replace,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false);

    /// <summary>
    /// Replaces all occurrences across multiple files.
    /// </summary>
    /// <param name="directory">The directory to search in.</param>
    /// <param name="search">The text to search for.</param>
    /// <param name="replace">The replacement text.</param>
    /// <param name="caseSensitive">Whether the search is case-sensitive.</param>
    /// <param name="wholeWord">Whether to match whole words only.</param>
    /// <param name="useRegex">Whether to treat the pattern as a regular expression.</param>
    /// <param name="includePattern">Glob pattern for files to include.</param>
    /// <param name="excludePattern">Glob pattern for files to exclude.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The total number of replacements made across all files.</returns>
    Task<int> ReplaceAllAsync(
        string directory,
        string search,
        string replace,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false,
        string? includePattern = null,
        string? excludePattern = null,
        CancellationToken cancellationToken = default);
}
