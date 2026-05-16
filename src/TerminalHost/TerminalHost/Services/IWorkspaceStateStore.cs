using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Persists workspace-level state across restarts: which project folders are
/// open, and which tab was last selected (with the folder it referenced).
/// Sibling to <see cref="TerminalHost.Core.Workspace.IDirectorySettingsStore"/>,
/// which handles per-directory state.
/// <para>
/// Step 4f (#48): introduced to remove <c>SaveOpenFolders</c> from
/// <c>MainViewModel</c>. The host-specific switch over <c>SelectedTab</c>
/// (Project / Dashboard / Timeline / Settings / Statistics) lives in the
/// adapter, not the caller. WPF has more tab types than Avalonia, so the
/// port stays host-specific — analogous to <c>ITabFactory</c>.
/// </para>
/// </summary>
public interface IWorkspaceStateStore
{
    /// <summary>
    /// Persists the open project folders (derived from <paramref name="tabs"/>)
    /// and the selected-tab kind + folder (derived from <paramref name="selectedTab"/>).
    /// The configuration is loaded and saved once per call.
    /// </summary>
    void SaveOpenFolders(IEnumerable<ITabViewModel> tabs, ITabViewModel? selectedTab);
}
