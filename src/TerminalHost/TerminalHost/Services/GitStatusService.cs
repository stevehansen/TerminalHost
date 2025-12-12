using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public class GitOperationResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}

public class GitStatusService
{
    public async Task<GitStatus> GetGitStatusAsync(string workingDirectory)
    {
        var status = new GitStatus();

        if (!Directory.Exists(workingDirectory))
            return status;

        // Check if it's a git repository
        var gitDir = await RunGitCommandAsync(workingDirectory, "rev-parse --git-dir");
        if (gitDir == null)
            return status;

        status.IsGitRepository = true;

        // Get branch name
        var branch = await RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD");
        status.BranchName = branch?.Trim() ?? "";

        // Handle detached HEAD
        if (status.BranchName == "HEAD")
        {
            var shortSha = await RunGitCommandAsync(workingDirectory, "rev-parse --short HEAD");
            status.BranchName = shortSha?.Trim() ?? "HEAD";
        }

        // Check dirty status
        var porcelain = await RunGitCommandAsync(workingDirectory, "status --porcelain");
        status.IsDirty = !string.IsNullOrWhiteSpace(porcelain);

        // Get ahead/behind counts (may fail if no upstream)
        var ahead = await RunGitCommandAsync(workingDirectory, "rev-list --count @{u}..HEAD");
        if (ahead != null && int.TryParse(ahead.Trim(), out var aheadCount))
            status.AheadCount = aheadCount;

        var behind = await RunGitCommandAsync(workingDirectory, "rev-list --count HEAD..@{u}");
        if (behind != null && int.TryParse(behind.Trim(), out var behindCount))
            status.BehindCount = behindCount;

        return status;
    }

    public async Task<List<GitFileStatus>> GetModifiedFilesAsync(string workingDirectory)
    {
        var files = new List<GitFileStatus>();

        if (!Directory.Exists(workingDirectory))
            return files;

        // Get status in porcelain v1 format: XY PATH or XY ORIG -> PATH for renames
        var output = await RunGitCommandAsync(workingDirectory, "status --porcelain");
        if (string.IsNullOrEmpty(output))
            return files;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Length < 3) continue;

            var indexStatus = line[0];  // Staged status
            var workTreeStatus = line[1]; // Unstaged status
            var path = line.Substring(3);

            // Handle renames/copies (format: "R  old -> new" or "C  old -> new")
            string? originalPath = null;
            if (path.Contains(" -> "))
            {
                var parts = path.Split(" -> ");
                originalPath = parts[0];
                path = parts[1];
            }

            // Determine status type (prefer unstaged status if present, otherwise staged)
            var statusChar = workTreeStatus != ' ' ? workTreeStatus : indexStatus;
            var isStaged = indexStatus != ' ' && indexStatus != '?';

            var fileStatus = new GitFileStatus
            {
                FilePath = path,
                Status = ParseStatusChar(statusChar),
                IsStaged = isStaged,
                OriginalPath = originalPath
            };

