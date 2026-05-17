using System.Collections.Concurrent;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Caches <see cref="IGitWorkspace"/> instances by normalized working directory.
/// Returns null when a path is not inside a git repository (per <see cref="IGitStatusService.GetGitStatusAsync"/>).
/// </summary>
public sealed class GitWorkspaceFactory : IGitWorkspaceFactory
{
    private readonly IGitStatusService _status;
    private readonly IGitHubService _gitHub;
    // Reserved for Phase 2/3: workspace-scoped PR title parsing migrates onto the facade.
    private readonly IGitPrService _gitPr;
    private readonly IGitWorktreeService _worktrees;

    private readonly ConcurrentDictionary<string, IGitWorkspace> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public GitWorkspaceFactory(
        IGitStatusService status,
        IGitHubService gitHub,
        IGitPrService gitPr,
        IGitWorktreeService worktrees)
    {
        _status = status;
        _gitHub = gitHub;
        _gitPr = gitPr;
        _worktrees = worktrees;
    }

    public async Task<IGitWorkspace?> OpenAsync(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return null;

        if (!TryNormalizePath(workingDirectory, out var key))
            return null;

        if (_cache.TryGetValue(key, out var existing))
            return existing;

        // Probe whether this is actually a git repository before caching anything.
        var status = await _status.GetGitStatusAsync(key).ConfigureAwait(false);
        if (!status.IsGitRepository)
            return null;

        var workspace = new GitWorkspace(
            key,
            _status,
            _gitHub,
            _gitPr,
            _worktrees,
            ws => _cache.TryRemove(new KeyValuePair<string, IGitWorkspace>(key, ws)));

        // Race: a concurrent OpenAsync for the same path may have constructed its own
        // workspace and won the GetOrAdd. Dispose our loser to release its SemaphoreSlim.
        var winner = _cache.GetOrAdd(key, workspace);
        if (!ReferenceEquals(winner, workspace))
            await workspace.DisposeAsync().ConfigureAwait(false);
        return winner;
    }

    private static bool TryNormalizePath(string path, out string normalized)
    {
        try
        {
            var full = Path.GetFullPath(path);
            normalized = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            normalized = "";
            return false;
        }
    }
}
