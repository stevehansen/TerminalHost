using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Implementation of IGitHubService using the gh CLI.
/// </summary>
internal sealed class GitHubService : IGitHubService
{
    private bool? _ghAvailable;
    private string? _currentUser;
    private DateTime _lastUserFetch = DateTime.MinValue;
    private readonly TimeSpan _userCacheDuration = TimeSpan.FromMinutes(30);

    public bool IsGitHubCliAvailable()
    {
        if (_ghAvailable.HasValue)
            return _ghAvailable.Value;

        try
        {
            // Use PowerShell to ensure PATH is resolved correctly (same as SetupViewModel)
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"gh auth token\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _ghAvailable = false;
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // If we got a token (non-empty output) and exit code 0, we're authenticated
            _ghAvailable = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            _ghAvailable = false;
        }

        return _ghAvailable ?? false;
    }

    public void ResetAvailabilityCache()
    {
        _ghAvailable = null;
        _currentUser = null;
    }

    public async Task<List<GitHubPullRequest>> GetReviewRequestsAsync()
    {
        if (!IsGitHubCliAvailable())
            return [];

        try
        {
            // Use GitHub API search for PRs where current user is requested reviewer (exclude drafts)
            var (exitCode, output) = await RunGhCommandAsync(
                null,
                "api search/issues --method GET -f q='is:pr is:open review-requested:@me draft:false' -f per_page=100");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return [];

            return ParsePullRequestsFromApi(output);
        }
        catch
        {
            return [];
        }
    }

    public async Task<List<GitHubPullRequest>> GetMyPullRequestsAsync()
    {
        if (!IsGitHubCliAvailable())
            return [];

        try
        {
            // Use GitHub API search endpoint directly
            var (exitCode, output) = await RunGhCommandAsync(
                null,
                "api search/issues --method GET -f q='is:pr is:open author:@me' -f per_page=100");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return [];

            return ParsePullRequestsFromApi(output);
        }
        catch
        {
            return [];
        }
    }

    public async Task<List<GitHubIssue>> GetMyIssuesAsync()
    {
        if (!IsGitHubCliAvailable())
            return [];

        try
        {
            var (exitCode, output) = await RunGhCommandAsync(
                null,
                "search issues --assignee=@me --state=open --json number,title,repository,author,labels,createdAt,updatedAt --limit 100");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return [];

            return ParseIssuesFromSearch(output);
        }
        catch
        {
            return [];
        }
    }

