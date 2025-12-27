namespace TerminalHost.Domain;

/// <summary>
/// Represents a git reflog entry.
/// </summary>
public class GitReflogEntry
{
    /// <summary>
    /// The full commit hash.
    /// </summary>
    public string Hash { get; set; } = "";

    /// <summary>
    /// Short (7-char) commit hash.
    /// </summary>
    public string ShortHash => Hash.Length >= 7 ? Hash[..7] : Hash;

    /// <summary>
    /// The reflog selector (e.g., "HEAD@{0}").
    /// </summary>
    public string Selector { get; set; } = "";

    /// <summary>
    /// The action type (e.g., "commit", "checkout", "rebase", "merge", "reset").
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// The full reflog message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// The date of the reflog entry.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Relative date string (e.g., "2 hours ago").
    /// </summary>
    public string RelativeDate
    {
        get
        {
            var span = DateTime.Now - Date;

            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";

            return $"{(int)(span.TotalDays / 365)}y ago";
        }
    }

    /// <summary>
    /// Icon for the action type.
    /// </summary>
    public string ActionIcon => Action.ToLower() switch
    {
        "commit" => "",
        "checkout" => "",
        "merge" => "",
        "rebase" => "",
        "reset" => "",
        "pull" => "",
        "clone" => "",
        "branch" => "",
        _ => ""
    };

    /// <summary>
    /// Display string for the selector.
    /// </summary>
    public string DisplaySelector => Selector;
}
