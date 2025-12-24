using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Service for searching across files in a directory.
/// </summary>
public class SearchService : ISearchService
{
    private readonly IFileSystem _fileSystem;
    private readonly IGitIgnoreService _gitIgnoreService;

    // Common binary file extensions to skip
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".obj", ".o", ".a", ".so", ".dylib",
        ".zip", ".tar", ".gz", ".rar", ".7z", ".bz2",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg",
        ".mp3", ".mp4", ".avi", ".mov", ".wmv", ".wav", ".flac",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".db", ".sqlite", ".mdb",
        ".class", ".pyc", ".pyo", ".cache"
    };

    // Common directories to always skip
    private static readonly HashSet<string> AlwaysExcludeDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea"
    };

    public SearchService(IFileSystem fileSystem, IGitIgnoreService gitIgnoreService)
    {
        _fileSystem = fileSystem;
        _gitIgnoreService = gitIgnoreService;
    }

    public async Task<SearchResults> SearchAsync(
        string pattern,
        string workingDirectory,
        SearchOptions options,
        Action<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new SearchResults();

        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(workingDirectory))
            return results;

        try
        {
            // Build regex for the search
            var regex = BuildSearchRegex(pattern, options);
            if (regex == null)
                return results;

            // Get gitignored paths if needed
            HashSet<string>? ignoredPaths = null;
            if (options.UseGitignore && _gitIgnoreService.IsGitRepository(workingDirectory))
            {
                ignoredPaths = await _gitIgnoreService.GetIgnoredPathsAsync(workingDirectory);
            }

            // Parse exclude patterns
            var excludePatterns = ParsePatterns(options.ExcludePattern);
            var includePatterns = ParsePatterns(options.IncludePattern);

            // Get all files to search
            var files = GetFilesToSearch(workingDirectory, includePatterns, excludePatterns, ignoredPaths, cancellationToken);

            var fileCount = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                fileCount++;
                progressCallback?.Invoke(fileCount);

                // Check if max results reached
                if (results.TotalMatchCount >= options.MaxResults)
                {
                    results.Truncated = true;
                    break;
                }

                var fileResult = await SearchFileAsync(file, workingDirectory, regex, options,
                    options.MaxResults - results.TotalMatchCount, cancellationToken);

                if (fileResult != null && fileResult.MatchCount > 0)
                {
                    results.Files.Add(fileResult);
                    results.TotalMatchCount += fileResult.MatchCount;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled
        }

        stopwatch.Stop();
        results.SearchTimeMs = stopwatch.ElapsedMilliseconds;

        return results;
    }

    public async Task<int> ReplaceAsync(
        string pattern,
        string replacement,
        string workingDirectory,
        SearchOptions options,
        IEnumerable<string>? filesToReplace = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(workingDirectory))
            return 0;

        var regex = BuildSearchRegex(pattern, options);
        if (regex == null)
            return 0;

        var totalReplacements = 0;

        // If specific files provided, use those; otherwise search all
        IEnumerable<string> files;
        if (filesToReplace != null)
        {
            files = filesToReplace;
        }
        else
        {
            HashSet<string>? ignoredPaths = null;
            if (options.UseGitignore && _gitIgnoreService.IsGitRepository(workingDirectory))
            {
                ignoredPaths = await _gitIgnoreService.GetIgnoredPathsAsync(workingDirectory);
            }

            var excludePatterns = ParsePatterns(options.ExcludePattern);
            var includePatterns = ParsePatterns(options.IncludePattern);
            files = GetFilesToSearch(workingDirectory, includePatterns, excludePatterns, ignoredPaths, cancellationToken);
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var content = await _fileSystem.ReadAllTextAsync(file);
                var newContent = regex.Replace(content, replacement);

                if (content != newContent)
                {
                    var matchCount = regex.Matches(content).Count;
                    _fileSystem.WriteAllText(file, newContent);
                    totalReplacements += matchCount;
                }
            }
            catch
            {
                // Skip files that can't be read or written
            }
        }

        return totalReplacements;
    }

    private Regex? BuildSearchRegex(string pattern, SearchOptions options)
    {
        try
        {
            string regexPattern;

            if (options.UseRegex)
            {
                regexPattern = pattern;
            }
            else
            {
                // Escape special regex characters for literal search
                regexPattern = Regex.Escape(pattern);
            }

            if (options.WholeWord)
            {
                regexPattern = $@"\b{regexPattern}\b";
            }

            var regexOptions = RegexOptions.Multiline | RegexOptions.Compiled;
            if (!options.CaseSensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            return new Regex(regexPattern, regexOptions, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern
            return null;
        }
    }

    private IEnumerable<string> GetFilesToSearch(
        string workingDirectory,
        List<string> includePatterns,
        List<string> excludePatterns,
        HashSet<string>? ignoredPaths,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(workingDirectory);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDir = stack.Pop();

            // Skip always-excluded directories
            var dirName = Path.GetFileName(currentDir);
            if (AlwaysExcludeDirs.Contains(dirName))
                continue;

            // Skip gitignored directories
            if (ignoredPaths != null && ignoredPaths.Contains(currentDir))
                continue;

            // Check exclude patterns for directories
            var relativeDirPath = GetRelativePath(workingDirectory, currentDir);
            if (IsExcluded(relativeDirPath, excludePatterns, isDirectory: true))
                continue;

            IEnumerable<string> directories;
            IEnumerable<string> files;

            try
            {
                directories = _fileSystem.GetDirectories(currentDir);
                files = _fileSystem.GetFiles(currentDir);
            }
            catch
            {
                continue;
            }

            // Add subdirectories to stack
            foreach (var dir in directories)
            {
                stack.Push(dir);
            }

            // Process files
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip binary files
                var ext = Path.GetExtension(file);
                if (BinaryExtensions.Contains(ext))
                    continue;

                // Skip gitignored files
                if (ignoredPaths != null && ignoredPaths.Contains(file))
                    continue;

                var relativeFilePath = GetRelativePath(workingDirectory, file);

                // Check exclude patterns
                if (IsExcluded(relativeFilePath, excludePatterns, isDirectory: false))
                    continue;

                // Check include patterns (if any specified, file must match at least one)
                if (includePatterns.Count > 0 && !IsIncluded(relativeFilePath, includePatterns))
                    continue;

                yield return file;
            }
        }
    }

    private async Task<SearchFileResult?> SearchFileAsync(
        string filePath,
        string workingDirectory,
        Regex regex,
        SearchOptions options,
        int maxMatches,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _fileSystem.ReadAllTextAsync(filePath);
            var lines = content.Split('\n');
            var matches = new List<SearchMatch>();

            for (var lineIndex = 0; lineIndex < lines.Length && matches.Count < maxMatches; lineIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = lines[lineIndex].TrimEnd('\r');
                var lineMatches = regex.Matches(line);

                foreach (Match match in lineMatches)
                {
                    if (matches.Count >= maxMatches)
                        break;

                    var searchMatch = new SearchMatch
                    {
                        LineNumber = lineIndex + 1,
                        Column = match.Index,
                        MatchLength = match.Length,
                        LineText = line,
                        MatchedText = match.Value
                    };

                    // Add context lines before
                    for (var i = Math.Max(0, lineIndex - options.ContextLines); i < lineIndex; i++)
                    {
                        searchMatch.ContextBefore.Add(new ContextLine
                        {
                            LineNumber = i + 1,
                            Text = lines[i].TrimEnd('\r')
                        });
                    }

                    // Add context lines after
                    for (var i = lineIndex + 1; i <= Math.Min(lines.Length - 1, lineIndex + options.ContextLines); i++)
                    {
                        searchMatch.ContextAfter.Add(new ContextLine
                        {
                            LineNumber = i + 1,
                            Text = lines[i].TrimEnd('\r')
                        });
                    }

                    matches.Add(searchMatch);
                }
            }

            if (matches.Count == 0)
                return null;

            return new SearchFileResult
            {
                FullPath = filePath,
                RelativePath = GetRelativePath(workingDirectory, filePath),
                Matches = matches
            };
        }
        catch
        {
            // Skip files that can't be read (binary, locked, etc.)
            return null;
        }
    }

    private static List<string> ParsePatterns(string? patternString)
    {
        if (string.IsNullOrWhiteSpace(patternString))
            return [];

        return patternString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static bool IsExcluded(string relativePath, List<string> excludePatterns, bool isDirectory)
    {
        if (excludePatterns.Count == 0)
            return false;

        foreach (var pattern in excludePatterns)
        {
            if (MatchesPattern(relativePath, pattern, isDirectory))
                return true;

            // Also check if any part of the path matches the pattern (for directory patterns)
            var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in pathParts)
            {
                if (string.Equals(part, pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool IsIncluded(string relativePath, List<string> includePatterns)
    {
        if (includePatterns.Count == 0)
            return true;

        foreach (var pattern in includePatterns)
        {
            if (MatchesPattern(relativePath, pattern, isDirectory: false))
                return true;
        }

        return false;
    }

    private static bool MatchesPattern(string path, string pattern, bool isDirectory)
    {
        // Convert glob pattern to regex
        // Supports: *, **, ?
        var normalizedPath = path.Replace(Path.DirectorySeparatorChar, '/');
        var normalizedPattern = pattern.Replace(Path.DirectorySeparatorChar, '/');

        // Handle simple cases first
        if (!normalizedPattern.Contains('*') && !normalizedPattern.Contains('?'))
        {
            // Exact match or ends with
            return normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.EndsWith("/" + normalizedPattern, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedPattern + "/", StringComparison.OrdinalIgnoreCase);
        }

        // Convert glob to regex
        var regexPattern = "^";
        var i = 0;
        while (i < normalizedPattern.Length)
        {
            var c = normalizedPattern[i];
            if (c == '*')
            {
                if (i + 1 < normalizedPattern.Length && normalizedPattern[i + 1] == '*')
                {
                    // ** matches any path segments
                    regexPattern += ".*";
                    i += 2;
                    // Skip trailing /
                    if (i < normalizedPattern.Length && normalizedPattern[i] == '/')
                        i++;
                }
                else
                {
                    // * matches anything except /
                    regexPattern += "[^/]*";
                    i++;
                }
            }
            else if (c == '?')
            {
                regexPattern += "[^/]";
                i++;
            }
            else
            {
                regexPattern += Regex.Escape(c.ToString());
                i++;
            }
        }
        regexPattern += "$";

        try
        {
            return Regex.IsMatch(normalizedPath, regexPattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        var normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedFull = Path.GetFullPath(fullPath);

        if (normalizedFull.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFull[(normalizedBase.Length + 1)..];
        }

        return fullPath;
    }
}