            files.Add(fileStatus);
        }

        return files;
    }

    private static GitFileStatusType ParseStatusChar(char status) => status switch
    {
        'M' => GitFileStatusType.Modified,
        'A' => GitFileStatusType.Added,
        'D' => GitFileStatusType.Deleted,
        'R' => GitFileStatusType.Renamed,
        'C' => GitFileStatusType.Copied,
        '?' => GitFileStatusType.Untracked,
        '!' => GitFileStatusType.Ignored,
        'U' => GitFileStatusType.Conflicted,
        'T' => GitFileStatusType.TypeChanged,
        _ => GitFileStatusType.Modified
    };

    public async Task<string?> GetFileDiffAsync(string workingDirectory, string filePath, bool staged = false)
    {
        if (!Directory.Exists(workingDirectory))
            return null;

        // For staged changes: git diff --cached -- file
        // For unstaged changes: git diff -- file
        // For untracked: we'll show the whole file content as "new"
        var args = staged
            ? $"diff --cached -- \"{filePath}\""
            : $"diff -- \"{filePath}\"";

        var diff = await RunGitCommandAsync(workingDirectory, args);

        // If no diff (untracked file), show file contents as addition
        if (string.IsNullOrEmpty(diff))
        {
            var fullPath = Path.Combine(workingDirectory, filePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(fullPath);
                    // Format as a diff with all lines as additions
                    var lines = content.Split('\n');
                    var diffLines = new List<string>
                    {
                        $"diff --git a/{filePath} b/{filePath}",
                        "new file mode 100644",
                        "--- /dev/null",
                        $"+++ b/{filePath}",
                        $"@@ -0,0 +1,{lines.Length} @@"
                    };
                    diffLines.AddRange(lines.Select(l => "+" + l));
                    return string.Join("\n", diffLines);
                }
                catch
                {
                    return null;
                }
            }
        }

        return diff;
    }

    public async Task<string?> GetFileContentAtHeadAsync(string workingDirectory, string filePath)
    {
        if (!Directory.Exists(workingDirectory))
            return null;

        // Get file content from HEAD
        return await RunGitCommandAsync(workingDirectory, $"show HEAD:\"{filePath}\"");
    }

    private static async Task<string?> RunGitCommandAsync(string workingDirectory, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GitOperationResult> RunGitOperationAsync(string workingDirectory, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return new GitOperationResult
            {
                Success = process.ExitCode == 0,
                Output = await outputTask,
                Error = await errorTask
            };
        }
        catch (Exception ex)
        {
            return new GitOperationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    #region Branch Operations

    public async Task<List<GitBranch>> GetBranchesAsync(string workingDirectory)
    {
        var branches = new List<GitBranch>();

        if (!Directory.Exists(workingDirectory))
            return branches;

        // Get current branch name
        var currentBranch = await RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD");
        currentBranch = currentBranch?.Trim();

        // Get all branches with tracking info
        // Format: refname|HEAD indicator|upstream|track info
        var output = await RunGitCommandAsync(workingDirectory,
            "branch -a --format=\"%(refname:short)|%(HEAD)|%(upstream:short)|%(upstream:track)\"");

        if (string.IsNullOrEmpty(output))
            return branches;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;

            var name = parts[0].Trim();
            var isCurrent = parts[1].Trim() == "*";
            var upstream = parts.Length > 2 ? parts[2].Trim() : null;
            var trackInfo = parts.Length > 3 ? parts[3].Trim() : null;

            // Skip HEAD reference
            if (name == "HEAD" || name.Contains("HEAD detached"))
                continue;

            var isRemote = name.StartsWith("remotes/") || name.Contains("/");
            string? remoteName = null;
            var shortName = name;

            // Handle remote branches (e.g., "remotes/origin/main" or "origin/main")
            if (name.StartsWith("remotes/"))
            {
                isRemote = true;
                name = name.Substring("remotes/".Length);
                var slashIndex = name.IndexOf('/');
                if (slashIndex > 0)
                {
                    remoteName = name.Substring(0, slashIndex);
                    shortName = name.Substring(slashIndex + 1);
                }
            }
            else if (isRemote)
            {
                var slashIndex = name.IndexOf('/');
                if (slashIndex > 0)
                {
                    remoteName = name.Substring(0, slashIndex);
                    shortName = name.Substring(slashIndex + 1);
                }
            }
            else
            {
                shortName = name;
                isRemote = false;
            }

            // Parse ahead/behind from track info like "[ahead 2, behind 1]" or "[ahead 2]"
            int? ahead = null, behind = null;
            if (!string.IsNullOrEmpty(trackInfo))
            {
                var aheadMatch = Regex.Match(trackInfo, @"ahead (\d+)");
                var behindMatch = Regex.Match(trackInfo, @"behind (\d+)");
                if (aheadMatch.Success) ahead = int.Parse(aheadMatch.Groups[1].Value);
                if (behindMatch.Success) behind = int.Parse(behindMatch.Groups[1].Value);
            }

            branches.Add(new GitBranch
            {
                Name = name,
                ShortName = shortName,
                IsCurrent = isCurrent || name == currentBranch,
                IsRemote = isRemote,
                RemoteName = remoteName,
                TrackingBranch = string.IsNullOrEmpty(upstream) ? null : upstream,
                AheadCount = ahead,
                BehindCount = behind
            });
        }

        // Remove duplicate remote branches that match local branches
        var localBranches = branches.Where(b => !b.IsRemote).Select(b => b.ShortName).ToHashSet();
        branches = branches
            .Where(b => !b.IsRemote || !localBranches.Contains(b.ShortName) || b.RemoteName != "origin")
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .ToList();

        return branches;
    }

    public async Task<GitOperationResult> CheckoutBranchAsync(string workingDirectory, string branchName)
    {
        if (!Directory.Exists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // For remote branches, create a local tracking branch
        if (branchName.Contains("/"))
        {
            var slashIndex = branchName.IndexOf('/');
            var remoteName = branchName.Substring(0, slashIndex);
            var remoteBranch = branchName.Substring(slashIndex + 1);

            // Check if local branch already exists
            var localBranches = await RunGitCommandAsync(workingDirectory, "branch --list");
            if (localBranches != null && localBranches.Contains(remoteBranch))
            {
                // Switch to existing local branch
                return await RunGitOperationAsync(workingDirectory, $"checkout \"{remoteBranch}\"");
            }

            // Create tracking branch
            return await RunGitOperationAsync(workingDirectory, $"checkout -b \"{remoteBranch}\" \"{branchName}\"");
        }

        return await RunGitOperationAsync(workingDirectory, $"checkout \"{branchName}\"");
    }

    public async Task<GitOperationResult> CreateBranchAsync(string workingDirectory, string branchName)
    {
        if (!Directory.Exists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(branchName))
            return new GitOperationResult { Success = false, Error = "Branch name cannot be empty" };

        // Validate branch name (basic validation)
        if (branchName.Contains(" ") || branchName.Contains("..") || branchName.StartsWith("-"))
            return new GitOperationResult { Success = false, Error = "Invalid branch name" };

        return await RunGitOperationAsync(workingDirectory, $"checkout -b \"{branchName}\"");
    }

    public async Task<GitOperationResult> DeleteBranchAsync(string workingDirectory, string branchName, bool force = false)
    {
        if (!Directory.Exists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var flag = force ? "-D" : "-d";
        return await RunGitOperationAsync(workingDirectory, $"branch {flag} \"{branchName}\"");
    }

    public async Task<GitOperationResult> DeleteRemoteBranchAsync(string workingDirectory, string remoteName, string branchName)
    {
        if (!Directory.Exists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await RunGitOperationAsync(workingDirectory, $"push \"{remoteName}\" --delete \"{branchName}\"");
    }

    public async Task<GitOperationResult> FetchAllAsync(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await RunGitOperationAsync(workingDirectory, "fetch --all --prune");
    }

    public async Task<GitOperationResult> PullAsync(string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await RunGitOperationAsync(workingDirectory, "pull");
    }

    #endregion
}
