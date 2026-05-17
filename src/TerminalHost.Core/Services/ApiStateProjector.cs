using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class ApiStateProjector : IApiStateProjector
{
    public List<ApiRepoInfo> BuildRepoList(IReadOnlyList<ProjectTabApiState> tabs, int selectedIndex)
        => tabs.Select((t, i) => ToRepoInfo(t, i, isActive: i == selectedIndex)).ToList();

    public ApiRepoDetailInfo? BuildRepoDetail(IReadOnlyList<ProjectTabApiState> tabs, int selectedIndex, int index)
    {
        if (index < 0 || index >= tabs.Count) return null;
        var t = tabs[index];
        return new ApiRepoDetailInfo
        {
            Index = index,
            Title = t.Title,
            WorkingDirectory = t.WorkingDirectory,
            IsActive = index == selectedIndex,
            Layout = t.Layout,
            SplitRatio = t.SplitRatio,
            ActiveTerminal = t.ActiveTerminal,
            Git = t.Git,
            Terminals = t.Terminals,
            AiAssistant = t.AiAssistant
        };
    }

    public List<ApiWorkspaceInfo> BuildWorkspaceList(IReadOnlyList<Domain.Workspace> workspaces, IReadOnlyList<ApiRepoInfo> openRepos)
    {
        return workspaces.Select(w =>
        {
            var normalizedPath = NormalizeForMatch(w.Path);
            var matchingRepo = openRepos.FirstOrDefault(r => NormalizeForMatch(r.WorkingDirectory) == normalizedPath);
            return new ApiWorkspaceInfo
            {
                Id = w.Id,
                Name = w.Name,
                Path = w.Path,
                PathId = ApiServer.NormalizePathId(w.Path),
                Section = w.Section,
                IsPinned = w.IsPinned,
                Order = w.Order,
                CustomIcon = w.CustomIcon,
                IsOpen = matchingRepo != null,
                RepoIndex = matchingRepo?.Index,
                ActivityIndicator = matchingRepo?.ActivityIndicator,
                Terminals = matchingRepo?.Terminals,
            };
        }).ToList();
    }

    private static ApiRepoInfo ToRepoInfo(ProjectTabApiState t, int index, bool isActive) => new()
    {
        Index = index,
        Title = t.Title,
        WorkingDirectory = t.WorkingDirectory,
        IsActive = isActive,
        Layout = t.Layout,
        SplitRatio = t.SplitRatio,
        ActiveTerminal = t.ActiveTerminal,
        Git = t.Git,
        Terminals = t.Terminals,
        ActivityIndicator = t.ActivityIndicator
    };

    private static string NormalizeForMatch(string path)
        => path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
}
