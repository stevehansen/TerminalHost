namespace TerminalHost.Core.Domain;

public class BranchTreeNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public GitBranch? Branch { get; set; }
    public List<BranchTreeNode> Children { get; set; } = [];
    public bool IsExpanded { get; set; } = true;
    public int BranchCount { get; set; }

    public string DisplayIcon => IsFolder ? "\U0001F4C1" : (Branch?.IsCurrent == true ? "\u2713" : (Branch?.IsRemote == true ? "\u2601" : "\u2192"));
    public bool IsCurrent => Branch?.IsCurrent == true;
    public string CountDisplay => IsFolder ? $"({BranchCount})" : "";
    public string StatusColor => Branch?.StatusColor ?? "#CCCCCC";

    // Additional info from branch (for tree view parity with flat list)
    public string? IssueNumber => Branch?.IssueNumber;
    public string StatusDisplay => Branch?.StatusDisplay ?? "";
    public string? LastCommitDisplay => Branch?.LastCommitDisplay;
    public bool HasStatusInfo => !string.IsNullOrEmpty(StatusDisplay) || !string.IsNullOrEmpty(IssueNumber);
}
