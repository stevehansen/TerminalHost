using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for detecting and handling clickable links in terminal output.
/// Supports URLs, file paths, and custom regex patterns.
/// </summary>
public class LinkDetectionService
{
    private readonly ProfileRegistry _profileRegistry;

    // Built-in patterns for common link types
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s<>""'`\]\)]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FilePathPattern = new(
        @"(?:[A-Za-z]:\\|\\\\|/)?(?:[\w.-]+[/\\])*[\w.-]+\.\w+(?::\d+)?(?::\d+)?",
        RegexOptions.Compiled);

    public LinkDetectionService(ProfileRegistry profileRegistry)
    {
        _profileRegistry = profileRegistry;
    }

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
                Console.WriteLine($"[LinkDetectionService] Invalid regex pattern: {pattern.Pattern}");
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
            result = result.Replace($"${i}", match.Groups[i].Value);
        }

        return result;
    }

    /// <summary>
    /// Attempts to resolve a file path, supporting both absolute and relative paths.
    /// </summary>
    private static string? TryResolveFilePath(string text, string? workingDirectory)
    {
        // Check if it looks like a file path
        if (!FilePathPattern.IsMatch(text))
            return null;

        // Extract just the path part (without line/column numbers)
        var pathMatch = Regex.Match(text, @"^(.+?)(?::(\d+))?(?::(\d+))?$");
        if (!pathMatch.Success)
            return null;

        var path = pathMatch.Groups[1].Value;
        var lineNumber = pathMatch.Groups[2].Success ? pathMatch.Groups[2].Value : null;

        // Try to resolve the path
        string? resolvedPath = null;

        // Check if it's an absolute path
        if (Path.IsPathRooted(path))
        {
            if (File.Exists(path) || Directory.Exists(path))
                resolvedPath = path;
        }
        // Try relative to working directory
        else if (!string.IsNullOrEmpty(workingDirectory))
        {
            var fullPath = Path.GetFullPath(Path.Combine(workingDirectory, path));
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                resolvedPath = fullPath;
        }

        if (resolvedPath == null)
            return null;

        // Return as file:// URL for directories, or open with default app
        if (Directory.Exists(resolvedPath))
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
        while (url.Length > 0 && ".,:;!?)>]".Contains(url[^1]))
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
    public static bool IsFilePath(string link)
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
        return File.Exists(link);
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
                if (File.Exists(link))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = link,
                        UseShellExecute = true
                    });
                    return;
                }
                else if (Directory.Exists(link))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{link}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }

            // It's a URL - open in default browser
            Process.Start(new ProcessStartInfo
            {
                FileName = link,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinkDetectionService] Failed to open link: {link}, Error: {ex.Message}");
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

        // Word boundary characters (not including common URL/path characters)
        var separators = new HashSet<char> { ' ', '\t', '\n', '\r', '"', '\'', '<', '>', '`', '|', ';' };

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
            return new List<DetectedLink>();

        // 1. Find all URLs with their positions
        foreach (Match match in UrlPattern.Matches(text))
        {
            var url = CleanUrl(match.Value);
            if (!seen.Contains(url))
            {
                seen.Add(url);
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
                    LinkType = File.Exists(resolved) ? LinkType.File : LinkType.Directory
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
                    if (!seen.Contains(url))
                    {
                        seen.Add(url);
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
        return allLinks
            .OrderBy(x => x.Position)
            .TakeLast(maxLinks)
            .Select(x => x.Link)
            .ToList();
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

/// <summary>
/// Represents a detected link from terminal output.
/// </summary>
public class DetectedLink
{
    /// <summary>
    /// Short display text for the link (truncated if needed).
    /// </summary>
    public string DisplayText { get; set; } = "";

    /// <summary>
    /// The full URL or file path to open.
    /// </summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// The type of link detected.
    /// </summary>
    public LinkType LinkType { get; set; }

    /// <summary>
    /// Icon to display based on link type.
    /// </summary>
    public string Icon => LinkType switch
    {
        LinkType.Url => "🔗",
        LinkType.File => "📄",
        LinkType.Directory => "📁",
        LinkType.Custom => "🏷️",
        _ => "🔗"
    };

    /// <summary>
    /// Whether this link points to a file that can be previewed.
    /// </summary>
    public bool IsFile => LinkType == LinkType.File;
}

/// <summary>
/// Type of detected link.
/// </summary>
public enum LinkType
{
    Url,
    File,
    Directory,
    Custom
}
