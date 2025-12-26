using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using TerminalHost.Domain;

namespace TerminalHost.Services;

internal sealed class SearchService : ISearchService
{
    private readonly IFileSystem _fileSystem;
    private readonly IGitProcessRunner _gitProcessRunner;

    // Binary file extensions to skip during search
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".obj", ".o",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".tar", ".gz", ".7z", ".rar", ".dmg",
        ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".pdb", ".cache", ".lock"
    };

    // Directories to always exclude
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", ".vs", ".idea",
        "packages", "dist", "build", "out", ".next", ".nuget",
        "__pycache__", ".pytest_cache", "coverage", ".nyc_output"
    };

    public SearchService(IFileSystem fileSystem, IGitProcessRunner gitProcessRunner)
    {
        _fileSystem = fileSystem;
        _gitProcessRunner = gitProcessRunner;
    }

    public async Task<List<SearchResult>> SearchAsync(
        string directory,
        string searchPattern,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false,
        string? includePattern = null,
        string? excludePattern = null,
        bool respectGitignore = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchPattern))
            return [];

        // Try git grep first if respecting gitignore and in a git repository
        if (respectGitignore && IsGitRepository(directory))
        {
            var gitResults = await SearchWithGitGrepAsync(
                directory, searchPattern, caseSensitive, wholeWord, useRegex,
                includePattern, excludePattern, cancellationToken);

            if (gitResults != null)
                return gitResults;
        }

        // Fall back to manual search
        return await SearchManuallyAsync(
            directory, searchPattern, caseSensitive, wholeWord, useRegex,
            includePattern, excludePattern, cancellationToken);
    }

    public async Task<int> ReplaceInFileAsync(
        string filePath,
        string search,
        string replace,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false)
    {
        if (!_fileSystem.FileExists(filePath))
            return 0;

        var content = await _fileSystem.ReadAllTextAsync(filePath);
        var regex = BuildRegex(search, caseSensitive, wholeWord, useRegex);

        var matches = regex.Matches(content);
        if (matches.Count == 0)
            return 0;

        var newContent = regex.Replace(content, replace);
        _fileSystem.WriteAllText(filePath, newContent);

        return matches.Count;
    }

    public async Task<int> ReplaceAllAsync(
        string directory,
        string search,
        string replace,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false,
        string? includePattern = null,
        string? excludePattern = null,
        CancellationToken cancellationToken = default)
    {
        var results = await SearchAsync(
            directory, search, caseSensitive, wholeWord, useRegex,
            includePattern, excludePattern, true, cancellationToken);

        var totalReplacements = 0;

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await ReplaceInFileAsync(
                result.FilePath, search, replace, caseSensitive, wholeWord, useRegex);
            totalReplacements += count;
        }

        return totalReplacements;
    }

    private bool IsGitRepository(string directory)
    {
        var gitDir = Path.Combine(directory, ".git");
        return _fileSystem.DirectoryExists(gitDir);
    }

    private async Task<List<SearchResult>?> SearchWithGitGrepAsync(
        string directory,
        string searchPattern,
        bool caseSensitive,
        bool wholeWord,
        bool useRegex,
        string? includePattern,
        string? excludePattern,
        CancellationToken cancellationToken)
    {
        try
        {
            var args = new StringBuilder("grep -n"); // -n for line numbers

            if (!caseSensitive)
                args.Append(" -i");

            if (wholeWord)
                args.Append(" -w");

            if (!useRegex)
                args.Append(" -F"); // Fixed string (literal) search

            // Escape the pattern for shell
            var escapedPattern = searchPattern.Replace("\"", "\\\"");
            args.Append($" \"{escapedPattern}\"");

            // Add file patterns
            if (!string.IsNullOrEmpty(includePattern))
            {
                // Split multiple patterns separated by commas or semicolons
                var patterns = includePattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pattern in patterns)
                {
                    args.Append($" -- \"{pattern.Trim()}\"");
                }
            }

            var result = await _gitProcessRunner.RunGitCommandAsync(directory, args.ToString());

            if (result == null)
                return null;

            return ParseGitGrepOutput(result, directory, searchPattern, caseSensitive, excludePattern);
        }
        catch
        {
            return null;
        }
    }

    private List<SearchResult> ParseGitGrepOutput(
        string output,
        string directory,
        string searchPattern,
        bool caseSensitive,
        string? excludePattern)
    {
        var resultsByFile = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        var excludeRegex = string.IsNullOrEmpty(excludePattern)
            ? null
            : CreateGlobRegex(excludePattern);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Format: filename:lineNumber:lineContent
            var firstColon = line.IndexOf(':');
            if (firstColon == -1) continue;

            var secondColon = line.IndexOf(':', firstColon + 1);
            if (secondColon == -1) continue;

            var filePath = line[..firstColon];
            if (!int.TryParse(line.AsSpan(firstColon + 1, secondColon - firstColon - 1), out var lineNumber))
                continue;

            var lineContent = line[(secondColon + 1)..];

            // Check exclude pattern
            if (excludeRegex != null && excludeRegex.IsMatch(filePath))
                continue;

            var fullPath = Path.Combine(directory, filePath);

            if (!resultsByFile.TryGetValue(filePath, out var searchResult))
            {
                searchResult = new SearchResult
                {
                    FilePath = fullPath,
                    RelativePath = filePath,
                    Matches = []
                };
                resultsByFile[filePath] = searchResult;
            }

            // Find match positions in the line
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            var pos = 0;
            while ((pos = lineContent.IndexOf(searchPattern, pos, comparison)) != -1)
            {
                searchResult.Matches.Add(new SearchMatch
                {
                    LineNumber = lineNumber,
                    LineContent = lineContent,
                    MatchStart = pos,
                    MatchLength = searchPattern.Length
                });
                pos += searchPattern.Length;
            }
        }

        return resultsByFile.Values
            .OrderBy(r => r.RelativePath)
            .ToList();
    }

    private async Task<List<SearchResult>> SearchManuallyAsync(
        string directory,
        string searchPattern,
        bool caseSensitive,
        bool wholeWord,
        bool useRegex,
        string? includePattern,
        string? excludePattern,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var regex = BuildRegex(searchPattern, caseSensitive, wholeWord, useRegex);
        var includeRegex = string.IsNullOrEmpty(includePattern)
            ? null
            : CreateGlobRegex(includePattern);
        var excludeRegex = string.IsNullOrEmpty(excludePattern)
            ? null
            : CreateGlobRegex(excludePattern);

        await SearchDirectoryAsync(
            directory, directory, regex, includeRegex, excludeRegex,
            results, cancellationToken);

        return results.OrderBy(r => r.RelativePath).ToList();
    }

    private async Task SearchDirectoryAsync(
        string rootDirectory,
        string currentDirectory,
        Regex regex,
        Regex? includeRegex,
        Regex? excludeRegex,
        List<SearchResult> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Search files in current directory
            var files = _fileSystem.GetFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly);

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath);
                var relativePath = Path.GetRelativePath(rootDirectory, filePath);

                // Skip binary files
                if (BinaryExtensions.Contains(extension))
                    continue;

                // Check include pattern
                if (includeRegex != null && !includeRegex.IsMatch(fileName))
                    continue;

                // Check exclude pattern
                if (excludeRegex != null && excludeRegex.IsMatch(fileName))
                    continue;

                var searchResult = await SearchFileAsync(filePath, relativePath, regex, cancellationToken);
                if (searchResult != null && searchResult.Matches.Count > 0)
                {
                    results.Add(searchResult);
                }
            }

            // Recurse into subdirectories
            var directories = _fileSystem.GetDirectories(currentDirectory);

            foreach (var subDir in directories)
            {
                var dirName = Path.GetFileName(subDir);

                // Skip excluded directories
                if (ExcludedDirectories.Contains(dirName))
                    continue;

                await SearchDirectoryAsync(
                    rootDirectory, subDir, regex, includeRegex, excludeRegex,
                    results, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (IOException)
        {
            // Skip directories with IO errors
        }
    }

    private async Task<SearchResult?> SearchFileAsync(
        string filePath,
        string relativePath,
        Regex regex,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _fileSystem.ReadAllTextAsync(filePath);
            cancellationToken.ThrowIfCancellationRequested();

            var matches = new List<SearchMatch>();
            var lines = content.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var lineMatches = regex.Matches(line);

                foreach (Match match in lineMatches)
                {
                    matches.Add(new SearchMatch
                    {
                        LineNumber = i + 1, // 1-based line numbers
                        LineContent = line,
                        MatchStart = match.Index,
                        MatchLength = match.Length
                    });
                }
            }

            if (matches.Count == 0)
                return null;

            return new SearchResult
            {
                FilePath = filePath,
                RelativePath = relativePath,
                Matches = matches
            };
        }
        catch
        {
            return null;
        }
    }

    private static Regex BuildRegex(string pattern, bool caseSensitive, bool wholeWord, bool useRegex)
    {
        var regexPattern = useRegex ? pattern : Regex.Escape(pattern);

        if (wholeWord)
        {
            regexPattern = $@"\b{regexPattern}\b";
        }

        var options = RegexOptions.Compiled;
        if (!caseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(regexPattern, options);
    }

    private static Regex CreateGlobRegex(string globPattern)
    {
        // Support multiple patterns separated by commas or semicolons
        var patterns = globPattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var regexPatterns = patterns.Select(p =>
        {
            var escaped = Regex.Escape(p.Trim())
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".");
            return $"({escaped})";
        });

        return new Regex(string.Join("|", regexPatterns), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
