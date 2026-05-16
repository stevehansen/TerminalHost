using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Persists workspace-level state across restarts: which project folders are
/// open and which folder was last selected. Sibling to
/// <see cref="TerminalHost.Core.Workspace.IDirectorySettingsStore"/>, which
/// handles per-directory state.
/// <para>
/// Step 4f (#48): introduced to remove <c>SaveOpenFolders</c> from
/// <c>MainViewModel</c>. Host-specific because the WPF host has more tab
/// types (Dashboard / Timeline / Settings / Statistics) than the Avalonia
/// host, which only persists project folder selection.
/// </para>
/// </summary>
public interface IWorkspaceStateStore
{
    /// <summary>
    /// Persists the open project folders (derived from <paramref name="tabs"/>)
    /// and <c>LastSelectedFolder</c> (the selected tab's working directory if
    /// it is a project tab, otherwise the first open folder).
    /// </summary>
    void SaveOpenFolders(IEnumerable<ITabViewModel> tabs, ITabViewModel? selectedTab);
}
