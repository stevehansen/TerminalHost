using System.IO;
using System.Text.RegularExpressions;
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

    #endregion
}