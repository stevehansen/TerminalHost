using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for searching across files in a directory.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches for a pattern across files in the specified directory.
    /// </summary>
    /// <param name="pattern">The search pattern (text or regex).</param>
    /// <param name="workingDirectory">The root directory to search in.</param>
    /// <param name="options">Search configuration options.</param>
    /// <param name="progressCallback">Optional callback for progress updates.</param>
    /// <param name="cancellationToken">Token to cancel the search.</param>
    /// <returns>Search results grouped by file.</returns>
    Task<SearchResults> SearchAsync(
        string pattern,
        string workingDirectory,
        SearchOptions options,
        Action<int>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces matches in files with a replacement string.
    /// </summary>
    /// <param name="pattern">The search pattern (text or regex).</param>
    /// <param name="replacement">The replacement string.</param>
    /// <param name="workingDirectory">The root directory to search in.</param>
    /// <param name="options">Search configuration options.</param>
    /// <param name="filesToReplace">Specific files to replace in (null = all matches).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Number of replacements made.</returns>
    Task<int> ReplaceAsync(
        string pattern,
        string replacement,
        string workingDirectory,
        SearchOptions options,
        IEnumerable<string>? filesToReplace = null,
        CancellationToken cancellationToken = default);
}
