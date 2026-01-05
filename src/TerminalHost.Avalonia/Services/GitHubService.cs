using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Implementation of IGitHubService using the gh CLI.
/// </summary>
internal sealed class GitHubService : IGitHubService
{
    private readonly IConfigurationService _configurationService;
    private bool? _ghAvailable;
    private string? _currentUser;
    private DateTime _lastUserFetch = DateTime.MinValue;
    private readonly TimeSpan _userCacheDuration = TimeSpan.FromMinutes(30);

    public GitHubService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public bool IsGitHubCliAvailable()
    {
        if (_ghAvailable.HasValue)
            return _ghAvailable.Value;

        try
        {
            // macOS: use /bin/sh to run gh command
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c \"gh auth token\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set up PATH with custom paths
            SetupEnvironmentWithCustomPaths(psi);

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
            var (exitCode, output, _) = await RunGhCommandAsync(
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
            var (exitCode, output, _) = await RunGhCommandAsync(
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
            var (exitCode, output, _) = await RunGhCommandAsync(
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
            var (exitCode, output, _) = await RunGhCommandAsync(
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
            var (exitCode, output, _) = await RunGhCommandAsync(
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
            var (exitCode, output, _) = await RunGhCommandAsync(
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

    public async Task<(bool success, string? error)> CheckoutPullRequestAsync(string workingDirectory, int prNumber)
    {
        if (!IsGitHubCliAvailable())
            return (false, "GitHub CLI not available");

        try
        {
            // First, fetch the latest from the PR to ensure we have the latest refs
            await RunGhCommandAsync(workingDirectory, $"pr checkout {prNumber} --detach", timeoutSeconds: 60);

            // Get the PR branch name
            var (branchExitCode, branchOutput, _) = await RunGhCommandAsync(
                workingDirectory,
                $"pr view {prNumber} --json headRefName --jq .headRefName",
                timeoutSeconds: 30);

            if (branchExitCode != 0 || string.IsNullOrWhiteSpace(branchOutput))
            {
                return (false, "Failed to get PR branch name");
            }

            var branchName = branchOutput.Trim();

            // Fetch the latest from origin for this branch
            await RunGitCommandAsync(workingDirectory, "fetch origin", timeoutSeconds: 60);

            // Check if local branch exists
            var (localBranchExitCode, _, _) = await RunGitCommandAsync(
                workingDirectory,
                $"rev-parse --verify {branchName}",
                timeoutSeconds: 10);

            if (localBranchExitCode == 0)
            {
                // Local branch exists - delete it so we can recreate fresh from origin
                // First switch to a safe branch (detached HEAD or default branch)
                await RunGitCommandAsync(workingDirectory, "checkout --detach HEAD", timeoutSeconds: 10);
                await RunGitCommandAsync(workingDirectory, $"branch -D {branchName}", timeoutSeconds: 10);
            }

            // Now checkout the PR fresh - this will create the branch tracking the remote
            var (exitCode, _, error) = await RunGhCommandAsync(workingDirectory, $"pr checkout {prNumber}", timeoutSeconds: 60);

            if (exitCode == 0)
                return (true, null);

            // Return a meaningful error message
            var errorMsg = !string.IsNullOrWhiteSpace(error) ? error.Trim() : $"gh pr checkout failed with exit code {exitCode}";
            return (false, errorMsg);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<(int exitCode, string output, string error)> RunGitCommandAsync(string? workingDirectory, string arguments, int timeoutSeconds = 120)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
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
                return (-1, "", "Failed to start git process");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync(cts.Token);

                return (process.ExitCode, await outputTask, await errorTask);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, "", $"Operation timed out after {timeoutSeconds} seconds");
            }
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
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

            var (exitCode, _, _) = await RunGhCommandAsync(workingDirectory, args);
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
            var (exitCode, _, _) = await RunGhCommandAsync(
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
            var (exitCode, _, _) = await RunGhCommandAsync(
                workingDirectory,
                $"pr comment {prNumber} --body \"{EscapeArgument(comment)}\"");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> MergePullRequestAsync(string workingDirectory, int prNumber, string method = "squash", string? commitSubject = null)
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

            var args = $"pr merge {prNumber} {methodArg} --delete-branch";

            // Add commit subject if provided (primarily used for squash merges)
            if (!string.IsNullOrEmpty(commitSubject))
            {
                args += $" --subject \"{EscapeArgument(commitSubject)}\"";
            }

            var (exitCode, _, _) = await RunGhCommandAsync(workingDirectory, args);
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
            var (exitCode, output, _) = await RunGhCommandAsync(
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

            var (exitCode, _, _) = await RunGhCommandAsync(
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
            var (exitCode, output, _) = await RunGhCommandAsync(null, "api user --jq .login");

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
            var (exitCode, output, _) = await RunGhCommandAsync(
                workingDirectory,
                "pr view --json number,title,body,author,state,baseRefName,headRefName,additions,deletions,changedFiles,createdAt,updatedAt,reviewDecision");

            if (exitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            // Get the repo name from the working directory
            var (repoExitCode, repoOutput, _) = await RunGhCommandAsync(workingDirectory, "repo view --json nameWithOwner --jq .nameWithOwner");
            var repo = repoExitCode == 0 ? repoOutput?.Trim() ?? "" : "";

            return ParsePullRequestDetails(output, repo);
        }
        catch
        {
            return null;
        }
    }

    public async Task<PrComments?> GetPullRequestCommentsAsync(string repo, int prNumber)
    {
        if (!IsGitHubCliAvailable())
            return null;

        try
        {
            var parts = repo.Split('/');
            if (parts.Length != 2)
                return null;

            var owner = parts[0];
            var repoName = parts[1];

            // Use GraphQL to get review threads with comments
            var query = @"query($owner: String!, $repo: String!, $pr: Int!) {
  repository(owner: $owner, name: $repo) {
    pullRequest(number: $pr) {
      comments(first: 100) {
        nodes {
          author { login }
          body
          createdAt
        }
      }
      reviewThreads(first: 100) {
        nodes {
          id
          isResolved
          isOutdated
          path
          line
          diffSide
          comments(first: 50) {
            nodes {
              author { login }
              body
              createdAt
            }
          }
        }
      }
    }
  }
}";

            var output = await RunGhGraphQLQueryAsync(query, owner, repoName, prNumber);

            if (string.IsNullOrEmpty(output))
                return null;

            return ParsePrComments(output);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> RunGhGraphQLQueryAsync(string query, string owner, string repo, int pr)
    {
        try
        {
            // macOS: use /bin/sh to run gh command
            // Write query to temp file and use @file syntax
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, query);

                var escapedTempFile = tempFile.Replace("\"", "\\\"");
                var args = $"-c \"gh api graphql -F owner={owner} -F repo={repo} -F pr={pr} -F query=@\\\"{escapedTempFile}\\\"\"";
                System.Diagnostics.Debug.WriteLine($"GraphQL: Running /bin/sh {args}");

                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                // Set up PATH with custom paths
                SetupEnvironmentWithCustomPaths(psi);

                using var process = Process.Start(psi);
                if (process == null)
                {
                    System.Diagnostics.Debug.WriteLine("GraphQL: Failed to start process");
                    return null;
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                System.Diagnostics.Debug.WriteLine($"GraphQL: Exit code {process.ExitCode}, Output length: {output?.Length ?? 0}, Error: {error}");

                return process.ExitCode == 0 ? output : null;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GraphQL: Exception {ex.Message}");
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

    private async Task<(int exitCode, string output, string error)> RunGhCommandAsync(string? workingDirectory, string arguments, int timeoutSeconds = 120)
    {
        try
        {
            // macOS: use /bin/sh to run gh command
            var escapedArguments = arguments.Replace("\"", "\\\"");
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"gh {escapedArguments}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            // Set up PATH with custom paths
            SetupEnvironmentWithCustomPaths(psi);

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "", "Failed to start process");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                // Read both streams concurrently to avoid deadlocks
                var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync(cts.Token);

                return (process.ExitCode, await outputTask, await errorTask);
            }
            catch (OperationCanceledException)
            {
                // Timeout - kill the process
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, "", $"Operation timed out after {timeoutSeconds} seconds");
            }
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    /// <summary>
    /// Sets up the environment with custom paths from settings.
    /// This ensures gh CLI can be found even when installed in non-standard locations.
    /// </summary>
    private void SetupEnvironmentWithCustomPaths(ProcessStartInfo psi)
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = _configurationService.Load();
        var customPaths = config.Settings.CustomPaths;

        // Build a comprehensive PATH
        var additionalPaths = new List<string>();

        // Add user-configured custom paths first (highest priority)
        if (customPaths.Count > 0)
        {
            additionalPaths.AddRange(customPaths.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        // Add default paths that are commonly used
        additionalPaths.AddRange(new[]
        {
            $"{homeDir}/.local/bin",           // User-installed tools
            "/opt/homebrew/bin",               // Homebrew on Apple Silicon
            "/opt/homebrew/sbin",
            "/usr/local/bin",                  // Homebrew on Intel, user tools
            "/usr/local/sbin",
            "/usr/bin",
            "/bin",
            "/usr/sbin",
            "/sbin",
            $"{homeDir}/.cargo/bin",           // Rust tools
            $"{homeDir}/.npm-global/bin",      // npm global packages
            "/opt/local/bin",                  // MacPorts
        });

        // Get existing PATH
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathParts = currentPath.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Add paths that don't already exist, in reverse order so first items end up first
        for (int i = additionalPaths.Count - 1; i >= 0; i--)
        {
            var path = additionalPaths[i];
            if (!pathParts.Contains(path) && Directory.Exists(path))
            {
                pathParts.Insert(0, path); // Prepend to give priority
            }
        }

        psi.Environment["PATH"] = string.Join(":", pathParts);
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

    private static PrComments? ParsePrComments(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data))
                return null;

            if (!data.TryGetProperty("repository", out var repository))
                return null;

            if (!repository.TryGetProperty("pullRequest", out var pr))
                return null;

            var result = new PrComments();

            // Parse general comments
            if (pr.TryGetProperty("comments", out var comments) &&
                comments.TryGetProperty("nodes", out var commentNodes) &&
                commentNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in commentNodes.EnumerateArray())
                {
                    var comment = ParseCommentItem(node);
                    if (comment != null)
                    {
                        result.GeneralComments.Add(comment);
                    }
                }
            }

            // Parse review threads
            if (pr.TryGetProperty("reviewThreads", out var threads) &&
                threads.TryGetProperty("nodes", out var threadNodes) &&
                threadNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in threadNodes.EnumerateArray())
                {
                    var thread = new PrReviewThread
                    {
                        Id = node.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        FilePath = node.TryGetProperty("path", out var path) ? path.GetString() ?? "" : "",
                        IsResolved = node.TryGetProperty("isResolved", out var resolved) && resolved.GetBoolean(),
                        IsOutdated = node.TryGetProperty("isOutdated", out var outdated) && outdated.GetBoolean(),
                        DiffSide = node.TryGetProperty("diffSide", out var side) ? side.GetString() : null
                    };

                    if (node.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number)
                    {
                        thread.Line = line.GetInt32();
                    }

                    // Parse thread comments
                    if (node.TryGetProperty("comments", out var threadComments) &&
                        threadComments.TryGetProperty("nodes", out var threadCommentNodes) &&
                        threadCommentNodes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var commentNode in threadCommentNodes.EnumerateArray())
                        {
                            var comment = ParseCommentItem(commentNode);
                            if (comment != null)
                            {
                                thread.Comments.Add(comment);
                            }
                        }
                    }

                    if (thread.Comments.Count > 0)
                    {
                        result.ReviewThreads.Add(thread);
                    }
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static PrCommentItem? ParseCommentItem(JsonElement node)
    {
        try
        {
            var author = "";
            if (node.TryGetProperty("author", out var authorObj) && authorObj.ValueKind == JsonValueKind.Object)
            {
                author = authorObj.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "";
            }

            var body = node.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            DateTime createdAt = DateTime.MinValue;
            if (node.TryGetProperty("createdAt", out var created) &&
                DateTime.TryParse(created.GetString(), out var parsed))
            {
                createdAt = parsed;
            }

            return new PrCommentItem
            {
                Author = author,
                Body = body,
                CreatedAt = createdAt,
                IsBot = author.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)
            };
        }
        catch
        {
            return null;
        }
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
