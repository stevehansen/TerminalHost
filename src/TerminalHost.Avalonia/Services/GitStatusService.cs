using System.IO;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;

namespace TerminalHost.Services;

internal sealed class GitStatusService : IGitStatusService
{
    private readonly IGitProcessRunner _gitRunner;
    private readonly IFileSystem _fileSystem;

    public GitStatusService(IGitProcessRunner gitRunner, IFileSystem fileSystem)
    {
        _gitRunner = gitRunner;
        _fileSystem = fileSystem;
    }

    public async Task<GitStatus> GetGitStatusAsync(string workingDirectory)
    {
        var status = new GitStatus();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return status;

        // Check if it's a git repository
        var gitDir = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse --git-dir");
        if (gitDir == null)
            return status;

        status.IsGitRepository = true;

        // Get branch name
        var branch = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD");
        status.BranchName = branch?.Trim() ?? "";

        // Handle detached HEAD
        if (status.BranchName == "HEAD")
        {
            var shortSha = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse --short HEAD");
            status.BranchName = shortSha?.Trim() ?? "HEAD";
        }

        // Check dirty status
        var porcelain = await _gitRunner.RunGitCommandAsync(workingDirectory, "status --porcelain");
        status.IsDirty = !string.IsNullOrWhiteSpace(porcelain);

        // Get ahead/behind counts (may fail if no upstream)
        var ahead = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-list --count @{u}..HEAD");
        if (ahead != null && int.TryParse(ahead.Trim(), out var aheadCount))
            status.AheadCount = aheadCount;

        var behind = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-list --count HEAD..@{u}");
        if (behind != null && int.TryParse(behind.Trim(), out var behindCount))
            status.BehindCount = behindCount;

        return status;
    }

