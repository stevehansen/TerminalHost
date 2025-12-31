namespace TerminalHost.Core.Domain;

/// <summary>
/// Result of comparing two git branches.
/// </summary>
public class BranchComparisonResult
{
    /// <summary>
    /// The base branch being compared from.
    /// </summary>
    public string BaseBranch { get; set; } = "";

    /// <summary>
    /// The branch being compared to.
    /// </summary>
    public string CompareBranch { get; set; } = "";

    /// <summary>
    /// The merge-base commit hash (common ancestor).
    /// </summary>
    public string MergeBase { get; set; } = "";

    /// <summary>
    /// Commits that exist only in the base branch (not in compare branch).
    /// </summary>
    public List<GitCommit> CommitsOnlyInBase { get; set; } = [];

    /// <summary>
    /// Commits that exist only in the compare branch (not in base branch).
    /// </summary>
    public List<GitCommit> CommitsOnlyInCompare { get; set; } = [];

    /// <summary>
    /// Number of files that differ between the branches.
    /// </summary>
    public int FilesChanged { get; set; }

    /// <summary>
    /// True if the base branch can be fast-forwarded to the compare branch.
    /// (Base branch is an ancestor of compare branch)
    /// </summary>
    public bool CanFastForwardBaseToCompare { get; set; }

    /// <summary>
    /// True if the compare branch can be fast-forwarded to the base branch.
    /// (Compare branch is an ancestor of base branch)
    /// </summary>
    public bool CanFastForwardCompareToBase { get; set; }

    /// <summary>
    /// Number of commits the base branch is ahead of the compare branch.
    /// </summary>
    public int BaseAheadCount => CommitsOnlyInBase.Count;

    /// <summary>
    /// Number of commits the base branch is behind the compare branch.
    /// </summary>
    public int BaseBehindCount => CommitsOnlyInCompare.Count;

    // Aliases for compatibility (properties used for assignment - not computed)
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }
    public List<GitCommit> UniqueCommits { get; set; } = [];
    public List<GitFileStatus> ChangedFiles { get; set; } = [];

    /// <summary>
    /// Summary display of the relationship.
    /// </summary>
    public string Summary
    {
        get
        {
            if (BaseAheadCount == 0 && BaseBehindCount == 0)
                return "Branches are identical";
            if (BaseAheadCount == 0)
                return $"{BaseBranch} is {BaseBehindCount} commit(s) behind {CompareBranch}";
            if (BaseBehindCount == 0)
                return $"{BaseBranch} is {BaseAheadCount} commit(s) ahead of {CompareBranch}";
            return $"{BaseBranch} and {CompareBranch} have diverged ({BaseAheadCount} ahead, {BaseBehindCount} behind)";
        }
    }
}
