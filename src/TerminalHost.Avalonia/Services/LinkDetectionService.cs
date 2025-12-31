using TerminalHost.Core.Interfaces;
using System.Diagnostics;
using TerminalHost.Core.Interfaces;
using System.IO;
using TerminalHost.Core.Interfaces;
using System.Text.RegularExpressions;

namespace TerminalHost.Services;

/// <summary>
/// Service for detecting and handling clickable links in terminal output.
/// Supports URLs, file paths, and custom regex patterns.
/// </summary>
internal sealed class LinkDetectionService(IProfileRegistry profileRegistry, IFileSystem fileSystem, IProcessService processService, IDialogService dialogService) : ILinkDetectionService
{
    private readonly IProfileRegistry _profileRegistry = profileRegistry;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessService _processService = processService;
    private readonly IDialogService _dialogService = dialogService;

    // Built-in patterns for common link types
    // Constructed using char arrays to completely bypass string literal escape issues
    private static readonly Regex UrlPattern = new(
        string.Join("", [ 
            "https?://[^", 
            new(['\\', 's']), 
            "<>\"'`", 
            new(['\\', '[']), 
            new(['\\', ']']), 
            new(['\\', ')']), 
            "]+" 
        ]),
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FilePathPattern = new(
        string.Join("", [
            "(?:[A-Za-z]:", new(['\\', '\\']), "|", new(['\\', '\\', '\\', '\\']), "|/)?",
            "(?:[", new(['\\', 'w']), ".-]+[", new(['\\', '\\']), "/])*",
            "([", new(['\\', 'w']), ".-]+", new(['\\', '.']), new(['\\', 'w']), "+)",
            "(?::", new(['\\', 'd']), "+)?",
            "(?::", new(['\\', 'd']), "+)?"
        ]),
        RegexOptions.Compiled);

    // Constructed using char array to avoid string literal corruption
    private static readonly string punctuation = new(['.', ',', ':', ';', '!', '?', ')', ']', '>']);
    // Word boundary characters (not including common URL/path characters)
    private static readonly HashSet<char> separators = [' ', '\t', '\n', '\r', '"', '\'', '<', '>', '`', '|', ';'];

    /// <summary>
    /// Attempts to detect and resolve a link from the given text.
    /// Returns the URL to open, or null if no link was detected.
    /// </summary>
    /// <param name="text">The text to analyze (typically selected text or word under cursor).</param>
    /// <param name="workingDirectory">Current working directory for resolving relative paths.</param>
    /// <returns>URL to open, or null if no link detected.</returns>
    public string? DetectLink(string text, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        // 1. Check custom patterns first (highest priority)
        var customLink = TryMatchCustomPatterns(text);
        if (customLink != null)
            return customLink;

        // 2. Check for URLs
        var urlMatch = UrlPattern.Match(text);
        if (urlMatch.Success)
            return CleanUrl(urlMatch.Value);

        // 3. Check if the entire text looks like a URL
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return CleanUrl(text);
        }

        // 4. Check for file paths
        var filePath = TryResolveFilePath(text, workingDirectory);
        if (filePath != null)
            return filePath;

        return null;
    }

