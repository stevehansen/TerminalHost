namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents information about a git worktree.
/// </summary>
public class WorktreeInfo
{
    /// <summary>
    /// Full path to the worktree directory.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The branch checked out in this worktree.
    /// </summary>
    public string Branch { get; set; } = "";

    /// <summary>
    /// The commit hash HEAD is pointing to.
    /// </summary>
    public string CommitHash { get; set; } = "";

    /// <summary>
    /// Whether this is the main worktree (the original repository).
    /// </summary>
    public bool IsMain { get; set; }

    /// <summary>
    /// Whether the worktree is locked (prevents removal).
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Whether the worktree is prunable (missing from disk).
    /// </summary>
    public bool IsPrunable { get; set; }

    /// <summary>
    /// Optional reason for the worktree being locked.
    /// </summary>
    public string? LockReason { get; set; }

    /// <summary>
    /// Whether HEAD is detached (not on a branch).
    /// </summary>
    public bool IsDetached { get; set; }

    /// <summary>
    /// Gets the display name for this worktree.
    /// </summary>
    public string DisplayName => IsMain ? "main" : (string.IsNullOrEmpty(Branch) ? System.IO.Path.GetFileName(Path) : Branch);
}
