using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for managing git worktrees.
/// </summary>
internal sealed class GitWorktreeService : IGitWorktreeService
{
    private readonly IGitProcessRunner _gitRunner;

    public GitWorktreeService(IGitProcessRunner gitRunner)
    {
        _gitRunner = gitRunner;
    }

    public async Task<List<WorktreeInfo>> GetWorktreesAsync(string repositoryPath)
    {
        var worktrees = new List<WorktreeInfo>();

        var output = await _gitRunner.RunGitCommandAsync(repositoryPath, "worktree list --porcelain");
        if (string.IsNullOrEmpty(output))
            return worktrees;

        // Parse porcelain output format:
        // worktree /path/to/worktree
        // HEAD <commit>
        // branch refs/heads/<branch>  (or "detached")
        // <blank line>

        WorktreeInfo? current = null;
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.StartsWith("worktree "))
            {
                if (current != null)
                    worktrees.Add(current);

                current = new WorktreeInfo
                {
                    Path = line[9..].Trim()
                };
            }
            else if (current != null)
            {
                if (line.StartsWith("HEAD "))
                {
                    current.CommitHash = line[5..].Trim();
                }
                else if (line.StartsWith("branch "))
                {
                    var branch = line[7..].Trim();
                    // Convert refs/heads/branch to just branch
                    if (branch.StartsWith("refs/heads/"))
                        branch = branch[11..];
                    current.Branch = branch;
                }
                else if (line == "detached")
                {
                    current.IsDetached = true;
                }
                else if (line == "bare")
                {
                    current.IsBare = true;
                }
                else if (line == "locked")
                {
                    current.IsLocked = true;
                }
                else if (line == "prunable")
                {
                    current.IsPrunable = true;
                }
            }
        }

        if (current != null)
            worktrees.Add(current);

        // Mark the first worktree as main (the original repository)
        if (worktrees.Count > 0)
            worktrees[0].IsMain = true;

        return worktrees;
    }

    public async Task<(bool Success, string? Error)> CreateWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        string branch,
        bool createBranch = false)
    {
        var args = createBranch
            ? $"worktree add -b \"{branch}\" \"{worktreePath}\" HEAD"
            : $"worktree add \"{worktreePath}\" \"{branch}\"";

        var result = await _gitRunner.RunGitOperationAsync(repositoryPath, args);

        if (result.Success)
            return (true, null);

        return (false, result.Error?.Trim() ?? "Failed to create worktree");
    }

    public async Task<(bool Success, string? Error)> RemoveWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        bool force = false)
    {
        var args = force
            ? $"worktree remove --force \"{worktreePath}\""
            : $"worktree remove \"{worktreePath}\"";

        var result = await _gitRunner.RunGitOperationAsync(repositoryPath, args);

        if (result.Success)
            return (true, null);

        return (false, result.Error?.Trim() ?? "Failed to remove worktree");
    }

    public async Task<string?> GetRepositoryRootAsync(string path)
    {
        var output = await _gitRunner.RunGitCommandAsync(path, "rev-parse --show-toplevel");
        return output?.Trim();
    }

    public async Task<(bool Success, string? Error)> CreateBranchFromWorktreeAsync(
        string repositoryPath,
        string worktreePath,
        string branchName)
    {
        // Create branch in the worktree pointing to its HEAD
        var result = await _gitRunner.RunGitOperationAsync(
            worktreePath,
            $"checkout -b \"{branchName}\"");

        if (result.Success)
            return (true, null);

        return (false, result.Error?.Trim() ?? "Failed to create branch");
    }

    public async Task<List<string>> GetAvailableBranchesAsync(string repositoryPath)
    {
        var branches = new List<string>();

        // Get local branches
        var output = await _gitRunner.RunGitCommandAsync(repositoryPath, "branch --format=%(refname:short)");
        if (!string.IsNullOrEmpty(output))
        {
            branches.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b)));
        }

        // Get remote branches
        output = await _gitRunner.RunGitCommandAsync(repositoryPath, "branch -r --format=%(refname:short)");
        if (!string.IsNullOrEmpty(output))
        {
            branches.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b) && !b.Contains("HEAD")));
        }

        return branches.Distinct().OrderBy(b => b).ToList();
    }
}
