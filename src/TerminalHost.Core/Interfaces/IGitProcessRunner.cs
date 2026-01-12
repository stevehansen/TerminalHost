using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IGitProcessRunner
{
    Task<string?> RunGitCommandAsync(string workingDirectory, string arguments);
    Task<string?> RunGitCommandAsync(string workingDirectory, string arguments, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<GitOperationResult> RunGitOperationAsync(string workingDirectory, string arguments);
}
