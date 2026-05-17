namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Opens (and caches) an <see cref="IGitWorkspace"/> per normalized working directory.
/// Returns null when the requested path is not inside a git repository.
/// </summary>
public interface IGitWorkspaceFactory
{
    Task<IGitWorkspace?> OpenAsync(string workingDirectory);
}
