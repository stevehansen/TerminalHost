namespace TerminalHost.Core.Domain;

/// <summary>
/// Specifies the mode for git reset operations.
/// </summary>
public enum ResetMode
{
    /// <summary>
    /// Keep changes staged (git reset --soft).
    /// Moves HEAD but keeps staging area and working directory unchanged.
    /// </summary>
    Soft,

    /// <summary>
    /// Keep changes unstaged (git reset --mixed, the default).
    /// Moves HEAD and resets staging area, but keeps working directory unchanged.
    /// </summary>
    Mixed,

    /// <summary>
    /// Discard all changes (git reset --hard).
    /// Moves HEAD and resets both staging area and working directory.
    /// WARNING: This will permanently discard uncommitted changes.
    /// </summary>
    Hard
}
