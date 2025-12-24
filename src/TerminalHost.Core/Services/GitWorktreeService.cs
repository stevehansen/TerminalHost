using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for managing git worktrees.
/// </summary>
public sealed class GitWorktreeService : IGitWorktreeService
{
    private readonly IGitProcessRunner _gitRunner;
    private readonly IFileSystem _fileSystem;

    public GitWorktreeService(IGitProcessRunner gitRunner, IFileSystem fileSystem)
    {
        _gitRunner = gitRunner;
        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyList<WorktreeInfo>> ListWorktreesAsync(string repoPath)
    {
        var worktrees = new List<WorktreeInfo>();

        if (!_fileSystem.DirectoryExists(repoPath))
            return worktrees;

        // Use porcelain format for reliable parsing
        var output = await _gitRunner.RunGitCommandAsync(repoPath, "worktree list --porcelain");
        if (string.IsNullOrEmpty(output))
            return worktrees;

        // Porcelain format:
        // worktree /path/to/main
        // HEAD abc123...
        // branch refs/heads/main
        //
        // worktree /path/to/feature
        // HEAD def456...
        // branch refs/heads/feature
        // locked [reason]
        //
        // Empty line separates entries

        WorktreeInfo? current = null;
        var isFirst = true;

        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                // End of current worktree entry
                if (current != null)
                {
                    worktrees.Add(current);
                    current = null;
                }
                isFirst = false;
                continue;
            }

            if (line.StartsWith("worktree "))
            {
                current = new WorktreeInfo
                {
                    Path = line.Substring(9).Trim(),
                    IsMain = isFirst
                };
            }
            else if (current != null)
            {
                if (line.StartsWith("HEAD "))
                {
                    current.CommitHash = line.Substring(5).Trim();
                }
                else if (line.StartsWith("branch "))
                {
                    var branchRef = line.Substring(7).Trim();
                    // Convert refs/heads/branch-name to branch-name
                    if (branchRef.StartsWith("refs/heads/"))
                        current.Branch = branchRef.Substring(11);
                    else
                        current.Branch = branchRef;
                }
                else if (line.StartsWith("detached"))
                {
                    current.IsDetached = true;
                }
                else if (line.StartsWith("locked"))
                {
                    current.IsLocked = true;
                    // Reason is optional, after "locked "
                    if (line.Length > 7)
                        current.LockReason = line.Substring(7).Trim();
                }
                else if (line.StartsWith("prunable"))
                {
                    current.IsPrunable = true;
                }
            }
        }

        // Don't forget the last entry if file doesn't end with blank line
        if (current != null)
        {
            worktrees.Add(current);
        }

        return worktrees;
    }

    public async Task<GitOperationResult> CreateWorktreeAsync(string repoPath, string branch, string targetPath, bool createBranch = false)
    {
        if (!_fileSystem.DirectoryExists(repoPath))
        {
            return new GitOperationResult
            {
                Success = false,
                Error = "Repository path does not exist."
            };
        }

        // Build the command
        var args = createBranch
            ? $"worktree add -b \"{branch}\" \"{targetPath}\""
            : $"worktree add \"{targetPath}\" \"{branch}\"";

        return await _gitRunner.RunGitOperationAsync(repoPath, args);
    }

    public async Task<GitOperationResult> RemoveWorktreeAsync(string worktreePath, bool force = false)
    {
        // Get the main worktree path first to run the command from there
        var mainPath = await GetMainWorktreePathAsync(worktreePath);
        if (mainPath == null)
        {
            return new GitOperationResult
            {
                Success = false,
                Error = "Could not determine main worktree path."
            };
        }

        var args = force
            ? $"worktree remove --force \"{worktreePath}\""
            : $"worktree remove \"{worktreePath}\"";

        return await _gitRunner.RunGitOperationAsync(mainPath, args);
    }

    public async Task<GitOperationResult> PruneWorktreesAsync(string repoPath)
    {
        if (!_fileSystem.DirectoryExists(repoPath))
        {
            return new GitOperationResult
            {
                Success = false,
                Error = "Repository path does not exist."
            };
        }

        return await _gitRunner.RunGitOperationAsync(repoPath, "worktree prune");
    }

    public async Task<bool> IsWorktreeAsync(string path)
    {
        if (!_fileSystem.DirectoryExists(path))
            return false;

        // A linked worktree has .git as a file (not directory) containing "gitdir: /path/to/.git/worktrees/name"
        var gitPath = Path.Combine(path, ".git");

        // If .git is a file, it's a linked worktree
        if (_fileSystem.FileExists(gitPath))
        {
            var content = await Task.Run(() => _fileSystem.ReadAllText(gitPath));
            return content?.StartsWith("gitdir:") == true;
        }

        return false;
    }

    public async Task<string?> GetMainWorktreePathAsync(string worktreePath)
    {
        if (!_fileSystem.DirectoryExists(worktreePath))
            return null;

        // Check if it's the main worktree (has .git directory)
        var gitDir = Path.Combine(worktreePath, ".git");
        if (_fileSystem.DirectoryExists(gitDir))
        {
            // This is the main worktree
            return worktreePath;
        }

        // Check if it's a linked worktree (has .git file)
        if (_fileSystem.FileExists(gitDir))
        {
            var content = await Task.Run(() => _fileSystem.ReadAllText(gitDir));
            if (content?.StartsWith("gitdir:") == true)
            {
                // Parse the path: "gitdir: /path/to/.git/worktrees/name"
                var linkedGitDir = content.Substring(7).Trim();

                // Navigate up from .git/worktrees/name to .git
                // Then up one more to get the main worktree path
                var worktreesDir = Path.GetDirectoryName(linkedGitDir);
                if (worktreesDir != null)
                {
                    var gitDirParent = Path.GetDirectoryName(worktreesDir);
                    if (gitDirParent != null)
                    {
                        var mainWorktree = Path.GetDirectoryName(gitDirParent);
                        return mainWorktree;
                    }
                }
            }
        }

        // Try using git command as fallback
        var output = await _gitRunner.RunGitCommandAsync(worktreePath, "worktree list --porcelain");
        if (!string.IsNullOrEmpty(output))
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("worktree "))
                {
                    // First worktree listed is the main one
                    return line.Substring(9).Trim();
                }
            }
        }

        return null;
    }

    public async Task<GitOperationResult> LockWorktreeAsync(string worktreePath, string? reason = null)
    {
        var mainPath = await GetMainWorktreePathAsync(worktreePath);
        if (mainPath == null)
        {
            return new GitOperationResult
            {
                Success = false,
                Error = "Could not determine main worktree path."
            };
        }

        var args = string.IsNullOrEmpty(reason)
            ? $"worktree lock \"{worktreePath}\""
            : $"worktree lock --reason \"{reason}\" \"{worktreePath}\"";

        return await _gitRunner.RunGitOperationAsync(mainPath, args);
    }

    public async Task<GitOperationResult> UnlockWorktreeAsync(string worktreePath)
    {
        var mainPath = await GetMainWorktreePathAsync(worktreePath);
        if (mainPath == null)
        {
            return new GitOperationResult
            {
                Success = false,
                Error = "Could not determine main worktree path."
            };
        }

        return await _gitRunner.RunGitOperationAsync(mainPath, $"worktree unlock \"{worktreePath}\"");
    }
}
