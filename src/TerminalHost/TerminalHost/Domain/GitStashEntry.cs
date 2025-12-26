namespace TerminalHost.Domain;

/// <summary>
/// Represents a git stash entry.
/// </summary>
public class GitStashEntry
{
    /// <summary>
    /// The stash index (0 for the most recent stash).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// The stash reference (e.g., "stash@{0}").
    /// </summary>
    public string Reference { get; set; } = "";

    /// <summary>
    /// The stash message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// The branch name where the stash was created.
    /// </summary>
    public string BranchName { get; set; } = "";

    /// <summary>
    /// The date when the stash was created.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Relative date string (e.g., "2 hours ago").
    /// </summary>
    public string RelativeDate { get; set; } = "";

    /// <summary>
    /// Display string for the stash entry.
    /// </summary>
    public string DisplayName => $"stash@{{{Index}}}: {Message}";

    /// <summary>
    /// Short description including branch name.
    /// </summary>
    public string Description => string.IsNullOrEmpty(BranchName)
        ? Message
        : $"On {BranchName}: {Message}";
}
