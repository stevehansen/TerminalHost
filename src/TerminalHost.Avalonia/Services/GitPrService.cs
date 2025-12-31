using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for detecting and fetching GitHub PR information.
/// </summary>
public partial class GitPrService : IGitPrService
{
    // Patterns to detect PR/issue numbers in task titles
    [GeneratedRegex(@"PR\s*#?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrPatternRegex();

    [GeneratedRegex(@"#(\d+)")]
    private static partial Regex IssueHashPatternRegex();

    [GeneratedRegex(@"pull/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PullPatternRegex();

    [GeneratedRegex(@"issues?/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IssueSlashPatternRegex();

    private bool? _ghAvailable;

    public (string? prNumber, string? issueNumber) ParseTaskTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null);

        // Try PR patterns first (more specific)
        var prMatch = PrPatternRegex().Match(title);
        if (prMatch.Success)
        {
            return (prMatch.Groups[1].Value, prMatch.Groups[1].Value);
        }

        var pullMatch = PullPatternRegex().Match(title);
        if (pullMatch.Success)
        {
            return (pullMatch.Groups[1].Value, pullMatch.Groups[1].Value);
        }

        // Try issue patterns
        var issueSlashMatch = IssueSlashPatternRegex().Match(title);
        if (issueSlashMatch.Success)
        {
            return (null, issueSlashMatch.Groups[1].Value);
        }

        // Generic #123 pattern (could be PR or issue)
        var hashMatch = IssueHashPatternRegex().Match(title);
        if (hashMatch.Success)
        {
            var number = hashMatch.Groups[1].Value;
            return (number, number); // Could be either
        }

        return (null, null);
    }

    public async Task<string?> FindBranchForIssueAsync(string projectPath, string issueNumber)
    {
        if (string.IsNullOrEmpty(projectPath) || string.IsNullOrEmpty(issueNumber))
            return null;

        try
        {
            // Get all branches
            var (exitCode, output) = await RunGitCommandAsync(projectPath, "branch -a --list");
            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            var branches = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(b => b.TrimStart('*', ' ').Trim())
                .ToList();

            // Patterns to look for (in order of preference)
            var patterns = new[]
            {
                $"issues/{issueNumber}",
                $"issue/{issueNumber}",
                $"feature/{issueNumber}",
                $"fix/{issueNumber}",
                $"bugfix/{issueNumber}",
                $"hotfix/{issueNumber}",
                $"pr-{issueNumber}",
                $"{issueNumber}-"  // e.g., "123-fix-auth"
            };

            foreach (var pattern in patterns)
            {
                var match = branches.FirstOrDefault(b =>
                    b.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    // Remove remote prefix if present
                    if (match.StartsWith("remotes/origin/", StringComparison.OrdinalIgnoreCase))
                    {
                        match = match.Substring("remotes/origin/".Length);
                    }
                    return match;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<GitPrDetails?> FetchPrDetailsAsync(string projectPath, string prNumber)
    {
        if (!IsGitHubCliAvailable())
            return null;

        try
        {
            // Use gh CLI to get PR details as JSON
            var (exitCode, output) = await RunCommandAsync(
                projectPath,
                "gh",
                $"pr view {prNumber} --json title,author,state,baseRefName,headRefName,additions,deletions,changedFiles,updatedAt");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            // Parse JSON response
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            return new GitPrDetails
            {
                Title = root.GetProperty("title").GetString() ?? "",
                Author = root.TryGetProperty("author", out var author) && author.TryGetProperty("login", out var login)
                    ? login.GetString() ?? ""
                    : "",
                State = root.GetProperty("state").GetString()?.ToLowerInvariant() ?? "",
                BaseBranch = root.TryGetProperty("baseRefName", out var baseBranch) ? baseBranch.GetString() : null,
                HeadBranch = root.TryGetProperty("headRefName", out var headBranch) ? headBranch.GetString() : null,
                Additions = root.TryGetProperty("additions", out var adds) ? adds.GetInt32() : 0,
                Deletions = root.TryGetProperty("deletions", out var dels) ? dels.GetInt32() : 0,
                ChangedFiles = root.TryGetProperty("changedFiles", out var files) ? files.GetInt32() : 0,
                UpdatedAt = root.TryGetProperty("updatedAt", out var updated) && DateTime.TryParse(updated.GetString(), out var dt)
                    ? dt
                    : null,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    public bool IsGitHubCliAvailable()
    {
        if (_ghAvailable.HasValue)
            return _ghAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            _ghAvailable = process?.ExitCode == 0;
        }
        catch
        {
            _ghAvailable = false;
        }

        return _ghAvailable ?? false;
    }

    public async Task<bool> AutoDetectPrInfoAsync(FocusTask task, string projectPath)
    {
        if (task == null || string.IsNullOrEmpty(task.Title))
            return false;

        var (prNumber, issueNumber) = ParseTaskTitle(task.Title);
        if (prNumber == null && issueNumber == null)
            return false;

        var updated = false;

        // Store PR number if found
        if (prNumber != null && task.LinkedPrNumber != prNumber)
        {
            task.LinkedPrNumber = prNumber;
            updated = true;
        }

        // Try to find matching branch
        var searchNumber = issueNumber ?? prNumber;
        if (searchNumber != null && !string.IsNullOrEmpty(projectPath))
        {
            var branch = await FindBranchForIssueAsync(projectPath, searchNumber);
            if (branch != null && task.LinkedBranch != branch)
            {
                task.LinkedBranch = branch;
                updated = true;
            }
        }

        // Fetch PR details if we have a PR number
        if (prNumber != null && !string.IsNullOrEmpty(projectPath))
        {
            var details = await FetchPrDetailsAsync(projectPath, prNumber);
            if (details != null)
            {
                task.PrDetails = details;
                updated = true;
            }
        }

        return updated;
    }

    private static async Task<(int exitCode, string output)> RunGitCommandAsync(string workingDirectory, string arguments)
    {
        return await RunCommandAsync(workingDirectory, "git", arguments);
    }

    private static async Task<(int exitCode, string output)> RunCommandAsync(string workingDirectory, string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "");

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, output);
        }
        catch
        {
            return (-1, "");
        }
    }
}
