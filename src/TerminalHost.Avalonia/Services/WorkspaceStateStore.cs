using TerminalHost.Core.Interfaces;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Default <see cref="IWorkspaceStateStore"/> for the Avalonia host. Only
/// <see cref="TerminalPairTabViewModel"/> is persistence-relevant — other
/// tab types fall through to the "first open folder" fallback for
/// <c>LastSelectedFolder</c>.
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

        if (selectedTab is TerminalPairTabViewModel selectedProjectTab)
        {
            config.LastSelectedFolder = selectedProjectTab.Pair.WorkingDirectory;
        }
        else
        {
            config.LastSelectedFolder = config.OpenFolders.FirstOrDefault();
        }

        _config.Save(config);
    }

    public ITabViewModel? FindLastSelectedTab(IEnumerable<ITabViewModel> tabs, string? lastTabType, string? lastSelectedFolder)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var materialized = tabs as IReadOnlyList<ITabViewModel> ?? [.. tabs];

        // Non-empty folder: match by project tab working directory, or leave selection alone.
        if (!string.IsNullOrEmpty(lastSelectedFolder))
        {
            return materialized.OfType<TerminalPairTabViewModel>()
                .FirstOrDefault(t => t.Pair.WorkingDirectory.Equals(lastSelectedFolder, StringComparison.OrdinalIgnoreCase));
        }

        // No folder recorded: fall back to the first available tab.
        return materialized.Count > 0 ? materialized[0] : null;
    }
}
