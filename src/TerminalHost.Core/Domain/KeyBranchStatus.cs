namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents the status of the current branch relative to a key branch (e.g., development, production).
/// </summary>
public class KeyBranchStatus
{
    /// <summary>
    /// The key branch being compared against.
    /// </summary>
    public required GitBranch Branch { get; init; }

    /// <summary>
    /// Number of commits the current branch is ahead of this key branch.
    /// </summary>
    public int AheadCount { get; init; }

    /// <summary>
    /// Number of commits the current branch is behind this key branch.
    /// </summary>
    public int BehindCount { get; init; }

    /// <summary>
    /// Whether fast-forward is possible (current branch can be fast-forwarded to this branch).
    /// True when AheadCount == 0 and BehindCount > 0.
    /// </summary>
    public bool CanFastForward => AheadCount == 0 && BehindCount > 0;

    /// <summary>
    /// Whether this key branch can be fast-forwarded to current (current is purely ahead).
    /// True when BehindCount == 0 and AheadCount > 0.
    /// </summary>
    public bool CanFastForwardKeyToCurrent => BehindCount == 0 && AheadCount > 0;

    /// <summary>
    /// Display string for ahead/behind status (e.g., "↑3 ↓2").
    /// </summary>
    public string StatusDisplay
    {
        get
        {
            if (AheadCount == 0 && BehindCount == 0)
                return "in sync";

            var parts = new List<string>();
            if (AheadCount > 0)
                parts.Add($"↑{AheadCount}");
            if (BehindCount > 0)
                parts.Add($"↓{BehindCount}");
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Whether branches have diverged (both ahead and behind).
    /// </summary>
    public bool HasDiverged => AheadCount > 0 && BehindCount > 0;

    /// <summary>
    /// Whether branches are in sync (neither ahead nor behind).
    /// </summary>
    public bool IsInSync => AheadCount == 0 && BehindCount == 0;
}
