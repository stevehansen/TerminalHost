namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a git stash entry.
/// </summary>
public class GitStashEntry
{
    /// <summary>
    /// The stash index (0, 1, 2, etc.)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// The stash message (user-provided or "WIP on branch: commit message")
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The branch where the stash was created
    /// </summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>
    /// Relative date of the stash (e.g., "2 hours ago")
    /// </summary>
    public string RelativeDate { get; set; } = string.Empty;

    /// <summary>
    /// The stash reference used in git commands (e.g., "stash@{0}")
    /// </summary>
    public string StashRef => $"stash@{{{Index}}}";

    /// <summary>
    /// Display title showing index and message
    /// </summary>
    public string DisplayTitle => $"stash@{{{Index}}}: {Message}";

    /// <summary>
    /// Short display for list items
    /// </summary>
    public string ShortMessage
    {
        get
        {
            // Remove "WIP on branch: " or "On branch: " prefix if present
            var msg = Message;
            if (msg.StartsWith("WIP on "))
            {
                var colonIndex = msg.IndexOf(": ", 7);
                if (colonIndex > 0)
                    msg = msg[(colonIndex + 2)..];
            }
            else if (msg.StartsWith("On "))
            {
                var colonIndex = msg.IndexOf(": ", 3);
                if (colonIndex > 0)
                    msg = msg[(colonIndex + 2)..];
            }
            return msg;
        }
    }
}