    public async Task<List<GitHubWorkflowRun>> GetFailedWorkflowRunsAsync()
    {
        if (!IsGitHubCliAvailable())
            return [];

        try
        {
            // Get recent failed runs - this requires being in a repo context or specifying repos
            // For now, we'll return empty and implement per-repo workflow checking
            // A more complete implementation would iterate through user's repos
            return [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<GitHubPullRequest?> GetPullRequestDetailsAsync(string repo, int prNumber)
    {
        if (!IsGitHubCliAvailable())
            return null;

        try
        {
            var (exitCode, output) = await RunGhCommandAsync(
                null,
                $"pr view {prNumber} --repo {repo} --json number,title,author,state,baseRefName,headRefName,additions,deletions,changedFiles,createdAt,updatedAt,reviewDecision,statusCheckRollup");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            return ParsePullRequestDetails(output, repo);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<GitHubPrFile>> GetPullRequestFilesAsync(string repo, int prNumber)
    {
        if (!IsGitHubCliAvailable())
            return [];

        try
        {
            var (exitCode, output) = await RunGhCommandAsync(
                null,
                $"pr view {prNumber} --repo {repo} --json files");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return [];

            return ParsePullRequestFiles(output);
        }
        catch
        {
            return [];
        }
    }

    public async Task<string?> GetPullRequestFileDiffAsync(string repo, int prNumber, string filePath)
    {
        if (!IsGitHubCliAvailable())
            return null;

        try
        {
            var (exitCode, output) = await RunGhCommandAsync(
                null,
                $"pr diff {prNumber} --repo {repo}");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            // Extract the diff for the specific file
            return ExtractFileDiff(output, filePath);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CheckoutPullRequestAsync(string workingDirectory, int prNumber)
    {
        if (!IsGitHubCliAvailable())
            return false;

        try
        {
            var (exitCode, _) = await RunGhCommandAsync(workingDirectory, $"pr checkout {prNumber}");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ApprovePullRequestAsync(string workingDirectory, int prNumber, string? comment = null)
    {
        if (!IsGitHubCliAvailable())
            return false;

        try
        {
            var args = $"pr review {prNumber} --approve";
            if (!string.IsNullOrEmpty(comment))
            {
                args += $" --body \"{EscapeArgument(comment)}\"";
            }

            var (exitCode, _) = await RunGhCommandAsync(workingDirectory, args);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RequestChangesAsync(string workingDirectory, int prNumber, string comment)
    {
        if (!IsGitHubCliAvailable())
            return false;

        try
        {
            var (exitCode, _) = await RunGhCommandAsync(
                workingDirectory,
                $"pr review {prNumber} --request-changes --body \"{EscapeArgument(comment)}\"");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CommentOnPullRequestAsync(string workingDirectory, int prNumber, string comment)
    {
        if (!IsGitHubCliAvailable())
            return false;

        try
        {
            var (exitCode, _) = await RunGhCommandAsync(
                workingDirectory,
                $"pr comment {prNumber} --body \"{EscapeArgument(comment)}\"");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> MergePullRequestAsync(string workingDirectory, int prNumber, string method = "squash")
    {
        if (!IsGitHubCliAvailable())
            return false;

        try
        {
            var methodArg = method.ToLowerInvariant() switch
            {
                "merge" => "--merge",
                "rebase" => "--rebase",
                _ => "--squash"
            };

            var (exitCode, _) = await RunGhCommandAsync(
                workingDirectory,
                $"pr merge {prNumber} {methodArg} --delete-branch");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<GitHubRepository>> GetUserRepositoriesAsync(int limit = 100)
    {
        if (!IsGitHubCliAvailable())
            return [];

        try
        {
            var (exitCode, output) = await RunGhCommandAsync(
                null,
                $"repo list --json nameWithOwner,description,isPrivate,pushedAt --limit {limit}");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return [];

            return ParseRepositories(output);
        }
        catch
        {
            return [];
        }
    }

    public async Task<string?> CloneRepositoryAsync(string repoFullName, string targetDirectory)
    {
        if (!IsGitHubCliAvailable())
            return null;

        try
        {
            var repoName = repoFullName.Contains('/') ? repoFullName.Split('/').Last() : repoFullName;
            var clonePath = Path.Combine(targetDirectory, repoName);

            var (exitCode, _) = await RunGhCommandAsync(
                targetDirectory,
                $"repo clone {repoFullName} \"{clonePath}\"");

            return exitCode == 0 ? clonePath : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetCurrentUserAsync()
    {
        if (_currentUser != null && DateTime.Now - _lastUserFetch < _userCacheDuration)
            return _currentUser;

        if (!IsGitHubCliAvailable())
            return null;

        try
        {
            var (exitCode, output) = await RunGhCommandAsync(null, "api user --jq .login");

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                _currentUser = output.Trim();
                _lastUserFetch = DateTime.Now;
                return _currentUser;
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    public async Task<GitHubPullRequest?> GetCurrentPullRequestAsync(string workingDirectory)
    {
        if (!IsGitHubCliAvailable() || string.IsNullOrEmpty(workingDirectory))
            return null;

        try
        {
            var (exitCode, output) = await RunGhCommandAsync(
                workingDirectory,
                "pr view --json number,title,body,author,state,baseRefName,headRefName,additions,deletions,changedFiles,createdAt,updatedAt,reviewDecision");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            // Get the repo name from the working directory
            var (repoExitCode, repoOutput) = await RunGhCommandAsync(workingDirectory, "repo view --json nameWithOwner --jq .nameWithOwner");
            var repo = repoExitCode == 0 ? repoOutput?.Trim() ?? "" : "";

            return ParsePullRequestDetails(output, repo);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GetFileDiffAsync(string workingDirectory, string filename)
    {
        if (string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(filename))
            return "";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"diff HEAD -- \"{filename}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            using var process = Process.Start(psi);
            if (process == null)
                return "";

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // If no local changes, try to get diff against the base branch
            if (string.IsNullOrWhiteSpace(output))
            {
                // Get the base branch (usually main or master)
                var basePsi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "symbolic-ref refs/remotes/origin/HEAD --short",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

                using var baseProcess = Process.Start(basePsi);
                if (baseProcess == null)
                    return "";

                var baseBranch = (await baseProcess.StandardOutput.ReadToEndAsync()).Trim();
                await baseProcess.WaitForExitAsync();

                if (!string.IsNullOrEmpty(baseBranch))
                {
                    // origin/main -> main
                    baseBranch = baseBranch.Replace("origin/", "");

                    var diffPsi = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"diff origin/{baseBranch}...HEAD -- \"{filename}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        WorkingDirectory = workingDirectory
                    };

                    using var diffProcess = Process.Start(diffPsi);
                    if (diffProcess != null)
                    {
                        output = await diffProcess.StandardOutput.ReadToEndAsync();
                        await diffProcess.WaitForExitAsync();
                    }
                }
            }

            return output;
        }
        catch
        {
            return "";
        }
    }

    #region Private Helpers

    private static async Task<(int exitCode, string output)> RunGhCommandAsync(string? workingDirectory, string arguments)
    {
        try
        {
            // Use PowerShell to ensure PATH is resolved correctly (same as SetupViewModel)
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"gh {arguments}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

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

    private static string EscapeArgument(string arg)
    {
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static List<GitHubPullRequest> ParsePullRequestsFromSearch(string json)
    {
        var results = new List<GitHubPullRequest>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in root.EnumerateArray())
            {
                var pr = new GitHubPullRequest
                {
                    Number = item.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                    Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    Author = item.TryGetProperty("author", out var author) && author.TryGetProperty("login", out var login)
                        ? login.GetString() ?? ""
                        : "",
                    Additions = item.TryGetProperty("additions", out var adds) ? adds.GetInt32() : 0,
                    Deletions = item.TryGetProperty("deletions", out var dels) ? dels.GetInt32() : 0,
                    State = "open"
                };

                // Repository comes as an object with nameWithOwner
                if (item.TryGetProperty("repository", out var repo))
                {
                    if (repo.TryGetProperty("nameWithOwner", out var nameWithOwner))
                    {
                        pr.Repository = nameWithOwner.GetString() ?? "";
                    }
                }

                if (item.TryGetProperty("createdAt", out var created) && DateTime.TryParse(created.GetString(), out var createdDt))
                {
                    pr.CreatedAt = createdDt;
                }

                if (item.TryGetProperty("updatedAt", out var updated) && DateTime.TryParse(updated.GetString(), out var updatedDt))
                {
                    pr.UpdatedAt = updatedDt;
                }

                results.Add(pr);
            }
        }
        catch
        {
            // Return what we have
        }

        return results;
    }

    private static GitHubPullRequest? ParsePullRequestDetails(string json, string repo)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var pr = new GitHubPullRequest
            {
                Number = root.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                Title = root.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                Body = root.TryGetProperty("body", out var body) ? body.GetString() : null,
                Repository = repo,
                Author = root.TryGetProperty("author", out var author) && author.TryGetProperty("login", out var login)
                    ? login.GetString() ?? ""
                    : "",
                State = root.TryGetProperty("state", out var state) ? state.GetString()?.ToLowerInvariant() ?? "open" : "open",
                BaseBranch = root.TryGetProperty("baseRefName", out var baseBranch) ? baseBranch.GetString() : null,
                HeadBranch = root.TryGetProperty("headRefName", out var headBranch) ? headBranch.GetString() : null,
                Additions = root.TryGetProperty("additions", out var adds) ? adds.GetInt32() : 0,
                Deletions = root.TryGetProperty("deletions", out var dels) ? dels.GetInt32() : 0,
                ChangedFiles = root.TryGetProperty("changedFiles", out var files) ? files.GetInt32() : 0
            };

            if (root.TryGetProperty("createdAt", out var created) && DateTime.TryParse(created.GetString(), out var createdDt))
            {
                pr.CreatedAt = createdDt;
            }

            if (root.TryGetProperty("updatedAt", out var updated) && DateTime.TryParse(updated.GetString(), out var updatedDt))
            {
                pr.UpdatedAt = updatedDt;
            }

            // Parse review decision
            if (root.TryGetProperty("reviewDecision", out var reviewDecision))
            {
                var decision = reviewDecision.GetString()?.ToUpperInvariant();
                pr.ReviewDecision = decision switch
                {
                    "APPROVED" => "approved",
                    "CHANGES_REQUESTED" => "changes_requested",
                    "REVIEW_REQUIRED" => "review_required",
                    _ => null
                };
            }

            // Parse CI status from statusCheckRollup
            if (root.TryGetProperty("statusCheckRollup", out var statusRollup) && statusRollup.ValueKind == JsonValueKind.Array)
            {
                var hasFailure = false;
                var hasPending = false;
                var hasSuccess = false;

                foreach (var check in statusRollup.EnumerateArray())
                {
                    var conclusion = check.TryGetProperty("conclusion", out var conc) ? conc.GetString() : null;
                    var checkState = check.TryGetProperty("state", out var st) ? st.GetString() : null;

                    if (conclusion == "FAILURE" || checkState == "FAILURE")
                        hasFailure = true;
                    else if (conclusion == "SUCCESS" || checkState == "SUCCESS")
                        hasSuccess = true;
                    else if (string.IsNullOrEmpty(conclusion) || checkState == "PENDING")
                        hasPending = true;
                }

                pr.CiStatus = hasFailure ? "failing" : hasPending ? "pending" : hasSuccess ? "passing" : null;
            }

            return pr;
        }
        catch
        {
            return null;
        }
    }

    private static List<GitHubIssue> ParseIssuesFromSearch(string json)
    {
        var results = new List<GitHubIssue>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in root.EnumerateArray())
            {
                var issue = new GitHubIssue
                {
                    Number = item.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                    Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    Author = item.TryGetProperty("author", out var author) && author.TryGetProperty("login", out var login)
                        ? login.GetString() ?? ""
                        : "",
                    State = "open"
                };

                if (item.TryGetProperty("repository", out var repo))
                {
                    if (repo.TryGetProperty("nameWithOwner", out var nameWithOwner))
                    {
                        issue.Repository = nameWithOwner.GetString() ?? "";
                    }
                }

                if (item.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in labels.EnumerateArray())
                    {
                        var name = label.TryGetProperty("name", out var labelName) ? labelName.GetString() ?? "" : "";
                        var color = label.TryGetProperty("color", out var labelColor) ? labelColor.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            issue.Labels.Add(new GitHubLabel { Name = name, Color = color });
                        }
                    }
                }

                if (item.TryGetProperty("createdAt", out var created) && DateTime.TryParse(created.GetString(), out var createdDt))
                {
                    issue.CreatedAt = createdDt;
                }

                if (item.TryGetProperty("updatedAt", out var updated) && DateTime.TryParse(updated.GetString(), out var updatedDt))
                {
                    issue.UpdatedAt = updatedDt;
                }

                results.Add(issue);
            }
        }
        catch
        {
            // Return what we have
        }

        return results;
    }

    private static List<GitHubPullRequest> ParsePullRequestsFromApi(string json)
    {
        var results = new List<GitHubPullRequest>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // API returns { "items": [...], "total_count": N }
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in items.EnumerateArray())
            {
                var pr = new GitHubPullRequest
                {
                    Number = item.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                    Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    Author = item.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login)
                        ? login.GetString() ?? ""
                        : "",
                    State = "open"
                };

                // Repository URL format: https://api.github.com/repos/owner/repo
                if (item.TryGetProperty("repository_url", out var repoUrl))
                {
                    var url = repoUrl.GetString() ?? "";
                    var parts = url.Split('/');
                    if (parts.Length >= 2)
                    {
                        pr.Repository = $"{parts[^2]}/{parts[^1]}";
                    }
                }

                if (item.TryGetProperty("created_at", out var created) && DateTime.TryParse(created.GetString(), out var createdDt))
                {
                    pr.CreatedAt = createdDt;
                }

                if (item.TryGetProperty("updated_at", out var updated) && DateTime.TryParse(updated.GetString(), out var updatedDt))
                {
                    pr.UpdatedAt = updatedDt;
                }

                // Parse labels
                if (item.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in labels.EnumerateArray())
                    {
                        var name = label.TryGetProperty("name", out var labelName) ? labelName.GetString() ?? "" : "";
                        var color = label.TryGetProperty("color", out var labelColor) ? labelColor.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            pr.Labels.Add(new GitHubLabel { Name = name, Color = color });
                        }
                    }
                }

                results.Add(pr);
            }
        }
        catch
        {
            // Return what we have
        }

        return results;
    }

    private static List<GitHubPrFile> ParsePullRequestFiles(string json)
    {
        var results = new List<GitHubPrFile>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in files.EnumerateArray())
            {
                var file = new GitHubPrFile
                {
                    Path = item.TryGetProperty("path", out var path) ? path.GetString() ?? "" : "",
                    Additions = item.TryGetProperty("additions", out var adds) ? adds.GetInt32() : 0,
                    Deletions = item.TryGetProperty("deletions", out var dels) ? dels.GetInt32() : 0
                };

                // Determine status from additions/deletions
                if (file.Additions > 0 && file.Deletions == 0)
                    file.Status = "added";
                else if (file.Additions == 0 && file.Deletions > 0)
                    file.Status = "removed";
                else
                    file.Status = "modified";

                results.Add(file);
            }
        }
        catch
        {
            // Return what we have
        }

        return results;
    }

    private static List<GitHubRepository> ParseRepositories(string json)
    {
        var results = new List<GitHubRepository>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in root.EnumerateArray())
            {
                var repo = new GitHubRepository
                {
                    FullName = item.TryGetProperty("nameWithOwner", out var name) ? name.GetString() ?? "" : "",
                    Description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    IsPrivate = item.TryGetProperty("isPrivate", out var priv) && priv.GetBoolean()
                };

                if (item.TryGetProperty("pushedAt", out var pushed) && DateTime.TryParse(pushed.GetString(), out var pushedDt))
                {
                    repo.LastPushedAt = pushedDt;
                }

                results.Add(repo);
            }
        }
        catch
        {
            // Return what we have
        }

        return results;
    }

    private static string? ExtractFileDiff(string fullDiff, string filePath)
    {
        // Find the section for the specific file
        var lines = fullDiff.Split('\n');
        var capturing = false;
        var result = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git"))
            {
                if (capturing)
                    break; // We were capturing, and hit next file

                // Check if this is our file
                if (line.Contains($"a/{filePath}") || line.Contains($"b/{filePath}"))
                {
                    capturing = true;
                }
            }

            if (capturing)
            {
                result.Add(line);
            }
        }

        return result.Count > 0 ? string.Join('\n', result) : null;
    }

    #endregion
}
