using System.IO;
using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a git worktree.
/// </summary>
public class WorktreeInfo
{
    /// <summary>
    /// The full path to the worktree directory.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// The branch name checked out in this worktree.
    /// </summary>
    [JsonPropertyName("branch")]
    public string Branch { get; set; } = "";

    /// <summary>
    /// The HEAD commit hash.
    /// </summary>
    [JsonPropertyName("commitHash")]
    public string CommitHash { get; set; } = "";

    /// <summary>
    /// Whether this is the main worktree (the original repository).
    /// </summary>
    [JsonPropertyName("isMain")]
    public bool IsMain { get; set; }

    /// <summary>
    /// Whether this is a bare repository.
    /// </summary>
    [JsonPropertyName("isBare")]
    public bool IsBare { get; set; }

    /// <summary>
    /// Whether the worktree is in detached HEAD state.
    /// </summary>
    [JsonPropertyName("isDetached")]
    public bool IsDetached { get; set; }

    /// <summary>
    /// Whether the worktree is locked.
    /// </summary>
    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; }

    /// <summary>
    /// Whether the worktree is prunable (orphaned).
    /// </summary>
    [JsonPropertyName("isPrunable")]
    public bool IsPrunable { get; set; }

    /// <summary>
    /// Display name for the worktree (directory name).
    /// </summary>
    [JsonIgnore]
    public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));

    /// <summary>
    /// Abbreviated path for display (replaces home with ~).
    /// </summary>
    [JsonIgnore]
    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Path.StartsWith(home))
                return "~" + Path[home.Length..];
            return Path;
        }
    }

    /// <summary>
    /// Short commit hash (first 7 characters).
    /// </summary>
    [JsonIgnore]
    public string ShortHash => CommitHash.Length >= 7 ? CommitHash[..7] : CommitHash;

    /// <summary>
    /// Display text for branch (includes detached state indicator).
    /// </summary>
    [JsonIgnore]
    public string BranchDisplay => IsDetached ? $"({ShortHash})" : Branch;

    /// <summary>
    /// Icon for the worktree state.
    /// </summary>
    [JsonIgnore]
    public string Icon
    {
        get
        {
            if (IsLocked) return "🔒";
            if (IsPrunable) return "⚠️";
            if (IsMain) return "🏠";
            if (IsDetached) return "📌";
            return "🌿";
        }
    }
}
