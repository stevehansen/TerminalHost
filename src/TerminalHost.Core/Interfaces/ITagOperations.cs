using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface ITagOperations
{
    Task<List<GitTag>> GetTagsAsync();
    Task<GitOperationResult> CreateTagAsync(string tagName, string? message = null, string? commitHash = null);
    Task<GitOperationResult> DeleteTagAsync(string tagName);
    Task<GitOperationResult> PushTagAsync(string tagName);
    Task<GitOperationResult> PushAllTagsAsync();
    Task<GitOperationResult> DeleteRemoteTagAsync(string tagName);
}