    public async Task<List<GitFileStatus>> GetModifiedFilesAsync(string workingDirectory)
    {
        var files = new List<GitFileStatus>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return files;

        // Get status in porcelain v1 format: XY PATH or XY ORIG -> PATH for renames
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, "status --porcelain");
        if (string.IsNullOrEmpty(output))
            return files;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Length < 3) continue;

            var indexStatus = line[0];  // Staged status
            var workTreeStatus = line[1]; // Unstaged status
            var rawPath = line.Substring(3);

            // Handle renames/copies (format: "R  old -> new" or "C  old -> new")
            // These may be entirely quoted: "old -> new" or partially: "old" -> "new"
            string? originalPath = null;
            string path = rawPath;

            if (rawPath.Contains(" -> "))
            {
                // Try to split on " -> " while handling quoted paths
                var separatorIndex = rawPath.IndexOf(" -> ");
                if (separatorIndex > 0)
                {
                    var oldPart = rawPath.Substring(0, separatorIndex);
                    var newPart = rawPath.Substring(separatorIndex + 4); // 4 = " -> ".Length

                    originalPath = UnquoteGitPath(oldPart.Trim());
                    path = UnquoteGitPath(newPart.Trim());
                }
            }
            else
            {
                path = UnquoteGitPath(rawPath);
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

    public async Task<(List<GitFileStatus> Staged, List<GitFileStatus> Unstaged)> GetStagedAndUnstagedFilesAsync(string workingDirectory)
    {
        var staged = new List<GitFileStatus>();
        var unstaged = new List<GitFileStatus>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (staged, unstaged);

        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, "status --porcelain");
        if (string.IsNullOrEmpty(output))
            return (staged, unstaged);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Length < 3) continue;

            var indexStatus = line[0];  // Staged status (X)
            var workTreeStatus = line[1]; // Unstaged status (Y)
            var rawPath = line.Substring(3);

            string? originalPath = null;
            string path = rawPath;

            if (rawPath.Contains(" -> "))
            {
                var separatorIndex = rawPath.IndexOf(" -> ");
                if (separatorIndex > 0)
                {
                    var oldPart = rawPath.Substring(0, separatorIndex);
                    var newPart = rawPath.Substring(separatorIndex + 4);
                    originalPath = UnquoteGitPath(oldPart.Trim());
                    path = UnquoteGitPath(newPart.Trim());
                }
            }
            else
            {
                path = UnquoteGitPath(rawPath);
            }

            // If file has staged changes (X is not ' ' and not '?')
            if (indexStatus != ' ' && indexStatus != '?')
            {
                staged.Add(new GitFileStatus
                {
                    FilePath = path,
                    Status = ParseStatusChar(indexStatus),
                    IsStaged = true,
                    OriginalPath = originalPath
                });
            }

            // If file has unstaged changes (Y is not ' ') OR is untracked (??)
            if (workTreeStatus != ' ' || indexStatus == '?')
            {
                var statusChar = indexStatus == '?' ? '?' : workTreeStatus;
                unstaged.Add(new GitFileStatus
                {
                    FilePath = path,
                    Status = ParseStatusChar(statusChar),
                    IsStaged = false,
                    OriginalPath = originalPath
                });
            }
        }

        return (staged, unstaged);
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

    /// <summary>
    /// Unquotes and unescapes a path from git's porcelain output.
    /// Git wraps paths with special characters (spaces, quotes, etc.) in double quotes
    /// and escapes backslashes and quotes with backslashes.
    ///
    /// Examples:
    ///   "My File.txt" -> My File.txt
    ///   "File with \"quotes\".txt" -> File with "quotes".txt
    ///   Regular.txt -> Regular.txt (no quotes, returned as-is)
    /// </summary>
    private static string UnquoteGitPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Check if path is quoted
        if (!path.StartsWith("\"") || !path.EndsWith("\""))
            return path;

        // Remove outer quotes
        var unquoted = path.Substring(1, path.Length - 2);

        // Unescape escaped characters (git uses backslash for escaping)
        // Replace \" with " and \\ with \
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < unquoted.Length; i++)
        {
            if (unquoted[i] == '\\' && i + 1 < unquoted.Length)
            {
                var nextChar = unquoted[i + 1];
                if (nextChar == '"' || nextChar == '\\')
                {
                    sb.Append(nextChar);
                    i++; // Skip the escaped character
                    continue;
                }
            }
            sb.Append(unquoted[i]);
        }

        return sb.ToString();
    }

    public async Task<string?> GetFileDiffAsync(string workingDirectory, string filePath, bool staged = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // For staged changes: git diff --cached -- file
        // For unstaged changes: git diff -- file
        // For untracked: we'll show the whole file content as "new"
        var args = staged
            ? $"diff --cached -- \"{filePath}\""
            : $"diff -- \"{filePath}\"" ;

        var diff = await _gitRunner.RunGitCommandAsync(workingDirectory, args);

        // If no diff (untracked file), show file contents as addition
        if (string.IsNullOrEmpty(diff))
        {
            var fullPath = Path.Combine(workingDirectory, filePath);
            if (_fileSystem.FileExists(fullPath))
            {
                try
                {
                    var content = await _fileSystem.ReadAllTextAsync(fullPath);
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
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // Get file content from HEAD
        return await _gitRunner.RunGitCommandAsync(workingDirectory, $"show HEAD:\"{filePath}\" ");
    }

    #region Branch Operations

    public async Task<List<GitBranch>> GetBranchesAsync(string workingDirectory)
    {
        var branches = new List<GitBranch>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return branches;

        // Get list of known remotes (e.g., "origin", "upstream")
        var remotesOutput = await _gitRunner.RunGitCommandAsync(workingDirectory, "remote");
        var knownRemotes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(remotesOutput))
        {
            foreach (var remote in remotesOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                knownRemotes.Add(remote.Trim());
            }
        }

        // Get current branch name
        var currentBranch = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD");
        currentBranch = currentBranch?.Trim();

        // Get all branches with tracking info
        // Format: refname|HEAD indicator|upstream|track info
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            "branch -a --format=\"%(refname:short)|%(HEAD)|%(upstream:short)|%(upstream:track)\" ");

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

            var isRemote = false;
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
            else if (name.Contains('/'))
            {
                // Check if it starts with a known remote name
                var slashIndex = name.IndexOf('/');
                if (slashIndex > 0)
                {
                    var possibleRemote = name.Substring(0, slashIndex);
                    if (knownRemotes.Contains(possibleRemote))
                    {
                        // This is a remote branch (e.g., "origin/main")
                        isRemote = true;
                        remoteName = possibleRemote;
                        shortName = name.Substring(slashIndex + 1);
                    }
                    // Otherwise it's a local branch with / in the name (e.g., "issues/123")
                    // Keep isRemote = false, shortName = name
                }
            }

            // Skip entries that are just the remote name (e.g., "origin" alone)
            if (knownRemotes.Contains(name) && !name.Contains('/'))
                continue;

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
        branches = [.. branches
            .Where(b => !b.IsRemote || !localBranches.Contains(b.ShortName) || b.RemoteName != "origin")
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)];

        return branches;
    }

    public async Task<GitOperationResult> CheckoutBranchAsync(string workingDirectory, string branchName, bool isRemote = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // For remote branches, create a local tracking branch
        if (isRemote && branchName.Contains('/'))
        {
            var slashIndex = branchName.IndexOf('/');
            var remoteName = branchName.Substring(0, slashIndex);
            var remoteBranch = branchName.Substring(slashIndex + 1);

            // Check if local branch already exists
            var localBranches = await _gitRunner.RunGitCommandAsync(workingDirectory, "branch --list");
            if (localBranches != null)
            {
                // Parse branch list properly - each line may have leading spaces and * for current branch
                var branchLines = localBranches.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in branchLines)
                {
                    var trimmed = line.Trim().TrimStart('*').Trim();
                    if (trimmed == remoteBranch)
                    {
                        // Switch to existing local branch
                        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"checkout \"{remoteBranch}\"");
                    }
                }
            }

            // Create tracking branch
            return await _gitRunner.RunGitOperationAsync(workingDirectory, $"checkout -b \"{remoteBranch}\" \"{branchName}\"");
        }

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"checkout \"{branchName}\"");
    }

    public async Task<GitOperationResult> CreateBranchAsync(string workingDirectory, string branchName)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(branchName))
            return new GitOperationResult { Success = false, Error = "Branch name cannot be empty" };

        // Validate branch name (basic validation)
        if (branchName.Contains(" ") || branchName.Contains("..") || branchName.StartsWith("-"))
            return new GitOperationResult { Success = false, Error = "Invalid branch name" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"checkout -b \"{branchName}\" ");
    }

    public async Task<GitOperationResult> DeleteBranchAsync(string workingDirectory, string branchName, bool force = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var flag = force ? "-D" : "-d";
        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"branch {flag} \"{branchName}\" ");
    }

    public async Task<GitOperationResult> DeleteRemoteBranchAsync(string workingDirectory, string remoteName, string branchName)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"push \"{remoteName}\" --delete \"{branchName}\" ");
    }

    public async Task<GitOperationResult> FetchAllAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "fetch --all --prune");
    }

    public async Task<GitOperationResult> PullAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "pull");
    }

    public async Task<GitOperationResult> PullRebaseAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "pull --rebase");
    }

    public async Task<GitOperationResult> PushAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "push");
    }

    #endregion

    #region Staging Operations (IGitStatusService Interface)

    async Task<GitOperationResult> IGitStatusService.StageFileAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"add -- \"{filePath}\"");
    }

    async Task<GitOperationResult> IGitStatusService.UnstageFileAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"reset HEAD -- \"{filePath}\"");
    }

    async Task<GitOperationResult> IGitStatusService.StageAllAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "add -A");
    }

    async Task<GitOperationResult> IGitStatusService.UnstageAllAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "reset HEAD");
    }

    async Task<GitOperationResult> IGitStatusService.DiscardChangesAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // For untracked files, we need to use git clean
        // For tracked files, use git checkout
        var status = await _gitRunner.RunGitCommandAsync(workingDirectory, $"status --porcelain -- \"{filePath}\"");
        if (string.IsNullOrEmpty(status))
            return new GitOperationResult { Success = false, Error = "Could not get file status" };

        // Check if file is untracked (starts with ??)
        if (status.TrimStart().StartsWith("??"))
        {
            // Delete untracked file
            var fullPath = Path.Combine(workingDirectory, filePath);
            if (_fileSystem.FileExists(fullPath))
            {
                try
                {
                    _fileSystem.DeleteFile(fullPath);
                    return new GitOperationResult { Success = true };
                }
                catch (Exception ex)
                {
                    return new GitOperationResult { Success = false, Error = ex.Message };
                }
            }
            return new GitOperationResult { Success = false, Error = "File not found" };
        }

        // For tracked files, use checkout to discard changes
        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"checkout -- \"{filePath}\"");
    }

    async Task<GitOperationResult> IGitStatusService.DiscardAllChangesAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // Reset staged changes and checkout all tracked files
        var resetResult = await _gitRunner.RunGitOperationAsync(workingDirectory, "reset --hard HEAD");
        if (!resetResult.Success)
            return resetResult;

        // Also clean untracked files
        return await _gitRunner.RunGitOperationAsync(workingDirectory, "clean -fd");
    }

    #endregion

    #region Commit Operations

    async Task<GitOperationResult> IGitStatusService.CreateCommitAsync(string workingDirectory, string message, bool amend)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(message) && !amend)
            return new GitOperationResult { Success = false, Error = "Commit message cannot be empty" };

        // Escape the message for shell
        var escapedMessage = message.Replace("\"", "\\\"");

        var args = amend
            ? $"commit --amend -m \"{escapedMessage}\""
            : $"commit -m \"{escapedMessage}\"";

        return await _gitRunner.RunGitOperationAsync(workingDirectory, args);
    }

    public async Task<(bool Success, string? Error)> CommitAsync(string workingDirectory, string message, bool amend = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        if (string.IsNullOrWhiteSpace(message) && !amend)
            return (false, "Commit message cannot be empty");

        // Escape the message for shell
        var escapedMessage = message.Replace("\"", "\\\"");

        var args = amend
            ? $"commit --amend -m \"{escapedMessage}\""
            : $"commit -m \"{escapedMessage}\"";

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, args);
        return (result.Success, result.Error);
    }

    #endregion

    #region Commit History Operations

    async Task<List<GitCommit>> IGitStatusService.GetCommitHistoryAsync(string workingDirectory, int count, string? author, string? filePath)
    {
        var commits = new List<GitCommit>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return commits;

        // Format: hash|author|email|timestamp|subject
        var authorFilter = string.IsNullOrEmpty(author) ? "" : $"--author=\"{author}\" ";
        var fileFilter = string.IsNullOrEmpty(filePath) ? "" : $"-- \"{filePath}\"";
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"log {authorFilter}-n {count} --format=\"%H|%an|%ae|%at|%s\" {fileFilter}");

        if (string.IsNullOrEmpty(output))
            return commits;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 5);
            if (parts.Length < 5) continue;

            if (!long.TryParse(parts[3], out var timestamp))
                continue;

            var commitDate = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            commits.Add(new GitCommit
            {
                Hash = parts[0],
                ShortHash = parts[0].Length >= 7 ? parts[0][..7] : parts[0],
                AuthorName = parts[1],
                AuthorEmail = parts[2],
                CommitDate = commitDate,
                RelativeDate = GetRelativeDate(commitDate),
                Subject = parts[4]
            });
        }

        return commits;
    }

    async Task<string?> IGitStatusService.GetCommitDiffAsync(string workingDirectory, string hash, string? filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        var fileArg = string.IsNullOrEmpty(filePath) ? "" : $"-- \"{filePath}\"";
        return await _gitRunner.RunGitCommandAsync(workingDirectory, $"show {hash} {fileArg}");
    }

    public async Task<List<GitCommit>> GetCommitHistoryAsync(string workingDirectory, int skip = 0, int take = 50, string? author = null)
    {
        var commits = new List<GitCommit>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return commits;

        // Format: hash|author|email|timestamp|subject
        var authorFilter = string.IsNullOrEmpty(author) ? "" : $"--author=\"{author}\" ";
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"log {authorFilter}--skip={skip} -n {take} --format=\"%H|%an|%ae|%at|%s\"");

        if (string.IsNullOrEmpty(output))
            return commits;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 5);
            if (parts.Length < 5) continue;

            if (!long.TryParse(parts[3], out var timestamp))
                continue;

            var commitDate = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            commits.Add(new GitCommit
            {
                Hash = parts[0],
                ShortHash = parts[0].Length >= 7 ? parts[0][..7] : parts[0],
                AuthorName = parts[1],
                AuthorEmail = parts[2],
                CommitDate = commitDate,
                RelativeDate = GetRelativeDate(commitDate),
                Subject = parts[4]
            });
        }

        return commits;
    }

    public async Task<GitCommitDetails?> GetCommitDetailsAsync(string workingDirectory, string commitHash)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // Get commit info with format: hash|author|email|timestamp|subject
        // Then body separately
        var infoOutput = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"show {commitHash} --format=\"%H|%an|%ae|%at|%s\" --no-patch");

        if (string.IsNullOrEmpty(infoOutput))
            return null;

        var infoLine = infoOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(infoLine))
            return null;

        var parts = infoLine.Split('|', 5);
        if (parts.Length < 5)
            return null;

        if (!long.TryParse(parts[3], out var timestamp))
            return null;

        // Get full body (everything after subject)
        var bodyOutput = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"show {commitHash} --format=\"%b\" --no-patch");

        // Get file stats: format is "additions deletions filename" with --numstat
        var statsOutput = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"show {commitHash} --numstat --format=\"\"");

        var files = new List<GitCommitFile>();
        if (!string.IsNullOrEmpty(statsOutput))
        {
            var statLines = statsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var statLine in statLines)
            {
                // Format: additions\tdeletions\tfilepath (or rename: oldpath => newpath)
                var statParts = statLine.Split('\t', 3);
                if (statParts.Length < 3) continue;

                var additions = statParts[0] == "-" ? 0 : int.TryParse(statParts[0], out var a) ? a : 0;
                var deletions = statParts[1] == "-" ? 0 : int.TryParse(statParts[1], out var d) ? d : 0;
                var filePath = statParts[2];

                string? originalPath = null;
                var status = GitFileStatusType.Modified;

                // Check for rename format: old => new or {old => new}
                if (filePath.Contains(" => "))
                {
                    status = GitFileStatusType.Renamed;
                    var renameMatch = System.Text.RegularExpressions.Regex.Match(filePath, @"(.*?)\{(.*?) => (.*?)\}(.*)");
                    if (renameMatch.Success)
                    {
                        // Format: prefix{old => new}suffix
                        var prefix = renameMatch.Groups[1].Value;
                        var oldPart = renameMatch.Groups[2].Value;
                        var newPart = renameMatch.Groups[3].Value;
                        var suffix = renameMatch.Groups[4].Value;
                        originalPath = prefix + oldPart + suffix;
                        filePath = prefix + newPart + suffix;
                    }
                    else
                    {
                        // Format: old => new
                        var renameParts = filePath.Split(" => ");
                        if (renameParts.Length == 2)
                        {
                            originalPath = renameParts[0];
                            filePath = renameParts[1];
                        }
                    }
                }
                else if (additions > 0 && deletions == 0)
                {
                    // Heuristic: new file likely has only additions
                    status = GitFileStatusType.Added;
                }
                else if (deletions > 0 && additions == 0)
                {
                    // Heuristic: deleted file likely has only deletions
                    status = GitFileStatusType.Deleted;
                }

                files.Add(new GitCommitFile
                {
                    FilePath = filePath,
                    Status = status,
                    Insertions = additions,
                    Deletions = deletions
                });
            }
        }

        // Refine file status by checking diff --name-status
        var nameStatusOutput = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"show {commitHash} --name-status --format=\"\"");

        if (!string.IsNullOrEmpty(nameStatusOutput))
        {
            var nameStatusMap = new Dictionary<string, GitFileStatusType>();
            var nameStatusLines = nameStatusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var nsLine in nameStatusLines)
            {
                if (nsLine.Length < 2) continue;
                var nsStatus = nsLine[0];
                var nsPath = nsLine.Substring(1).Trim();

                // Handle rename format: R100\told\tnew
                if (nsStatus == 'R' && nsLine.Contains('\t'))
                {
                    var tabParts = nsLine.Split('\t');
                    if (tabParts.Length >= 3)
                        nsPath = tabParts[2]; // new path
                }

                var parsedStatus = ParseStatusChar(nsStatus);
                nameStatusMap[nsPath] = parsedStatus;
            }

            // Update file statuses
            foreach (var file in files)
            {
                if (nameStatusMap.TryGetValue(file.FilePath, out var correctStatus))
                {
                    file.Status = correctStatus;
                }
            }
        }

        var commitDate = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        return new GitCommitDetails
        {
            Hash = parts[0],
            ShortHash = parts[0].Length >= 7 ? parts[0][..7] : parts[0],
            AuthorName = parts[1],
            AuthorEmail = parts[2],
            CommitDate = commitDate,
            RelativeDate = GetRelativeDate(commitDate),
            Subject = parts[4],
            Body = bodyOutput?.Trim() ?? "",
            Files = files,
            TotalInsertions = files.Sum(f => f.Insertions),
            TotalDeletions = files.Sum(f => f.Deletions)
        };
    }

    public async Task<string?> GetFileDiffInCommitAsync(string workingDirectory, string commitHash, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // Get diff for a specific file in a commit
        // Use commitHash^..commitHash to show changes introduced by that commit
        return await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"show {commitHash} -- \"{filePath}\"");
    }

    #endregion

    #region Stash Operations

    public async Task<List<GitStashEntry>> GetStashListAsync(string workingDirectory)
    {
        var stashes = new List<GitStashEntry>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return stashes;

        // Get stash list with format: %gd|%gs|%ci|%cr
        // %gd = reflog selector (stash@{0})
        // %gs = reflog subject (stash message)
        // %ci = commit date ISO format
        // %cr = relative commit date
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            "stash list --format=\"%gd|%gs|%ci|%cr\"");

        if (string.IsNullOrEmpty(output))
            return stashes;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 4);
            if (parts.Length < 4) continue;

            var reference = parts[0].Trim(); // stash@{0}
            var message = parts[1].Trim();   // On branch: message or WIP on branch: hash message
            var dateStr = parts[2].Trim();
            var relativeDate = parts[3].Trim();

            // Parse stash index from reference (e.g., "stash@{0}" -> 0)
            var index = 0;
            var indexMatch = Regex.Match(reference, @"stash@\{(\d+)\}");
            if (indexMatch.Success)
            {
                int.TryParse(indexMatch.Groups[1].Value, out index);
            }

            // Parse branch name from message (e.g., "On master: message" or "WIP on master: hash message")
            var branchName = "";
            var stashMessage = message;
            var onBranchMatch = Regex.Match(message, @"^(?:WIP )?[Oo]n ([^:]+): (.*)$");
            if (onBranchMatch.Success)
            {
                branchName = onBranchMatch.Groups[1].Value;
                stashMessage = onBranchMatch.Groups[2].Value;
            }

            // Parse date
            DateTime.TryParse(dateStr, out var date);

            stashes.Add(new GitStashEntry
            {
                Index = index,
                Message = stashMessage,
                Branch = branchName,
                RelativeDate = relativeDate
            });
        }

        return stashes;
    }

    // Interface stash methods
    async Task<GitOperationResult> IGitStatusService.CreateStashAsync(string workingDirectory, string? message, bool includeUntracked)
    {
        var result = await StashAsync(workingDirectory, message, includeUntracked);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    async Task<GitOperationResult> IGitStatusService.ApplyStashAsync(string workingDirectory, int index)
    {
        var result = await StashApplyAsync(workingDirectory, index);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    async Task<GitOperationResult> IGitStatusService.PopStashAsync(string workingDirectory, int index)
    {
        var result = await StashPopAsync(workingDirectory, index);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    async Task<GitOperationResult> IGitStatusService.DropStashAsync(string workingDirectory, int index)
    {
        var result = await StashDropAsync(workingDirectory, index);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    async Task<GitOperationResult> IGitStatusService.CreateBranchFromStashAsync(string workingDirectory, string branchName, int index)
    {
        var result = await StashBranchAsync(workingDirectory, index, branchName);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    public async Task<(bool Success, string? Error)> StashAsync(string workingDirectory, string? message = null, bool includeUntracked = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var args = "stash push";

        if (includeUntracked)
            args += " -u";

        if (!string.IsNullOrWhiteSpace(message))
        {
            var escapedMessage = message.Replace("\"", "\\\"");
            args += $" -m \"{escapedMessage}\"";
        }

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, args);
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> StashApplyAsync(string workingDirectory, int index)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash apply stash@{{{index}}}");
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> StashPopAsync(string workingDirectory, int index)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash pop stash@{{{index}}}");
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> StashDropAsync(string workingDirectory, int index)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash drop stash@{{{index}}}");
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> StashBranchAsync(string workingDirectory, int index, string branchName)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        if (string.IsNullOrWhiteSpace(branchName))
            return (false, "Branch name cannot be empty");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash branch \"{branchName}\" stash@{{{index}}}");
        return (result.Success, result.Error);
    }

    #endregion

    #region File History and Blame Operations

    public async Task<GitBlameResult?> GetFileBlameAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // Use git blame with line-porcelain format for detailed info
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, $"blame --line-porcelain -- \"{filePath}\"");

        if (string.IsNullOrEmpty(output))
            return null;

        var result = new GitBlameResult { FilePath = filePath };
        var lines = output.Split('\n');

        string? currentHash = null;
        string? currentAuthor = null;
        string? currentAuthorEmail = null;
        long? currentAuthorTime = null;
        int currentLineNumber = 0;
        string? previousHash = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (string.IsNullOrEmpty(line))
                continue;

            // First line of a block: <40-char hash> <original line> <final line> [<line count>]
            if (line.Length >= 40 && !line.StartsWith('\t') && !line.Contains(' ') ||
                (line.Length > 40 && line[40] == ' ' && char.IsLetterOrDigit(line[0])))
            {
                var parts = line.Split(' ');
                if (parts.Length >= 3 && parts[0].Length == 40)
                {
                    currentHash = parts[0];
                    if (int.TryParse(parts[2], out var lineNum))
                        currentLineNumber = lineNum;
                }
            }
            else if (line.StartsWith("author "))
            {
                currentAuthor = line.Substring(7);
            }
            else if (line.StartsWith("author-mail "))
            {
                currentAuthorEmail = line.Substring(12).Trim('<', '>');
            }
            else if (line.StartsWith("author-time "))
            {
                if (long.TryParse(line.Substring(12), out var time))
                    currentAuthorTime = time;
            }
            else if (line.StartsWith("\t"))
            {
                // This is the actual content line
                if (currentHash != null && currentAuthorTime.HasValue)
                {
                    var commitDate = DateTimeOffset.FromUnixTimeSeconds(currentAuthorTime.Value);
                    var blameLine = new GitBlameLine
                    {
                        CommitHash = currentHash,
                        ShortHash = currentHash.Length >= 7 ? currentHash[..7] : currentHash,
                        Author = currentAuthor ?? "Unknown",
                        AuthorEmail = currentAuthorEmail ?? "",
                        CommitDate = commitDate,
                        RelativeDate = GetRelativeDate(commitDate),
                        LineNumber = currentLineNumber,
                        LineContent = line.Substring(1), // Remove leading tab
                        IsFirstInGroup = currentHash != previousHash
                    };

                    result.Lines.Add(blameLine);
                    previousHash = currentHash;
                }
            }
        }

        return result;
    }

    public async Task<List<GitCommit>> GetFileHistoryAsync(string workingDirectory, string filePath, int skip = 0, int take = 50)
    {
        var commits = new List<GitCommit>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return commits;

        // Use --follow to track file renames
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"log --follow --skip={skip} -n {take} --format=\"%H|%an|%ae|%at|%s\" -- \"{filePath}\"");

        if (string.IsNullOrEmpty(output))
            return commits;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 5);
            if (parts.Length < 5) continue;

            if (!long.TryParse(parts[3], out var timestamp))
                continue;

            var commitDate = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            commits.Add(new GitCommit
            {
                Hash = parts[0],
                ShortHash = parts[0].Length >= 7 ? parts[0][..7] : parts[0],
                AuthorName = parts[1],
                AuthorEmail = parts[2],
                CommitDate = commitDate,
                RelativeDate = GetRelativeDate(commitDate),
                Subject = parts[4]
            });
        }

        return commits;
    }

    public async Task<string?> GetFileContentAtCommitAsync(string workingDirectory, string commitHash, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        return await _gitRunner.RunGitCommandAsync(workingDirectory, $"show {commitHash}:\"{filePath}\"");
    }

    #endregion

    #region Reflog Operations

    public async Task<List<GitReflogEntry>> GetReflogAsync(string workingDirectory, int take = 100)
    {
        var entries = new List<GitReflogEntry>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return entries;

        // Format: hash|selector|subject|date
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"reflog --format=\"%H|%gd|%gs|%ci\" -n {take}");

        if (string.IsNullOrEmpty(output))
            return entries;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 4);
            if (parts.Length < 4) continue;

            var hash = parts[0].Trim();
            var selector = parts[1].Trim();
            var subject = parts[2].Trim();
            var dateStr = parts[3].Trim();

            // Parse action from subject (e.g., "commit: message" or "checkout: moving from...")
            var action = "other";
            var message = subject;
            var colonIndex = subject.IndexOf(':');
            if (colonIndex > 0)
            {
                action = subject.Substring(0, colonIndex).Trim().ToLower();
                message = subject.Substring(colonIndex + 1).Trim();
            }

            DateTime.TryParse(dateStr, out var date);

            entries.Add(new GitReflogEntry
            {
                Hash = hash,
                ShortHash = hash.Length >= 7 ? hash[..7] : hash,
                Selector = selector,
                Action = action,
                Description = message,
                RelativeTime = GetRelativeDate(date)
            });
        }

        return entries;
    }

    public async Task<(bool Success, string? Error)> CreateBranchFromRefAsync(string workingDirectory, string refSpec, string branchName)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        if (string.IsNullOrWhiteSpace(branchName))
            return (false, "Branch name cannot be empty");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, $"checkout -b \"{branchName}\" {refSpec}");
        return (result.Success, result.Error);
    }

    #endregion

    #region Cherry-pick Operations

    public async Task<(bool Success, string? Error)> CherryPickAsync(string workingDirectory, string commitHash)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, $"cherry-pick {commitHash}");
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> CherryPickContinueAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, "cherry-pick --continue");
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> CherryPickAbortAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, "cherry-pick --abort");
        return (result.Success, result.Error);
    }

    #endregion

    #region Revert Operations

    public async Task<(bool Success, string? Error)> RevertAsync(string workingDirectory, string commitHash)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, $"revert {commitHash} --no-edit");
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> RevertContinueAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, "revert --continue");
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> RevertAbortAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, "Directory does not exist");

        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, "revert --abort");
        return (result.Success, result.Error);
    }

    #endregion

    #region Gitignore Operations

    public async Task<HashSet<string>> GetIgnoredFilesAsync(string workingDirectory)
    {
        var ignoredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return ignoredFiles;

        // Use git status --ignored --porcelain to get ignored files
        // Output format for ignored files: !! path/to/file
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, "status --ignored --porcelain");

        if (string.IsNullOrEmpty(output))
            return ignoredFiles;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // Ignored files are marked with !!
            if (line.StartsWith("!! "))
            {
                var relativePath = line.Substring(3).TrimEnd('/');
                var fullPath = Path.GetFullPath(Path.Combine(workingDirectory, relativePath));
                ignoredFiles.Add(fullPath);
            }
        }

        return ignoredFiles;
    }

    #endregion

    #region Interface Implementation Methods

    // File diff between commits
    async Task<string?> IGitStatusService.GetFileDiffBetweenCommitsAsync(string workingDirectory, string filePath, string fromHash, string toHash)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        return await _gitRunner.RunGitCommandAsync(workingDirectory, $"diff {fromHash}..{toHash} -- \"{filePath}\"");
    }

    // Reflog interface
    async Task<List<GitReflogEntry>> IGitStatusService.GetReflogAsync(string workingDirectory, int count)
    {
        return await GetReflogAsync(workingDirectory, count);
    }

    // Cherry-pick interface methods
    async Task<GitOperationResult> IGitStatusService.CherryPickAsync(string workingDirectory, string commitHash, bool noCommit)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var args = noCommit ? $"cherry-pick --no-commit {commitHash}" : $"cherry-pick {commitHash}";
        return await _gitRunner.RunGitOperationAsync(workingDirectory, args);
    }

    async Task<GitOperationResult> IGitStatusService.CherryPickContinueAsync(string workingDirectory)
    {
        var result = await CherryPickContinueAsync(workingDirectory);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    async Task<GitOperationResult> IGitStatusService.CherryPickAbortAsync(string workingDirectory)
    {
        var result = await CherryPickAbortAsync(workingDirectory);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    // Revert interface methods
    async Task<GitOperationResult> IGitStatusService.RevertAsync(string workingDirectory, string commitHash, bool noCommit)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var args = noCommit ? $"revert --no-commit {commitHash}" : $"revert {commitHash} --no-edit";
        return await _gitRunner.RunGitOperationAsync(workingDirectory, args);
    }

    async Task<GitOperationResult> IGitStatusService.RevertContinueAsync(string workingDirectory)
    {
        var result = await RevertContinueAsync(workingDirectory);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    async Task<GitOperationResult> IGitStatusService.RevertAbortAsync(string workingDirectory)
    {
        var result = await RevertAbortAsync(workingDirectory);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    // Branch from reflog
    async Task<GitOperationResult> IGitStatusService.CreateBranchFromRefAsync(string workingDirectory, string branchName, string refSpec)
    {
        var result = await CreateBranchFromRefAsync(workingDirectory, refSpec, branchName);
        return new GitOperationResult { Success = result.Success, Error = result.Error };
    }

    // Reset operations
    async Task<GitOperationResult> IGitStatusService.ResetAsync(string workingDirectory, string targetRef, ResetMode mode)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var modeArg = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            _ => "--mixed"
        };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"reset {modeArg} {targetRef}");
    }

    // Fast-forward merge
    async Task<GitOperationResult> IGitStatusService.FastForwardAsync(string workingDirectory, string targetBranch)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"merge --ff-only {targetBranch}");
    }

    async Task<(bool CanFastForward, int CommitCount, string? Error)> IGitStatusService.CheckFastForwardAsync(string workingDirectory, string targetBranch)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, 0, "Directory does not exist");

        // Check if target is ancestor of current HEAD
        var mergeBase = await _gitRunner.RunGitCommandAsync(workingDirectory, $"merge-base HEAD {targetBranch}");
        var currentHead = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse HEAD");

        if (mergeBase?.Trim() != currentHead?.Trim())
            return (false, 0, "Cannot fast-forward: branches have diverged");

        // Count commits to fast-forward
        var countOutput = await _gitRunner.RunGitCommandAsync(workingDirectory, $"rev-list --count HEAD..{targetBranch}");
        var commitCount = int.TryParse(countOutput?.Trim(), out var count) ? count : 0;

        return (true, commitCount, null);
    }

    // Rebase operations
    async Task<GitOperationResult> IGitStatusService.RebaseAsync(string workingDirectory, string ontoBranch)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"rebase {ontoBranch}");
    }

    async Task<GitOperationResult> IGitStatusService.RebaseContinueAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "rebase --continue");
    }

    async Task<GitOperationResult> IGitStatusService.RebaseAbortAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "rebase --abort");
    }

    async Task<GitOperationResult> IGitStatusService.RebaseSkipAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "rebase --skip");
    }

    async Task<bool> IGitStatusService.IsRebaseInProgressAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return false;

        var gitDir = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse --git-dir");
        if (string.IsNullOrEmpty(gitDir))
            return false;

        var rebaseMerge = Path.Combine(workingDirectory, gitDir.Trim(), "rebase-merge");
        var rebaseApply = Path.Combine(workingDirectory, gitDir.Trim(), "rebase-apply");

        return _fileSystem.DirectoryExists(rebaseMerge) || _fileSystem.DirectoryExists(rebaseApply);
    }

    // Branch comparison
    async Task<BranchComparisonResult> IGitStatusService.CompareBranchesAsync(string workingDirectory, string baseBranch, string compareBranch)
    {
        var result = new BranchComparisonResult
        {
            BaseBranch = baseBranch,
            CompareBranch = compareBranch
        };

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return result;

        // Get ahead/behind counts
        var (ahead, behind) = await ((IGitStatusService)this).GetAheadBehindAsync(workingDirectory, compareBranch, baseBranch);
        result.AheadCount = ahead;
        result.BehindCount = behind;

        // Get commits unique to compare branch
        result.UniqueCommits = await ((IGitStatusService)this).GetCommitsBetweenAsync(workingDirectory, baseBranch, compareBranch);

        // Get changed files
        var diffOutput = await _gitRunner.RunGitCommandAsync(workingDirectory, $"diff --name-status {baseBranch}...{compareBranch}");
        if (!string.IsNullOrEmpty(diffOutput))
        {
            var lines = diffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Length < 2) continue;
                var status = line[0];
                var path = line.Substring(1).Trim();

                result.ChangedFiles.Add(new GitFileStatus
                {
                    FilePath = path,
                    Status = ParseStatusChar(status),
                    IsStaged = false
                });
            }
        }

        return result;
    }

    async Task<List<GitCommit>> IGitStatusService.GetCommitsBetweenAsync(string workingDirectory, string fromRef, string toRef)
    {
        var commits = new List<GitCommit>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return commits;

        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            $"log {fromRef}..{toRef} --format=\"%H|%an|%ae|%at|%s\"");

        if (string.IsNullOrEmpty(output))
            return commits;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 5);
            if (parts.Length < 5) continue;

            if (!long.TryParse(parts[3], out var timestamp))
                continue;

            var commitDate = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            commits.Add(new GitCommit
            {
                Hash = parts[0],
                ShortHash = parts[0].Length >= 7 ? parts[0][..7] : parts[0],
                AuthorName = parts[1],
                AuthorEmail = parts[2],
                CommitDate = commitDate,
                RelativeDate = GetRelativeDate(commitDate),
                Subject = parts[4]
            });
        }

        return commits;
    }

    // Key branches detection
    async Task<List<GitBranch>> IGitStatusService.GetKeyBranchesAsync(string workingDirectory, IEnumerable<string> keyBranchPatterns)
    {
        var allBranches = await GetBranchesAsync(workingDirectory);
        var keyBranches = new List<GitBranch>();

        foreach (var pattern in keyBranchPatterns)
        {
            var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
            keyBranches.AddRange(allBranches.Where(b => regex.IsMatch(b.ShortName) && !keyBranches.Contains(b)));
        }

        return keyBranches;
    }

    // Ahead/behind
    async Task<(int Ahead, int Behind)> IGitStatusService.GetAheadBehindAsync(string workingDirectory, string branch, string compareTo)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (0, 0);

        var aheadOutput = await _gitRunner.RunGitCommandAsync(workingDirectory, $"rev-list --count {compareTo}..{branch}");
        var behindOutput = await _gitRunner.RunGitCommandAsync(workingDirectory, $"rev-list --count {branch}..{compareTo}");

        var ahead = int.TryParse(aheadOutput?.Trim(), out var a) ? a : 0;
        var behind = int.TryParse(behindOutput?.Trim(), out var b) ? b : 0;

        return (ahead, behind);
    }

    #endregion

    #region Helper Methods

    private static string GetRelativeDate(DateTimeOffset date)
    {
        var span = DateTimeOffset.Now - date;

        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";

        return $"{(int)(span.TotalDays / 365)}y ago";
    }

    private static string GetRelativeDate(DateTime date)
    {
        return GetRelativeDate(new DateTimeOffset(date));
    }

    #endregion
}