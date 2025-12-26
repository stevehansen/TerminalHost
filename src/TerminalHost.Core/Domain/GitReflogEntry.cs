namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a git reflog entry.
/// </summary>
public class GitReflogEntry
{
    /// <summary>
    /// The commit hash this entry points to.
    /// </summary>
    public string Hash { get; set; } = "";

    /// <summary>
    /// Short version of the hash (7 chars).
    /// </summary>
    public string ShortHash { get; set; } = "";

    /// <summary>
    /// The reflog selector, e.g., "HEAD@{0}".
    /// </summary>
    public string Selector { get; set; } = "";

    /// <summary>
    /// The action that caused this entry (commit, checkout, reset, merge, rebase, etc.).
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// The description of the action.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Relative time (e.g., "2 hours ago").
    /// </summary>
    public string RelativeTime { get; set; } = "";

    /// <summary>
    /// Icon for the action type.
    /// </summary>
    public string ActionIcon => Action.ToLowerInvariant() switch
    {
        "commit" => "C",
        "commit (initial)" => "C",
        "commit (amend)" => "A",
        "checkout" => ">",
        "reset" => "R",
        "merge" => "M",
        "rebase" => "B",
        "rebase (finish)" => "B",
        "rebase (start)" => "B",
        "pull" => "P",
        "cherry-pick" => "K",
        "revert" => "V",
        "branch" => "+",
        _ => "?"
    };

    /// <summary>
    /// Display text combining action and description.
    /// </summary>
    public string DisplayText => string.IsNullOrEmpty(Description)
        ? Action
        : $"{Action}: {Description}";
}
