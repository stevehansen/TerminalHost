namespace TerminalHost.Domain;

/// <summary>
/// Result from the Create Worktree dialog.
/// </summary>
public record CreateWorktreeDialogResult(
    string BranchName,
    string WorktreePath,
    bool CreateNewBranch,
    bool OpenAfterCreation);
