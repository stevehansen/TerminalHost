using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Persists per-directory state (layout, panel visibility, container opt-in)
/// and the recent-folders list. Hides the load-normalize-lookup-save sequence
/// that both hosts duplicated.
/// <para>
/// Step 4d (#48): introduced to remove the <c>NormalizePath</c> / <c>GetDirectorySettings</c>
/// / <c>UpdateRecentFolders</c> trio from <c>MainViewModel</c> and to give
/// <c>SaveDirectorySettings</c> / <c>ToggleContainerForCurrentWorkspace</c> a
/// single load-mutate-save primitive. The dictionary-key form (lowercase, with
/// <see cref="System.IO.Path.GetFullPath(string)"/> trimming) is owned here so
/// callers never compute it.
/// </para>
/// </summary>
public interface IDirectorySettingsStore
{
    /// <summary>
    /// Returns the saved settings for <paramref name="workingDirectory"/>, or
    /// <c>null</c> if the directory has no saved state. Null/empty/invalid
    /// input collapses to <c>null</c> — callers can chain without guards.
    /// </summary>
    DirectorySettings? Get(string? workingDirectory);

    /// <summary>
    /// Loads the configuration, fetches (or creates) the settings entry for
    /// <paramref name="workingDirectory"/>, passes it to <paramref name="mutate"/>,
    /// then saves. No-op if the path normalizes to empty (sentinel/invalid).
    /// </summary>
    /// <remarks>
    /// The mutator receives a live <see cref="DirectorySettings"/> reference and
    /// is expected to assign fields — it does NOT return a new value. This
    /// matches the existing in-place mutation pattern used by both hosts.
    /// </remarks>
    void Update(string workingDirectory, Action<DirectorySettings> mutate);

    /// <summary>
    /// Inserts <paramref name="workingDirectory"/> at the front of
    /// <c>Settings.Repositories.RecentPaths</c> (replacing any case-insensitive
    /// duplicate) and trims the list to <c>MaxRecentItems</c>. The stored value
    /// is the canonical, case-preserving full path (matches the prior
    /// <c>UpdateRecentFolders</c> behavior — recent-list paths are NOT
    /// lowercased even though dictionary keys are).
    /// </summary>
    void AddRecent(string workingDirectory);
}
