using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IConflictOperations
{
    Task<bool> IsMergeInProgressAsync();
    Task<bool> IsRebaseInProgressAsync();
    Task<ConflictInfo?> ParseConflictFileAsync(string filePath);
    Task<GitOperationResult> MarkResolvedAsync(string filePath);

    Task<GitOperationResult> MergeAbortAsync();
    Task<GitOperationResult> MergeContinueAsync();

    Task<GitOperationResult> RebaseAsync(string ontoBranch);
    Task<GitOperationResult> RebaseContinueAsync();
    Task<GitOperationResult> RebaseAbortAsync();
    Task<GitOperationResult> RebaseSkipAsync();

    Task<GitOperationResult> CherryPickAsync(string commitHash, bool noCommit = false);
    Task<GitOperationResult> CherryPickContinueAsync();
    Task<GitOperationResult> CherryPickAbortAsync();

    Task<GitOperationResult> RevertAsync(string commitHash, bool noCommit = false);
    Task<GitOperationResult> RevertContinueAsync();
    Task<GitOperationResult> RevertAbortAsync();
}
