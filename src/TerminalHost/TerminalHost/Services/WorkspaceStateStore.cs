using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Default <see cref="IWorkspaceStateStore"/> for the WPF host. Knows about
/// the five tab types that map to <c>LastSelectedTabType</c>
/// (<c>Project</c>, <c>Dashboard</c>, <c>Timeline</c>, <c>Settings</c>,
/// <c>Statistics</c>) and falls back to <c>Project</c> for anything else.
/// </summary>
public sealed class WorkspaceStateStore : IWorkspaceStateStore
{
    private readonly IConfigurationService _config;

    public WorkspaceStateStore(IConfigurationService config)
    {
        _config = config;
    }

    public void SaveOpenFolders(IEnumerable<ITabViewModel> tabs, ITabViewModel? selectedTab)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var config = _config.Load();

        config.OpenFolders = [.. tabs.OfType<TerminalPairTabViewModel>().Select(t => t.Pair.WorkingDirectory)];

        switch (selectedTab)
        {
            case TerminalPairTabViewModel selectedProjectTab:
                config.LastSelectedTabType = "Project";
                config.LastSelectedFolder = selectedProjectTab.Pair.WorkingDirectory;
                break;
            case DashboardTabViewModel:
                config.LastSelectedTabType = "Dashboard";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            case TimelineTabViewModel:
                config.LastSelectedTabType = "Timeline";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            case SettingsTabViewModel:
                config.LastSelectedTabType = "Settings";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            case StatisticsTabViewModel:
                config.LastSelectedTabType = "Statistics";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
            default:
                config.LastSelectedTabType = "Project";
                config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
                break;
        }

        _config.Save(config);
    }

    public ITabViewModel? FindLastSelectedTab(IEnumerable<ITabViewModel> tabs, string? lastTabType, string? lastSelectedFolder)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var materialized = tabs as IReadOnlyCollection<ITabViewModel> ?? [.. tabs];

        switch (lastTabType)
        {
            case "Dashboard":
                return materialized.OfType<DashboardTabViewModel>().FirstOrDefault();
            case "Timeline":
                return materialized.OfType<TimelineTabViewModel>().FirstOrDefault();
            case "Project":
            case null:
            case "":
            default:
                if (string.IsNullOrEmpty(lastSelectedFolder)) return null;
                return materialized.OfType<TerminalPairTabViewModel>()
                    .FirstOrDefault(t => t.Pair.WorkingDirectory.Equals(lastSelectedFolder, StringComparison.OrdinalIgnoreCase));
        }
    }
}