    /// <summary>
    /// Attempts to match text against custom link patterns from configuration.
    /// </summary>
    private string? TryMatchCustomPatterns(string text)
    {
        var patterns = _profileRegistry.LinkPatterns
            .Where(p => p.Enabled)
            .OrderByDescending(p => p.Priority);

        foreach (var pattern in patterns)
        {
            try
            {
                var regex = new Regex(pattern.Pattern, RegexOptions.IgnoreCase);
                var match = regex.Match(text);

                if (match.Success)
                {
                    return BuildUrlFromTemplate(pattern.UrlTemplate, match);
                }
            }
            catch (RegexParseException)
            {
                // Invalid regex pattern, skip it
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a URL from a template by substituting captured groups.
    /// </summary>
    private static string BuildUrlFromTemplate(string template, Match match)
    {
        var result = template;

        // Replace $0 with full match
        result = result.Replace("$0", match.Value);

        // Replace $1, $2, etc. with captured groups
        for (int i = 1; i < match.Groups.Count; i++)
        {
            result = result.Replace($"${{i}}", match.Groups[i].Value);
        }

        return result;
    }

    /// <summary>
    /// Attempts to resolve a file path, supporting both absolute and relative paths.
    /// </summary>
    private string? TryResolveFilePath(string text, string? workingDirectory)
    {
        // Check if it looks like a file path
        if (!FilePathPattern.IsMatch(text))
            return null;

        // Extract just the path part (without line/column numbers)
        var pathMatch = Regex.Match(text, @"^(.+?)(?::(\d+))?(?::(\d+))?$");
        if (!pathMatch.Success)
            return null;

        var path = pathMatch.Groups[1].Value;
        _ = pathMatch.Groups[2].Success ? pathMatch.Groups[2].Value : null;

        // Try to resolve the path
        string? resolvedPath = null;

        // Check if it's an absolute path
        if (Path.IsPathRooted(path))
        {
            if (_fileSystem.FileExists(path) || _fileSystem.DirectoryExists(path))
                resolvedPath = path;
        }
        // Try relative to working directory
        else if (!string.IsNullOrEmpty(workingDirectory))
        {
            var fullPath = Path.GetFullPath(Path.Combine(workingDirectory, path));
            if (_fileSystem.FileExists(fullPath) || _fileSystem.DirectoryExists(fullPath))
                resolvedPath = fullPath;
        }

        if (resolvedPath == null)
            return null;

        // Return as file:// URL for directories, or open with default app
        if (_fileSystem.DirectoryExists(resolvedPath))
        {
            return $"file:///{resolvedPath.Replace('\\', '/')}";
        }

        // For files, we'll just return the path - the caller will open it
        return resolvedPath;
    }

    /// <summary>
    /// Cleans up a URL by removing trailing punctuation that might have been included.
    /// </summary>
    private static string CleanUrl(string url)
    {
        // Remove trailing punctuation that's likely not part of the URL
        
        while (url.Length > 0 && punctuation.Contains(url[^1]))
        {
            // But keep if there's a matching opening bracket
            if (url[^1] == ')' && url.Contains('('))
                break;
            if (url[^1] == ']' && url.Contains('['))
                break;
            if (url[^1] == '>' && url.Contains('<'))
                break;

            url = url[..^1];
        }

        return url;
    }

    
    /// <summary>
    /// Determines if a link is a local file path (as opposed to a URL).
    /// </summary>
    public bool IsFilePath(string link)
    {
        if (string.IsNullOrEmpty(link))
            return false;

        // It's a URL if it starts with a protocol
        if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            link.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check if it's an existing file
        return _fileSystem.FileExists(link);
    }

/// <summary>
    /// Opens a link using the system default handler.
    /// </summary>
    /// <param name="link">The URL or file path to open.</param>
    public void OpenLink(string link)
    {
        try
        {
            // Check if it's a file path (not a URL)
            if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !link.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // It's a file path - check if it exists
                if (_fileSystem.FileExists(link))
                {
                    // Open file with default application
                    _processService.Start(link);
                    return;
                }
                else if (_fileSystem.DirectoryExists(link))
                {
                    // Open folder in explorer
                    _processService.Start("explorer.exe", link);
                    return;
                }
            }

            // It's a URL - open in default browser
            _processService.Start(link);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to open link:\n{ex.Message}", "Link Error"); // Use injected IDialogService
        }
    }

    /// <summary>
    /// Extracts a "word" from text at the given position.
    /// A word is delimited by whitespace or common separators.
    /// </summary>
    public static string ExtractWordAt(string text, int position)
    {
        if (string.IsNullOrEmpty(text) || position < 0 || position >= text.Length)
            return string.Empty;

        // Find start of word
        var start = position;
        while (start > 0 && !separators.Contains(text[start - 1]))
            start--;

        // Find end of word
        var end = position;
        while (end < text.Length && !separators.Contains(text[end]))
            end++;

        return text[start..end];
    }

    /// <summary>
    /// Scans text for all detectable links (URLs, file paths, custom patterns).
    /// Returns a list of detected links with their display text and URL.
    /// Uses FIFO ordering - returns the most recent (last appearing) links when over maxLinks.
    /// </summary>
    /// <param name="text">The text to scan for links.</param>
    /// <param name="workingDirectory">Current working directory for resolving relative paths.</param>
    /// <param name="maxLinks">Maximum number of links to return.</param>
    /// <returns>List of detected links (DisplayText, Url, LinkType).</returns>
    public List<DetectedLink> DetectAllLinks(string text, string? workingDirectory = null, int maxLinks = 20)
    {
        var allLinks = new List<(int Position, DetectedLink Link)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(text))
            return [];

        // 1. Find all URLs with their positions
        foreach (Match match in UrlPattern.Matches(text))
        {
            var url = CleanUrl(match.Value);
            if (seen.Add(url))
            {
                allLinks.Add((match.Index, new DetectedLink
                {
                    DisplayText = TruncateForDisplay(url, 60),
                    Url = url,
                    LinkType = LinkType.Url
                }));
            }
        }

        // 2. Find file paths with their positions
        foreach (Match match in FilePathPattern.Matches(text))
        {
            var potentialPath = match.Value;
            var resolved = TryResolveFilePath(potentialPath, workingDirectory);
            if (resolved != null && !seen.Contains(resolved))
            {
                seen.Add(resolved);
                var displayPath = Path.GetFileName(resolved);
                if (string.IsNullOrEmpty(displayPath))
                    displayPath = resolved;

                allLinks.Add((match.Index, new DetectedLink
                {
                    DisplayText = TruncateForDisplay(displayPath, 40),
                    Url = resolved,
                    LinkType = _fileSystem.FileExists(resolved) ? LinkType.File : LinkType.Directory
                }));
            }
        }

        // 3. Find custom pattern matches with their positions
        var customPatterns = _profileRegistry.LinkPatterns
            .Where(p => p.Enabled)
            .OrderByDescending(p => p.Priority);

        foreach (var pattern in customPatterns)
        {
            try
            {
                var regex = new Regex(pattern.Pattern, RegexOptions.IgnoreCase);
                foreach (Match match in regex.Matches(text))
                {
                    var url = BuildUrlFromTemplate(pattern.UrlTemplate, match);
                    if (seen.Add(url))
                    {
                        allLinks.Add((match.Index, new DetectedLink
                        {
                            DisplayText = $"{pattern.Name}: {match.Value}",
                            Url = url,
                            LinkType = LinkType.Custom
                        }));
                    }
                }
            }
            catch (RegexParseException)
            {
                // Invalid regex pattern, skip it
            }
        }

        // Sort by position and take the last maxLinks (FIFO - most recent links)
        return [.. allLinks
            .OrderBy(x => x.Position)
            .TakeLast(maxLinks)
            .Select(x => x.Link)];
    }

    /// <summary>
    /// Truncates a string for display, adding ellipsis if needed.
    /// </summary>
    private static string TruncateForDisplay(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }
}
// DetectedLink and LinkType are now in TerminalHost.Core.Domain
