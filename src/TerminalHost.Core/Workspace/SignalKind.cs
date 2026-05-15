namespace TerminalHost.Core.Workspace;

/// <summary>
/// Per-project monitoring signals. Each flag corresponds to one of the periodic
/// refresh paths that <see cref="IProjectMonitor"/> coordinates.
/// </summary>
[Flags]
public enum SignalKind
{
    None = 0,
    GitStatus = 1,
    GitAutoFetch = 2,
    Activity = 4,
    Links = 8,
    RunUrl = 16,
    All = GitStatus | GitAutoFetch | Activity | Links | RunUrl
}
