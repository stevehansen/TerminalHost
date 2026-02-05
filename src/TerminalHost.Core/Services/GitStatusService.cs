using System.IO;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class GitStatusService : IGitStatusService
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

        // Get stash count
        var stashOutput = await _gitRunner.RunGitCommandAsync(workingDirectory, "stash list");
        if (!string.IsNullOrEmpty(stashOutput))
            status.StashCount = stashOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        return status;
    }

    public async Task<List<GitFileStatus>> GetModifiedFilesAsync(string workingDirectory)
    {
        var files = new List<GitFileStatus>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return files;

        // Get list of submodule paths for detection
        var submodulePaths = await GetSubmodulePathsAsync(workingDirectory);

        // Get status in porcelain v1 format: XY PATH or XY ORIG -> PATH for renames
        // Use --untracked-files=all to show individual files in new directories (not just directory names)
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all");
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

            // Check if this path is a submodule
            var isSubmodule = submodulePaths.Contains(path.Replace('\\', '/'));

            var fileStatus = new GitFileStatus
            {
                FilePath = path,
                Status = ParseStatusChar(statusChar),
                IsStaged = isStaged,
                OriginalPath = originalPath,
                IsSubmodule = isSubmodule
            };

            files.Add(fileStatus);
        }

        return files;
    }

    private async Task<HashSet<string>> GetSubmodulePathsAsync(string workingDirectory)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get submodule paths from .gitmodules config
        // Format: submodule.name.path value
        var output = await _gitRunner.RunGitCommandAsync(
            workingDirectory,
            "config --file .gitmodules --get-regexp path",
            TimeSpan.FromSeconds(5));

        if (string.IsNullOrEmpty(output))
            return paths;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // Each line is "submodule.<name>.path <value>"
            var parts = line.Split(' ', 2);
            if (parts.Length == 2)
            {
                var submodulePath = parts[1].Trim();
                paths.Add(submodulePath);
            }
        }

        return paths;
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

        // Get all branches with tracking info and last commit info
        // Format: refname|HEAD indicator|upstream|track info|commit hash|relative date|subject
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory,
            "branch -a --format=\"%(refname:short)|%(HEAD)|%(upstream:short)|%(upstream:track)|%(objectname:short)|%(committerdate:relative)|%(subject)\" ");

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
            var commitHash = parts.Length > 4 ? parts[4].Trim() : null;
            var relativeDate = parts.Length > 5 ? parts[5].Trim() : null;
            var subject = parts.Length > 6 ? string.Join("|", parts.Skip(6)).Trim() : null; // Subject may contain |

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
            // Also detect "[gone]" when the remote tracking branch has been deleted
            int? ahead = null, behind = null;
            var isGone = false;
            if (!string.IsNullOrEmpty(trackInfo))
            {
                var aheadMatch = Regex.Match(trackInfo, @"ahead (\d+)");
                var behindMatch = Regex.Match(trackInfo, @"behind (\d+)");
                if (aheadMatch.Success) ahead = int.Parse(aheadMatch.Groups[1].Value);
                if (behindMatch.Success) behind = int.Parse(behindMatch.Groups[1].Value);

                // Check for [gone] status - remote tracking branch was deleted
                isGone = trackInfo.Contains("gone", StringComparison.OrdinalIgnoreCase);
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
                BehindCount = behind,
                IsGone = isGone,
                LastCommitHash = string.IsNullOrEmpty(commitHash) ? null : commitHash,
                LastCommitMessage = string.IsNullOrEmpty(subject) ? null : subject,
                LastCommitRelativeDate = string.IsNullOrEmpty(relativeDate) ? null : relativeDate
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

    #region Staging Operations

    public async Task<GitOperationResult> StageFileAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"add -- \"{filePath}\"");
    }

    public async Task<GitOperationResult> UnstageFileAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // Use restore --staged for unstaging (works for both tracked and newly added files)
        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"restore --staged -- \"{filePath}\"");
    }

    public async Task<GitOperationResult> StageAllAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "add -A");
    }

    public async Task<GitOperationResult> UnstageAllAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "restore --staged .");
    }

    public async Task<GitOperationResult> DiscardChangesAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // For tracked files: restore to HEAD version
        // For untracked files: we need to delete them (git restore doesn't work on untracked files)
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

        // For tracked files, use restore to discard changes
        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"restore -- \"{filePath}\"");
    }

    public async Task<GitOperationResult> DiscardAllChangesAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // Restore all tracked files to HEAD version
        var restoreResult = await _gitRunner.RunGitOperationAsync(workingDirectory, "restore .");
        if (!restoreResult.Success)
            return restoreResult;

        // Also clean untracked files
        return await _gitRunner.RunGitOperationAsync(workingDirectory, "clean -fd");
    }

    #endregion

    #region Commit Operations

    public async Task<GitOperationResult> CreateCommitAsync(string workingDirectory, string message, bool amend = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(message))
            return new GitOperationResult { Success = false, Error = "Commit message cannot be empty" };

        // Escape the message for command line
        var escapedMessage = message.Replace("\"", "\\\"");
        var amendFlag = amend ? "--amend " : "";

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"commit {amendFlag}-m \"{escapedMessage}\"");
    }

    #endregion

    #region Commit History

    public async Task<List<GitCommit>> GetCommitHistoryAsync(string workingDirectory, int count = 50, string? author = null, string? filePath = null)
    {
        var commits = new List<GitCommit>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return commits;

        // Build command with optional filters
        // Format: hash|short_hash|author_name|author_email|relative_date|ISO_date|subject|decorations|parent_hashes
        var format = "%H|%h|%an|%ae|%ar|%aI|%s|%d|%P";
        var args = $"log --format=\"{format}\" -n {count}";

        if (!string.IsNullOrEmpty(author))
            args += $" --author=\"{author}\"";

        if (!string.IsNullOrEmpty(filePath))
            args += $" --follow -- \"{filePath}\"";

        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, args);
        if (string.IsNullOrEmpty(output))
            return commits;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 7) continue;

            var commit = new GitCommit
            {
                Hash = parts[0],
                ShortHash = parts[1],
                AuthorName = parts[2],
                AuthorEmail = parts[3],
                RelativeDate = parts[4],
                CommitDate = DateTimeOffset.TryParse(parts[5], out var date) ? date : DateTimeOffset.MinValue,
                Subject = parts[6],
                Decorations = parts.Length > 7 ? parts[7].Trim() : null,
                ParentHashes = parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8])
                    ? parts[8].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                    : []
            };

            // Clean up decorations (remove surrounding parentheses)
            if (!string.IsNullOrEmpty(commit.Decorations))
            {
                commit.Decorations = commit.Decorations.Trim('(', ')', ' ');
                if (string.IsNullOrWhiteSpace(commit.Decorations))
                    commit.Decorations = null;
            }

            commits.Add(commit);
        }

        return commits;
    }

    public async Task<GitCommitDetails?> GetCommitDetailsAsync(string workingDirectory, string hash)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // Get commit info with stats
        var format = "%H|%h|%an|%ae|%cn|%ce|%ar|%aI|%s|%b|%P";
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, $"show --stat --format=\"{format}\" {hash}");

        if (string.IsNullOrEmpty(output))
            return null;

        var lines = output.Split('\n');
        if (lines.Length == 0) return null;

        // First line is the formatted commit info
        var firstLine = lines[0];
        var parts = firstLine.Split('|');
        if (parts.Length < 9) return null;

        var details = new GitCommitDetails
        {
            Hash = parts[0],
            ShortHash = parts[1],
            AuthorName = parts[2],
            AuthorEmail = parts[3],
            CommitterName = parts[4],
            CommitterEmail = parts[5],
            RelativeDate = parts[6],
            CommitDate = DateTimeOffset.TryParse(parts[7], out var date) ? date : DateTimeOffset.MinValue,
            Subject = parts[8],
            Body = parts.Length > 9 ? parts[9].Trim() : null,
            ParentHashes = parts.Length > 10 && !string.IsNullOrWhiteSpace(parts[10])
                ? parts[10].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                : []
        };

        // Parse file stats from remaining lines
        // Format: " filename | N +"/ "-" or "insertions(+)" / "deletions(-)" summary line
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Skip the summary line like "3 files changed, 10 insertions(+), 5 deletions(-)"
            if (line.Contains("files changed") || line.Contains("file changed"))
            {
                // Parse the summary
                var insertionsMatch = Regex.Match(line, @"(\d+) insertion");
                var deletionsMatch = Regex.Match(line, @"(\d+) deletion");
                if (insertionsMatch.Success) details.TotalInsertions = int.Parse(insertionsMatch.Groups[1].Value);
                if (deletionsMatch.Success) details.TotalDeletions = int.Parse(deletionsMatch.Groups[1].Value);
                continue;
            }

            // Parse file line: " filename | N ++--" or " filename | Bin X -> Y bytes"
            var pipeIndex = line.IndexOf('|');
            if (pipeIndex > 0)
            {
                var filePath = line.Substring(0, pipeIndex).Trim();
                var statsStr = line.Substring(pipeIndex + 1).Trim();

                var file = new GitCommitFile { FilePath = filePath };

                // Parse stats: count of + and - or "N ++" style
                var plusCount = statsStr.Count(c => c == '+');
                var minusCount = statsStr.Count(c => c == '-');
                file.Insertions = plusCount;
                file.Deletions = minusCount;

                // Determine status from the stats
                if (minusCount == 0 && plusCount > 0)
                    file.Status = GitFileStatusType.Added;
                else if (plusCount == 0 && minusCount > 0)
                    file.Status = GitFileStatusType.Deleted;
                else
                    file.Status = GitFileStatusType.Modified;

                details.Files.Add(file);
            }
        }

        return details;
    }

    public async Task<string?> GetCommitDiffAsync(string workingDirectory, string hash, string? filePath = null)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        var args = filePath != null
            ? $"show {hash} -- \"{filePath}\""
            : $"show {hash}";

        return await _gitRunner.RunGitCommandAsync(workingDirectory, args);
    }

    #endregion

    #region Stash Operations

    public async Task<List<GitStashEntry>> GetStashListAsync(string workingDirectory)
    {
        var stashes = new List<GitStashEntry>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return stashes;

        // Format: stash@{0}|WIP on branch: message|2 hours ago
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, "stash list --format=\"%gd|%gs|%cr\"");
        if (string.IsNullOrEmpty(output))
            return stashes;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 3) continue;

            var stashRef = parts[0].Trim(); // e.g., "stash@{0}"
            var message = parts[1].Trim();  // e.g., "WIP on main: commit message" or user message
            var relativeDate = parts[2].Trim();

            // Extract index from stash@{N}
            var indexMatch = Regex.Match(stashRef, @"stash@\{(\d+)\}");
            if (!indexMatch.Success) continue;

            var index = int.Parse(indexMatch.Groups[1].Value);

            // Extract branch from message (format: "WIP on branch:" or "On branch:")
            var branch = "";
            var branchMatch = Regex.Match(message, @"^(?:WIP )?[Oo]n ([^:]+):");
            if (branchMatch.Success)
            {
                branch = branchMatch.Groups[1].Value;
            }

            stashes.Add(new GitStashEntry
            {
                Index = index,
                Message = message,
                Branch = branch,
                RelativeDate = relativeDate
            });
        }

        return stashes;
    }

    public async Task<GitOperationResult> CreateStashAsync(string workingDirectory, string? message = null, bool includeUntracked = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var args = "stash push";
        if (includeUntracked) args += " -u";
        if (!string.IsNullOrEmpty(message))
        {
            var escapedMessage = message.Replace("\"", "\\\"");
            args += $" -m \"{escapedMessage}\"";
        }

        return await _gitRunner.RunGitOperationAsync(workingDirectory, args);
    }

    public async Task<GitOperationResult> ApplyStashAsync(string workingDirectory, int index)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash apply stash@{{{index}}}");
    }

    public async Task<GitOperationResult> PopStashAsync(string workingDirectory, int index)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash pop stash@{{{index}}}");
    }

    public async Task<GitOperationResult> DropStashAsync(string workingDirectory, int index)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash drop stash@{{{index}}}");
    }

    public async Task<GitOperationResult> CreateBranchFromStashAsync(string workingDirectory, string branchName, int index)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(branchName))
            return new GitOperationResult { Success = false, Error = "Branch name cannot be empty" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"stash branch \"{branchName}\" stash@{{{index}}}");
    }

    #endregion

    #region File History and Blame

    public async Task<GitBlameResult?> GetFileBlameAsync(string workingDirectory, string filePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // Use --line-porcelain for machine-readable output
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, $"blame --line-porcelain -- \"{filePath}\"");

        if (string.IsNullOrEmpty(output))
            return null;

        return ParseBlameOutput(output, filePath);
    }

    private GitBlameResult ParseBlameOutput(string output, string filePath)
    {
        var result = new GitBlameResult { FilePath = filePath };
        var lines = output.Split('\n');

        int lineNumber = 0;
        string? currentHash = null;
        string? author = null;
        string? authorEmail = null;
        long authorTime = 0;
        string? summary = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;

            // Line starting with 40-char hex is a new commit header
            // Format: <40-char hash> <orig-line> <final-line> [<num-lines>]
            if (line.Length >= 40 && IsHexString(line.AsSpan(0, 40)))
            {
                currentHash = line.Substring(0, 40);
                continue;
            }

            if (line.StartsWith("author "))
                author = line.Substring(7);
            else if (line.StartsWith("author-mail "))
                authorEmail = line.Substring(12).Trim('<', '>');
            else if (line.StartsWith("author-time "))
                long.TryParse(line.Substring(12), out authorTime);
            else if (line.StartsWith("summary "))
                summary = line.Substring(8);
            else if (line.StartsWith("\t"))
            {
                // This is the actual line content (prefixed with tab)
                lineNumber++;
                var blameLine = new GitBlameLine
                {
                    LineNumber = lineNumber,
                    CommitHash = currentHash ?? "",
                    ShortHash = currentHash?.Substring(0, 7) ?? "",
                    Author = author ?? "",
                    AuthorEmail = authorEmail ?? "",
                    CommitDate = DateTimeOffset.FromUnixTimeSeconds(authorTime),
                    RelativeDate = GetRelativeDate(authorTime),
                    Summary = summary ?? "",
                    LineContent = line.Length > 1 ? line.Substring(1) : "" // Remove the tab prefix
                };
                result.Lines.Add(blameLine);
            }
        }

        // Collect unique authors and assign colors
        result.UniqueAuthors = result.Lines.Select(l => l.Author).Distinct().ToList();
        result.AuthorColors = AssignAuthorColors(result.UniqueAuthors);

        // Mark first line in each commit group
        MarkBlameGroups(result.Lines);

        return result;
    }

    private static bool IsHexString(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    private static string GetRelativeDate(long unixTimestamp)
    {
        if (unixTimestamp == 0) return "";

        var date = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        var diff = DateTimeOffset.Now - date;

        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return $"{(int)(diff.TotalDays / 365)}y ago";
    }

    private static Dictionary<string, string> AssignAuthorColors(List<string> authors)
    {
        // Predefined colors for blame display (high contrast on dark background)
        var colors = new[]
        {
            "#9CDCFE", // Light blue
            "#DCDCAA", // Yellow
            "#4EC9B0", // Teal
            "#CE9178", // Orange
            "#C586C0", // Purple
            "#B5CEA8", // Light green
            "#D7BA7D", // Gold
            "#D16969", // Red
            "#6A9955", // Green
            "#569CD6"  // Blue
        };

        var result = new Dictionary<string, string>();
        for (int i = 0; i < authors.Count; i++)
        {
            result[authors[i]] = colors[i % colors.Length];
        }
        return result;
    }

    private static void MarkBlameGroups(List<GitBlameLine> lines)
    {
        if (lines.Count == 0) return;

        string? lastHash = null;
        int groupStart = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var currentHash = lines[i].CommitHash;

            if (currentHash != lastHash)
            {
                // Mark the first line of the new group
                lines[i].IsFirstInGroup = true;

                // Update previous group size
                if (i > 0 && groupStart < i)
                {
                    lines[groupStart].GroupSize = i - groupStart;
                }

                groupStart = i;
                lastHash = currentHash;
            }
        }

        // Set size for the last group
        if (groupStart < lines.Count)
        {
            lines[groupStart].GroupSize = lines.Count - groupStart;
        }
    }

    public async Task<string?> GetFileContentAtCommitAsync(string workingDirectory, string filePath, string commitHash)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // git show <hash>:<file> returns the file content at that commit
        // Need to use forward slashes for git path
        var gitPath = filePath.Replace('\\', '/');
        return await _gitRunner.RunGitCommandAsync(workingDirectory, $"show {commitHash}:\"{gitPath}\"");
    }

    public async Task<string?> GetFileDiffBetweenCommitsAsync(string workingDirectory, string filePath, string fromHash, string toHash)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return null;

        // git diff <hash1> <hash2> -- <file>
        return await _gitRunner.RunGitCommandAsync(workingDirectory, $"diff {fromHash} {toHash} -- \"{filePath}\"");
    }

    #endregion

    #region Reflog

    public async Task<List<GitReflogEntry>> GetReflogAsync(string workingDirectory, int count = 50)
    {
        var entries = new List<GitReflogEntry>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return entries;

        // Format: hash|short_hash|selector|action|subject|relative_time
        // %H = full hash, %h = short hash, %gd = selector (HEAD@{0}), %gs = subject (action: description), %ar = relative time
        var output = await _gitRunner.RunGitCommandAsync(
            workingDirectory,
            $"reflog --format=\"%H|%h|%gd|%gs|%ar\" -n {count}");

        if (string.IsNullOrEmpty(output))
            return entries;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 5) continue;

            var subject = parts[3].Trim();
            var action = subject;
            var description = "";

            // Parse "action: description" format (e.g., "checkout: moving from main to feature")
            var colonIndex = subject.IndexOf(':');
            if (colonIndex > 0)
            {
                action = subject.Substring(0, colonIndex).Trim();
                description = subject.Length > colonIndex + 1
                    ? subject.Substring(colonIndex + 1).Trim()
                    : "";
            }

            entries.Add(new GitReflogEntry
            {
                Hash = parts[0].Trim(),
                ShortHash = parts[1].Trim(),
                Selector = parts[2].Trim(),
                Action = action,
                Description = description,
                RelativeTime = parts[4].Trim()
            });
        }

        return entries;
    }

    #endregion

    #region Cherry-pick and Revert

    public async Task<GitOperationResult> CherryPickAsync(string workingDirectory, string commitHash, bool noCommit = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var args = noCommit
            ? $"cherry-pick --no-commit {commitHash}"
            : $"cherry-pick {commitHash}";

        return await _gitRunner.RunGitOperationAsync(workingDirectory, args);
    }

    public async Task<GitOperationResult> CherryPickContinueAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "cherry-pick --continue");
    }

    public async Task<GitOperationResult> CherryPickAbortAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "cherry-pick --abort");
    }

    public async Task<GitOperationResult> RevertAsync(string workingDirectory, string commitHash, bool noCommit = false)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        var args = noCommit
            ? $"revert --no-commit {commitHash}"
            : $"revert {commitHash}";

        return await _gitRunner.RunGitOperationAsync(workingDirectory, args);
    }

    public async Task<GitOperationResult> RevertContinueAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "revert --continue");
    }

    public async Task<GitOperationResult> RevertAbortAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "revert --abort");
    }

    public async Task<GitOperationResult> CreateBranchFromRefAsync(string workingDirectory, string branchName, string refSpec)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(branchName))
            return new GitOperationResult { Success = false, Error = "Branch name cannot be empty" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"branch \"{branchName}\" {refSpec}");
    }

    #endregion

    #region Reset Operations

    public async Task<GitOperationResult> ResetAsync(string workingDirectory, string targetRef, ResetMode mode = ResetMode.Mixed)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(targetRef))
            return new GitOperationResult { Success = false, Error = "Target reference cannot be empty" };

        var modeFlag = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            _ => "--mixed"
        };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"reset {modeFlag} \"{targetRef}\"");
    }

    #endregion

    #region Fast-Forward Operations

    public async Task<GitOperationResult> FastForwardAsync(string workingDirectory, string targetBranch)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(targetBranch))
            return new GitOperationResult { Success = false, Error = "Target branch cannot be empty" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"merge --ff-only \"{targetBranch}\"");
    }

    public async Task<(bool CanFastForward, int CommitCount, string? Error)> CheckFastForwardAsync(string workingDirectory, string targetBranch)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return (false, 0, "Directory does not exist");

        if (string.IsNullOrWhiteSpace(targetBranch))
            return (false, 0, "Target branch cannot be empty");

        // Get merge-base between current HEAD and target branch
        var mergeBase = await _gitRunner.RunGitCommandAsync(workingDirectory, $"merge-base HEAD \"{targetBranch}\"");
        if (string.IsNullOrEmpty(mergeBase))
            return (false, 0, "Could not find common ancestor");

        mergeBase = mergeBase.Trim();

        // Get current HEAD commit
        var currentHead = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse HEAD");
        if (string.IsNullOrEmpty(currentHead))
            return (false, 0, "Could not get current HEAD");

        currentHead = currentHead.Trim();

        // For fast-forward: merge-base must equal current HEAD
        // (i.e., current branch is an ancestor of target branch)
        if (mergeBase != currentHead)
            return (false, 0, "Branches have diverged; fast-forward not possible");

        // Count commits between HEAD and target
        var countOutput = await _gitRunner.RunGitCommandAsync(workingDirectory, $"rev-list --count HEAD..\"{targetBranch}\"");
        var commitCount = 0;
        if (!string.IsNullOrEmpty(countOutput) && int.TryParse(countOutput.Trim(), out var count))
            commitCount = count;

        if (commitCount == 0)
            return (false, 0, "Already up to date");

        return (true, commitCount, null);
    }

    #endregion

    #region Rebase Operations

    public async Task<GitOperationResult> RebaseAsync(string workingDirectory, string ontoBranch)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        if (string.IsNullOrWhiteSpace(ontoBranch))
            return new GitOperationResult { Success = false, Error = "Onto branch cannot be empty" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"rebase \"{ontoBranch}\"");
    }

    public async Task<GitOperationResult> RebaseContinueAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "rebase --continue");
    }

    public async Task<GitOperationResult> RebaseAbortAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "rebase --abort");
    }

    public async Task<GitOperationResult> RebaseSkipAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "rebase --skip");
    }

    public async Task<bool> IsRebaseInProgressAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return false;

        // Check for rebase-merge or rebase-apply directories in .git
        var gitDir = await _gitRunner.RunGitCommandAsync(workingDirectory, "rev-parse --git-dir");
        if (string.IsNullOrEmpty(gitDir))
            return false;

        gitDir = gitDir.Trim();

        // Handle both absolute and relative git dir paths
        var gitDirPath = Path.IsPathRooted(gitDir)
            ? gitDir
            : Path.Combine(workingDirectory, gitDir);

        return _fileSystem.DirectoryExists(Path.Combine(gitDirPath, "rebase-merge")) ||
               _fileSystem.DirectoryExists(Path.Combine(gitDirPath, "rebase-apply"));
    }

    #endregion

    #region Branch Comparison

    public async Task<BranchComparisonResult> CompareBranchesAsync(string workingDirectory, string baseBranch, string compareBranch)
    {
        var result = new BranchComparisonResult
        {
            BaseBranch = baseBranch,
            CompareBranch = compareBranch
        };

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return result;

        // Get merge-base
        var mergeBase = await _gitRunner.RunGitCommandAsync(workingDirectory, $"merge-base \"{baseBranch}\" \"{compareBranch}\"");
        result.MergeBase = mergeBase?.Trim() ?? "";

        // Get commits only in base branch (not in compare)
        result.CommitsOnlyInBase = await GetCommitsBetweenAsync(workingDirectory, compareBranch, baseBranch);

        // Get commits only in compare branch (not in base)
        result.CommitsOnlyInCompare = await GetCommitsBetweenAsync(workingDirectory, baseBranch, compareBranch);

        // Count files changed
        var diffStat = await _gitRunner.RunGitCommandAsync(workingDirectory, $"diff --stat \"{baseBranch}\" \"{compareBranch}\"");
        if (!string.IsNullOrEmpty(diffStat))
        {
            // Last line of diff --stat contains "X files changed" summary
            var lines = diffStat.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                var lastLine = lines[lines.Length - 1];
                var filesMatch = Regex.Match(lastLine, @"(\d+) files? changed");
                if (filesMatch.Success)
                    result.FilesChanged = int.Parse(filesMatch.Groups[1].Value);
            }
        }

        // Check fast-forward possibilities
        // Base can FF to compare if base has no unique commits (base is ancestor of compare)
        result.CanFastForwardBaseToCompare = result.CommitsOnlyInBase.Count == 0 && result.CommitsOnlyInCompare.Count > 0;

        // Compare can FF to base if compare has no unique commits (compare is ancestor of base)
        result.CanFastForwardCompareToBase = result.CommitsOnlyInCompare.Count == 0 && result.CommitsOnlyInBase.Count > 0;

        return result;
    }

    public async Task<List<GitCommit>> GetCommitsBetweenAsync(string workingDirectory, string fromRef, string toRef)
    {
        var commits = new List<GitCommit>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return commits;

        // Get commits that are in toRef but not in fromRef
        // Format same as GetCommitHistoryAsync
        var format = "%H|%h|%an|%ae|%ar|%aI|%s|%d|%P";
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, $"log --format=\"{format}\" \"{fromRef}..\"{toRef}\"");

        if (string.IsNullOrEmpty(output))
            return commits;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 7) continue;

            var commit = new GitCommit
            {
                Hash = parts[0],
                ShortHash = parts[1],
                AuthorName = parts[2],
                AuthorEmail = parts[3],
                RelativeDate = parts[4],
                CommitDate = DateTimeOffset.TryParse(parts[5], out var date) ? date : DateTimeOffset.MinValue,
                Subject = parts[6],
                Decorations = parts.Length > 7 ? parts[7].Trim() : null,
                ParentHashes = parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8])
                    ? parts[8].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                    : []
            };

            // Clean up decorations
            if (!string.IsNullOrEmpty(commit.Decorations))
            {
                commit.Decorations = commit.Decorations.Trim('(', ')', ' ');
                if (string.IsNullOrWhiteSpace(commit.Decorations))
                    commit.Decorations = null;
            }

            commits.Add(commit);
        }

        return commits;
    }

    public async Task<List<GitBranch>> GetKeyBranchesAsync(string workingDirectory, IEnumerable<string> keyBranchPatterns)
    {
        var allBranches = await GetBranchesAsync(workingDirectory);

        // Filter to only local branches that match the key branch patterns (case-insensitive)
        var patterns = new HashSet<string>(keyBranchPatterns, StringComparer.OrdinalIgnoreCase);

        return allBranches
            .Where(b => !b.IsRemote && patterns.Contains(b.ShortName))
            .ToList();
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindAsync(string workingDirectory, string branch, string compareTo)
    {
        int ahead = 0, behind = 0;

        try
        {
            // Get commits using left-right syntax
            // Output format: "X\tY" where X = commits only in left (compareTo), Y = commits only in right (branch)
            var output = await _gitRunner.RunGitCommandAsync(
                workingDirectory,
                $"rev-list --count --left-right \"{compareTo}\"...\"{branch}\"");

            if (!string.IsNullOrWhiteSpace(output))
            {
                var parts = output.Trim().Split('\t');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0], out behind);  // Commits in compareTo not in branch
                    int.TryParse(parts[1], out ahead);   // Commits in branch not in compareTo
                }
            }
        }
        catch
        {
            // If git command fails, return zeros
        }

        return (ahead, behind);
    }

    public async Task<GitOperationResult> UpdateBranchPointerAsync(string workingDirectory, string branchName, string targetRef)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory not found" };

        // Use 'git branch -f' to move the branch pointer without checkout
        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"branch -f \"{branchName}\" \"{targetRef}\"");
    }

    #endregion

    #region Submodule Operations

    public async Task<List<SubmoduleInfo>> GetSubmodulesAsync(string workingDirectory)
    {
        var submodules = new List<SubmoduleInfo>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return submodules;

        // git submodule status output format:
        //  abc1234 path/to/submodule (v1.0.0)    <- initialized, clean (space prefix)
        // -abc1234 path/to/submodule             <- not initialized (- prefix)
        // +abc1234 path/to/submodule (v1.0.0)    <- modified (+ prefix)
        // Uabc1234 path/to/submodule             <- merge conflict (U prefix)
        var output = await _gitRunner.RunGitCommandAsync(
            workingDirectory,
            "submodule status",
            TimeSpan.FromSeconds(10));

        if (string.IsNullOrEmpty(output))
            return submodules;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Length < 2) continue;

            var statusChar = line[0];
            var rest = line.Substring(1).Trim();

            // Parse: "abc1234 path/to/submodule (description)"
            var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var commit = parts[0];
            var pathAndDesc = parts[1];

            // Extract path and optional description (in parentheses)
            string path;
            string? description = null;

            var parenIndex = pathAndDesc.IndexOf(" (");
            if (parenIndex > 0)
            {
                path = pathAndDesc.Substring(0, parenIndex);
                description = pathAndDesc.Substring(parenIndex + 2).TrimEnd(')');
            }
            else
            {
                path = pathAndDesc;
            }

            var status = statusChar switch
            {
                '-' => SubmoduleStatus.Uninitialized,
                '+' => SubmoduleStatus.Modified,
                'U' => SubmoduleStatus.Modified, // Merge conflict treated as modified
                _ => SubmoduleStatus.Clean
            };

            submodules.Add(new SubmoduleInfo
            {
                Path = path,
                CurrentCommit = commit,
                Status = status,
                Description = description
            });
        }

        return submodules;
    }

    public async Task<GitOperationResult> InitializeSubmoduleAsync(string workingDirectory, string submodulePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // Initialize and update the submodule
        var initResult = await _gitRunner.RunGitOperationAsync(workingDirectory, $"submodule init \"{submodulePath}\"");
        if (!initResult.Success)
            return initResult;

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"submodule update \"{submodulePath}\"");
    }

    public async Task<GitOperationResult> UpdateSubmoduleAsync(string workingDirectory, string submodulePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"submodule update \"{submodulePath}\"");
    }

    public async Task<GitOperationResult> UpdateSubmoduleToLatestAsync(string workingDirectory, string submodulePath)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"submodule update --remote \"{submodulePath}\"");
    }

    #endregion

    #region Tag Operations

    public async Task<List<GitTag>> GetTagsAsync(string workingDirectory)
    {
        var tags = new List<GitTag>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return tags;

        // Use for-each-ref to get tag info in a single command
        // Format: refname:short | objectname:short | objectname | objecttype | subject | taggername | taggerdate | contents:subject
        var output = await _gitRunner.RunGitCommandAsync(
            workingDirectory,
            "for-each-ref --sort=-version:refname refs/tags/ --format=\"%(refname:short)|%(objectname:short)|%(*objectname:short)|%(objecttype)|%(subject)|%(taggername)|%(taggerdate:relative)\"",
            TimeSpan.FromSeconds(10));

        if (string.IsNullOrEmpty(output))
            return tags;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 7) continue;

            var name = parts[0].Trim();
            var tagHash = parts[1].Trim();
            var derefHash = parts[2].Trim();
            var objectType = parts[3].Trim();
            var subject = parts[4].Trim();
            var taggerName = parts[5].Trim();
            var taggerDate = parts[6].Trim();

            var isAnnotated = objectType == "tag";
            // For annotated tags, the dereferenced hash points to the commit
            var commitHash = isAnnotated && !string.IsNullOrEmpty(derefHash) ? derefHash : tagHash;

            tags.Add(new GitTag
            {
                Name = name,
                Hash = commitHash,
                ShortHash = commitHash,
                IsAnnotated = isAnnotated,
                Message = isAnnotated ? subject : null,
                CommitSubject = subject,
                TaggerName = isAnnotated ? taggerName : null,
                TaggerDate = isAnnotated ? taggerDate : null
            });
        }

        return tags;
    }

    public async Task<GitOperationResult> CreateTagAsync(string workingDirectory, string tagName, string? message = null, string? commitHash = null)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        string command;
        if (!string.IsNullOrEmpty(message))
        {
            // Annotated tag
            var escapedMessage = message.Replace("\"", "\\\"");
            command = $"tag -a \"{tagName}\" -m \"{escapedMessage}\"";
        }
        else
        {
            // Lightweight tag
            command = $"tag \"{tagName}\"";
        }

        if (!string.IsNullOrEmpty(commitHash))
        {
            command += $" {commitHash}";
        }

        return await _gitRunner.RunGitOperationAsync(workingDirectory, command);
    }

    public async Task<GitOperationResult> DeleteTagAsync(string workingDirectory, string tagName)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"tag -d \"{tagName}\"");
    }

    public async Task<GitOperationResult> PushTagAsync(string workingDirectory, string tagName)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"push origin \"{tagName}\"");
    }

    public async Task<GitOperationResult> PushAllTagsAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, "push origin --tags");
    }

    public async Task<GitOperationResult> DeleteRemoteTagAsync(string workingDirectory, string tagName)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"push origin --delete \"{tagName}\"");
    }

    #endregion
}
