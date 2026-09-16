using TerminalHost.Core.Workspace;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Helpers for building <see cref="PanelScope"/> values keyed by tab working directory.
/// </summary>
/// <remarks>
/// The router treats the scope's <c>TabId</c> as opaque, but <see cref="DirectorySettings"/>
/// lookups require the canonical normalized-and-lowercased path. Centralizing the conversion
/// here keeps every call site consistent with <see cref="DirectorySettingsStore.NormalizeKey"/>.
/// </remarks>
public static class TabPanelScope
{
    /// <summary>
    /// Builds a tab-scoped <see cref="PanelScope"/> for the given working directory,
    /// normalized to the same canonical form used by <see cref="DirectorySettings"/> dictionary keys.
    /// </summary>
    public static PanelScope ForTab(string workingDirectory) =>
        PanelScope.ForTab(WorkspaceService.NormalizeWorkingDirectory(workingDirectory).ToLowerInvariant());
}
