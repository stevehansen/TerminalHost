using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Projects per-tab snapshots into the REST API DTOs (<see cref="ApiRepoInfo"/>,
/// <see cref="ApiRepoDetailInfo"/>, <see cref="ApiWorkspaceInfo"/>). Pure function:
/// no state, no IO. Hosts gather inputs from their concrete tab view-models and
/// pass them in.
/// </summary>
public interface IApiStateProjector
{
    /// <summary>
    /// Builds the list of repos for <c>GET /repos</c>. The projector stamps Index
    /// and IsActive based on list position and <paramref name="selectedIndex"/>.
    /// </summary>
    /// <param name="tabs">Per-tab snapshots, in tab order.</param>
    /// <param name="selectedIndex">Index of the selected tab, or -1 if none.</param>
    List<ApiRepoInfo> BuildRepoList(IReadOnlyList<ProjectTabApiState> tabs, int selectedIndex);

    /// <summary>
    /// Builds the detail DTO for <c>GET /repos/{index}</c>, or null if the index
    /// is out of range.
    /// </summary>
    ApiRepoDetailInfo? BuildRepoDetail(IReadOnlyList<ProjectTabApiState> tabs, int selectedIndex, int index);

    /// <summary>
    /// Builds the workspace list for <c>GET /workspaces</c>, matching each workspace
    /// against the open repos by normalized path. The caller supplies the workspace
    /// list (hosts differ on where it's sourced) and the already-projected repo list.
    /// </summary>
    List<ApiWorkspaceInfo> BuildWorkspaceList(IReadOnlyList<Domain.Workspace> workspaces, IReadOnlyList<ApiRepoInfo> openRepos);
}
