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
}
