using System.Text.RegularExpressions;

namespace TerminalHost.Domain;

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

    // Display helpers
    public string DisplayName => IsRemote ? Name : ShortName;

    // Extract issue number from branch names like "issues/#123" or "issue/#456"
    public string? IssueNumber
    {
        get
        {
            var match = IssuePatternRegex().Match(ShortName);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    [GeneratedRegex(@"^issues?/(#\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IssuePatternRegex();

    public string StatusDisplay
    {
        get
        {
            if (AheadCount == null && BehindCount == null)
                return "";

            var parts = new List<string>();
            if (AheadCount > 0) parts.Add($"↑{AheadCount}");
            if (BehindCount > 0) parts.Add($"↓{BehindCount}");

            return parts.Count > 0 ? string.Join(" ", parts) : "";
        }
    }

    public string StatusColor => IsCurrent ? "#4EC9B0" : (IsRemote ? "#808080" : "#CCCCCC");

    public string Icon => IsCurrent ? "●" : (IsRemote ? "○" : "○");

    public string TypeGroup => IsCurrent ? "Current" : (IsRemote ? "Remote" : "Local");

    // For sorting: current first, then local, then remote
    public int SortOrder => IsCurrent ? 0 : (IsRemote ? 2 : 1);
}
