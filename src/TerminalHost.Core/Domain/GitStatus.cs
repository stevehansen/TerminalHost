namespace TerminalHost.Core.Domain;

public class GitStatus
{
    public bool IsGitRepository { get; set; }
    public string BranchName { get; set; } = "";
    public bool IsDirty { get; set; }
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }

    // Common branch name abbreviations
    private static readonly Dictionary<string, string> BranchAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "development", "dev" },
        { "develop", "dev" },
        { "production", "prod" },
        { "staging", "stg" },
        { "release", "rel" },
        { "hotfix", "hf" },
        { "bugfix", "bf" },
        { "feature", "ft" },
        { "master", "main" },  // Optional: keep both short
    };

    // Get abbreviated branch name for compact display
    public string BranchNameShort => GetAbbreviatedBranchName(BranchName);

    private static string GetAbbreviatedBranchName(string branchName, int maxLength = 20)
    {
        if (string.IsNullOrEmpty(branchName))
            return branchName;

        // Check for exact matches first (e.g., "development" -> "dev")
        if (BranchAbbreviations.TryGetValue(branchName, out var abbrev))
            return abbrev;

        // Handle prefixed branches like "feature/xyz" or "issues/123-description"
        var slashIndex = branchName.IndexOf('/');
        if (slashIndex > 0)
        {
            var prefix = branchName[..slashIndex];
            var rest = branchName[(slashIndex + 1)..];

            // Abbreviate the prefix if possible
            if (BranchAbbreviations.TryGetValue(prefix, out var prefixAbbrev))
                prefix = prefixAbbrev;

            // For issue branches like "issues/123", "issues/#123", or "issues/123-description"
            if (prefix.Equals("issues", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("issue", StringComparison.OrdinalIgnoreCase))
            {
                // Strip leading # if present for parsing
                var issueNumber = rest.StartsWith('#') ? rest[1..] : rest;

                var dashIndex = issueNumber.IndexOf('-');
                if (dashIndex > 0 && int.TryParse(issueNumber[..dashIndex], out _))
                {
                    // Keep issue number + first word for context
                    var afterNumber = issueNumber[(dashIndex + 1)..];
                    var nextDash = afterNumber.IndexOf('-');
                    var firstWord = nextDash > 0 ? afterNumber[..nextDash] : afterNumber;
                    if (firstWord.Length > 8) firstWord = firstWord[..8];
                    return $"#{issueNumber[..dashIndex]}-{firstWord}";
                }
                // Just the issue number (e.g., "issues/123" or "issues/#123" -> "#123")
                if (int.TryParse(issueNumber, out _))
                {
                    return $"#{issueNumber}";
                }
            }

            // Truncate the rest if too long
            if (rest.Length > maxLength - prefix.Length - 1)
            {
                rest = rest[..(maxLength - prefix.Length - 4)] + "...";
            }

            return $"{prefix}/{rest}";
        }

        // Simple truncation for other long names
        if (branchName.Length > maxLength)
            return branchName[..(maxLength - 3)] + "...";

        return branchName;
    }

    // Display helpers
    public string BranchDisplayShort
    {
        get
        {
            if (!IsGitRepository || string.IsNullOrEmpty(BranchName))
                return "";

            var display = $"[{BranchNameShort}";

            if (IsDirty)
                display += " *";
            else if (AheadCount > 0 || BehindCount > 0)
            {
                if (AheadCount > 0) display += $" ↑{AheadCount}";
                if (BehindCount > 0) display += $" ↓{BehindCount}";
            }

            return display + "]";
        }
    }

    public string StatusDisplayFull
    {
        get
        {
            if (!IsGitRepository || string.IsNullOrEmpty(BranchName))
                return "";

            var parts = new List<string> { $"🌿 {BranchName}" };

            if (AheadCount > 0 || BehindCount > 0)
            {
                var sync = "";
                if (AheadCount > 0) sync += $"{AheadCount}↑";
                if (AheadCount > 0 && BehindCount > 0) sync += " ";
                if (BehindCount > 0) sync += $"{BehindCount}↓";
                parts.Add(sync);
            }

            if (IsDirty)
                parts.Add("modified");

            return string.Join(" • ", parts);
        }
    }
}
