using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface ISubmoduleOperations
{
    /// <summary>
    /// Gets the list of submodules with their current status.
    /// </summary>
    Task<List<SubmoduleInfo>> GetSubmodulesAsync();

    /// <summary>
    /// Initializes a submodule.
    /// </summary>
    Task<GitOperationResult> InitializeAsync(string submodulePath);

    /// <summary>
    /// Updates a submodule to the tracked commit.
    /// </summary>
    Task<GitOperationResult> UpdateAsync(string submodulePath);

    /// <summary>
    /// Updates a submodule to the latest commit from remote (--remote).
    /// </summary>
    Task<GitOperationResult> UpdateToLatestAsync(string submodulePath);
}
