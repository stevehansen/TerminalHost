namespace TerminalHost.Domain;

public class GitStatus
{
    public bool IsGitRepository { get; set; }
    public string BranchName { get; set; } = "";
    public bool IsDirty { get; set; }
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }

    // Display helpers
    public string BranchDisplayShort
    {
        get
        {
            if (!IsGitRepository || string.IsNullOrEmpty(BranchName))
                return "";

            var display = $"[{BranchName}";

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
