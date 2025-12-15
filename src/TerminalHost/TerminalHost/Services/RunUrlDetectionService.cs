using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TerminalHost.Services;

/// <summary>
/// Service for detecting localhost URLs from terminal output.
/// </summary>
internal sealed class RunUrlDetectionService : IRunUrlDetectionService
{
    private readonly IProcessService _processService;

    public RunUrlDetectionService(IProcessService processService)
    {
        _processService = processService;
    }

    /// <summary>
    /// Built-in patterns for detecting localhost URLs from common frameworks.
    /// </summary>
    private static readonly Regex[] BuiltInPatterns =
    [
        // ASP.NET Core: "Now listening on: https://localhost:5001"
        new Regex(@"Now listening on:\s*(https?://[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Vite/Node: "Local: http://localhost:5173/"
        new Regex(@"Local:\s*(https?://(?:localhost|127\.0\.0\.1)[:\d/]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Flask: "Running on http://127.0.0.1:5000"
        new Regex(@"Running on\s*(https?://[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Generic: Any localhost or 127.0.0.1 URL with port
        new Regex(@"(https?://(?:localhost|127\.0\.0\.1):\d+[^\s]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Next.js: "ready - started server on 0.0.0.0:3000, url: http://localhost:3000"
        new Regex(@"url:\s*(https?://(?:localhost|127\.0\.0\.1)[:\d/]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Create React App: "Local: http://localhost:3000"
        new Regex(@"(?:Local|On Your Network):\s*(https?://[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Django: "Starting development server at http://127.0.0.1:8000/"
        new Regex(@"development server at\s*(https?://[^\s/]+/?)", RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Express: "Server listening on port 3000" + "http://localhost:3000"
        new Regex(@"listening on.*?(https?://(?:localhost|127\.0\.0\.1):\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// Detects a localhost URL from terminal output.
    /// </summary>
    /// <param name="output">The terminal output to scan.</param>
    /// <param name="customPattern">Optional custom regex pattern with a capture group.</param>
    /// <returns>The detected URL, or null if not found.</returns>
    public string? DetectUrl(string output, string? customPattern = null)
    {
        if (string.IsNullOrEmpty(output))
            return null;

        // Try custom pattern first if provided
        if (!string.IsNullOrEmpty(customPattern))
        {
            try
            {
                var customRegex = new Regex(customPattern, RegexOptions.IgnoreCase);
                var customMatch = customRegex.Match(output);
                if (customMatch.Success && customMatch.Groups.Count > 1)
                {
                    var url = customMatch.Groups[1].Value;
                    if (IsValidUrl(url))
                        return NormalizeUrl(url);
                }
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern, fall through to built-in patterns
            }
        }

        // Try built-in patterns
        foreach (var pattern in BuiltInPatterns)
        {
            var match = pattern.Match(output);
            if (match.Success && match.Groups.Count > 1)
            {
                var url = match.Groups[1].Value;
                if (IsValidUrl(url))
                    return NormalizeUrl(url);
            }
        }

        return null;
    }

    /// <summary>
    /// Detects all localhost URLs from terminal output.
    /// </summary>
    /// <param name="output">The terminal output to scan.</param>
    /// <param name="customPattern">Optional custom regex pattern with a capture group.</param>
    /// <returns>List of detected URLs.</returns>
    public List<string> DetectAllUrls(string output, string? customPattern = null)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(output))
            return [.. urls];

        // Try custom pattern first if provided
        if (!string.IsNullOrEmpty(customPattern))
        {
            try
            {
                var customRegex = new Regex(customPattern, RegexOptions.IgnoreCase);
                foreach (Match match in customRegex.Matches(output))
                {
                    if (match.Groups.Count > 1)
                    {
                        var url = match.Groups[1].Value;
                        if (IsValidUrl(url))
                            urls.Add(NormalizeUrl(url));
                    }
                }
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern
            }
        }

        // Try built-in patterns
        foreach (var pattern in BuiltInPatterns)
        {
            foreach (Match match in pattern.Matches(output))
            {
                if (match.Groups.Count > 1)
                {
                    var url = match.Groups[1].Value;
                    if (IsValidUrl(url))
                        urls.Add(NormalizeUrl(url));
                }
            }
        }

        return [.. urls];
    }

    /// <summary>
    /// Opens a URL in the default browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    public void OpenInBrowser(string url)
    {
        if (string.IsNullOrEmpty(url))
            return;

        try
        {
            _processService.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Ignore errors opening browser
        }
    }

    /// <summary>
    /// Validates that a URL is properly formed.
    /// </summary>
    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Normalizes a URL by removing trailing slashes and cleaning up.
    /// </summary>
    private static string NormalizeUrl(string url)
    {
        // Remove trailing slash unless it's the root
        if (url.EndsWith('/') && url.Count(c => c == '/') > 3)
        {
            url = url.TrimEnd('/');
        }
        return url;
    }
}
