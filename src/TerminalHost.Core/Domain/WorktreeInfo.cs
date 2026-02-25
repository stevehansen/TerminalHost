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
    /// Whether this is a bare repository (no working tree).
    /// </summary>
    public bool IsBare { get; set; }

    /// <summary>
    /// Gets the display name for this worktree.
    /// </summary>
    public string DisplayName => IsMain ? "main" : (string.IsNullOrEmpty(Branch) ? System.IO.Path.GetFileName(Path) : Branch);

    /// <summary>
    /// Icon to display based on worktree state.
    /// </summary>
    public string Icon => IsMain ? "📁" : (IsDetached ? "🔗" : "🔀");

    /// <summary>
    /// Branch name formatted for display (shows "detached" or "bare" when appropriate).
    /// </summary>
    public string BranchDisplay => IsDetached ? $"(detached) {CommitHash[..Math.Min(7, CommitHash.Length)]}"
        : IsBare ? "(bare)"
        : string.IsNullOrEmpty(Branch) ? System.IO.Path.GetFileName(Path)
        : Branch;

    /// <summary>
    /// Shortened path for display with ellipsis support.
    /// </summary>
    public string DisplayPath => Path;

    /// <summary>
    /// Status badge text showing worktree state (locked, prunable, main).
    /// </summary>
    public string StatusBadge
    {
        get
        {
            var parts = new List<string>();
            if (IsMain) parts.Add("Main Worktree");
            if (IsLocked)
            {
                parts.Add(string.IsNullOrEmpty(LockReason) ? "🔒 Locked" : $"🔒 {LockReason}");
            }
            if (IsPrunable) parts.Add("⚠️ Missing");
            return string.Join("  •  ", parts);
        }
    }

    /// <summary>
    /// Color for status badge (red for prunable, orange for locked, green for normal).
    /// </summary>
    public string StatusColor
    {
        get
        {
            if (IsPrunable) return "#F44336";
            if (IsLocked) return "#FF9800";
            return "#4CAF50";
        }
    }

    /// <summary>
    /// Whether this worktree can be removed (non-main worktrees only).
    /// </summary>
    public bool CanRemove => !IsMain;

    /// <summary>
    /// Button text for lock/unlock toggle.
    /// </summary>
    public string LockButtonText => IsLocked ? "Unlock" : "Lock";
}
