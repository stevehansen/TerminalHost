using System.Text.RegularExpressions;

namespace TerminalHost.Core.Domain;

public partial class GitBranch
{
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";  // Without remote prefix for display
    public bool IsCurrent { get; set; }
    public bool IsRemote { get; set; }
    public string? RemoteName { get; set; }  // e.g., "origin"
    public string? TrackingBranch { get; set; }  // e.g., "origin/main"
    public int? AheadCount { get; set; }
    public int? BehindCount { get; set; }
    public bool IsGone { get; set; }  // Tracking branch was deleted on remote

    /// <summary>
    /// Local branch that has never been pushed (no tracking branch configured)
    /// </summary>
    public bool IsLocalOnly => !IsRemote && string.IsNullOrEmpty(TrackingBranch);

    /// <summary>
    /// Local branch whose remote tracking branch has been deleted (e.g., after squash merge)
    /// </summary>
    public bool IsStale => !IsRemote && IsGone;

    // Last commit info
    public string? LastCommitHash { get; set; }
    public string? LastCommitMessage { get; set; }
    public string? LastCommitRelativeDate { get; set; }
    public DateTimeOffset? LastCommitDate { get; set; }

    // Display helpers
    public string DisplayName => IsRemote ? Name : ShortName;

    // Extract issue number from branch names like:
    // - "issues/#123" or "issue/#456" → "#123"
    // - "issues/123" → "#123"
    // - Just "123" or "#123" (the whole branch name) → "#123"
    // - Branches ending with /123 or /#123 → "#123"
    public string? IssueNumber
    {
        get
        {
            // First try: issues/123 or issues/#123 pattern
            var match = IssuePatternRegex().Match(ShortName);
            if (match.Success)
            {
                var num = match.Groups[1].Value;
                return num.StartsWith("#") ? num : "#" + num;
            }

            // Second try: just a number or #number (branch is literally "123" or "#123")
            match = PureNumberRegex().Match(ShortName);
            if (match.Success)
            {
                var num = match.Groups[1].Value;
                return num.StartsWith("#") ? num : "#" + num;
            }

            // Third try: any branch ending in /123 or /#123
            match = TrailingNumberRegex().Match(ShortName);
            if (match.Success)
            {
                var num = match.Groups[1].Value;
                return num.StartsWith("#") ? num : "#" + num;
            }

            return null;
        }
    }

    // Matches: issues/123, issues/#123, issue/456, issue/#456
    [GeneratedRegex(@"^issues?/(#?\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IssuePatternRegex();

    // Matches: just "123" or "#123" as the whole branch name
    [GeneratedRegex(@"^(#?\d+)$")]
    private static partial Regex PureNumberRegex();

    // Matches: anything/123 or anything/#123 at the end
    [GeneratedRegex(@"/(#?\d+)$")]
    private static partial Regex TrailingNumberRegex();

    public string StatusDisplay
    {
        get
        {
            var parts = new List<string>();

            // Show stale indicator for branches whose remote was deleted
            if (IsStale) parts.Add("gone");

            // Show local-only indicator (never pushed)
            if (IsLocalOnly && !IsCurrent) parts.Add("local only");

            if (AheadCount > 0) parts.Add($"↑{AheadCount}");
            if (BehindCount > 0) parts.Add($"↓{BehindCount}");

            return parts.Count > 0 ? string.Join(" ", parts) : "";
        }
    }

    /// <summary>
    /// Color for the status badge based on branch state
    /// </summary>
    public string StatusBadgeColor
    {
        get
        {
            if (IsStale) return "#E06C75";  // Red for stale/gone
            if (IsLocalOnly && !IsCurrent) return "#E5C07B";  // Yellow for local-only
            return "#ABB2BF";  // Default gray
        }
    }

    public string StatusColor => IsCurrent ? "#4EC9B0" : (IsRemote ? "#808080" : "#CCCCCC");

    /// <summary>
    /// Display string for last commit info (e.g., "fix: button styling • 2 hours ago")
    /// </summary>
    public string? LastCommitDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(LastCommitMessage))
                return null;

            if (string.IsNullOrEmpty(LastCommitRelativeDate))
                return LastCommitMessage;

            return $"{LastCommitMessage} • {LastCommitRelativeDate}";
        }
    }

    public string Icon => IsCurrent ? "●" : (IsRemote ? "○" : "○");

    public string TypeGroup => IsCurrent ? "Current" : (IsRemote ? "Remote" : "Local");

    // For sorting: current first, then local, then remote
    public int SortOrder => IsCurrent ? 0 : (IsRemote ? 2 : 1);
}
