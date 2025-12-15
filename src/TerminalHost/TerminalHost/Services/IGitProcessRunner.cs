namespace TerminalHost.Services;

public interface IGitProcessRunner
{
    Task<string?> RunGitCommandAsync(string workingDirectory, string arguments);
    Task<GitOperationResult> RunGitOperationAsync(string workingDirectory, string arguments);
}
