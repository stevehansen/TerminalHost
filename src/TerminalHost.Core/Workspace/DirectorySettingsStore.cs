using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Default <see cref="IDirectorySettingsStore"/> built on top of
/// <see cref="IConfigurationService"/>. Reads/writes
/// <see cref="AppConfiguration.DirectorySettings"/> and
/// <see cref="AppConfiguration.Settings"/>.<c>Repositories.RecentPaths</c>.
/// </summary>
public sealed class DirectorySettingsStore : IDirectorySettingsStore
{
    private readonly IConfigurationService _config;

    public DirectorySettingsStore(IConfigurationService config)
    {
        _config = config;
    }

    public DirectorySettings? Get(string? workingDirectory)
    {
        var key = NormalizeKey(workingDirectory);
        if (key.Length == 0) return null;
        var config = _config.Load();
        return config.DirectorySettings.TryGetValue(key, out var settings) ? settings : null;
    }

    public void Update(string workingDirectory, Action<DirectorySettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var key = NormalizeKey(workingDirectory);
        if (key.Length == 0) return;
        var config = _config.Load();
        if (!config.DirectorySettings.TryGetValue(key, out var settings))
        {
            settings = new DirectorySettings();
        }
        mutate(settings);
        config.DirectorySettings[key] = settings;
        _config.Save(config);
    }

    public void AddRecent(string workingDirectory)
    {
        var canonical = WorkspaceService.NormalizeWorkingDirectory(workingDirectory);
        if (canonical.Length == 0) return;
        var config = _config.Load();
        var recent = config.Settings.Repositories.RecentPaths;
        var max = config.Settings.Repositories.MaxRecentItems;
        recent.RemoveAll(p => p.Equals(canonical, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, canonical);
        while (recent.Count > max)
        {
            recent.RemoveAt(recent.Count - 1);
        }
        _config.Save(config);
    }

    /// <summary>
    /// Computes the dictionary-key form used for <see cref="AppConfiguration.DirectorySettings"/>:
    /// canonical full path, trailing separators stripped, lowercased. Returns
    /// the empty string for null/empty/invalid input — callers can compare
    /// against the empty string instead of try/catching.
    /// </summary>
    /// <remarks>
    /// Public + static because a small number of host call sites work with a
    /// pre-loaded <c>IDictionary&lt;string, DirectorySettings&gt;</c> snapshot
    /// (e.g. <c>RestoreOpenFolders</c>'s deferred-restore optimization) and need
    /// to look up entries directly without reloading the configuration. Those
    /// callers must use the same key form the store uses internally.
    /// </remarks>
    public static string NormalizeKey(string? workingDirectory) =>
        WorkspaceService.NormalizeWorkingDirectory(workingDirectory).ToLowerInvariant();
}
