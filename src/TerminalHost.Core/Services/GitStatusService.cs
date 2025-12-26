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

        return status;
    }

    public async Task<List<GitFileStatus>> GetModifiedFilesAsync(string workingDirectory)
    {
        var files = new List<GitFileStatus>();

        if (!_fileSystem.DirectoryExists(workingDirectory))
            return files;

        // Get status in porcelain v1 format: XY PATH or XY ORIG -> PATH for renames
        // Use -u (--untracked-files=all) to show individual files in new directories
        var output = await _gitRunner.RunGitCommandAsync(workingDirectory, "status --porcelain -u");
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
        // For untracked files: we'd need to delete, but that's handled in the ViewModel with confirmation
        return await _gitRunner.RunGitOperationAsync(workingDirectory, $"restore -- \"{filePath}\"");
    }

    public async Task<GitOperationResult> DiscardAllChangesAsync(string workingDirectory)
    {
        if (!_fileSystem.DirectoryExists(workingDirectory))
            return new GitOperationResult { Success = false, Error = "Directory does not exist" };

        // Restore all tracked files to HEAD version
        return await _gitRunner.RunGitOperationAsync(workingDirectory, "restore .");
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
}
