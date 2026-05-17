namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Determines which data sources contribute to a <see cref="GitSnapshot"/>.
/// </summary>
public enum GitSnapshotScope
{
    /// <summary>Local-only: status, branch, worktrees. Skips remote/PR lookups.</summary>
    LocalOnly,

    /// <summary>Full: includes pull-request lookup via the GitHub CLI.</summary>
    Full,
}
